#include <config.h>
#include <signal.h>

#ifdef HAVE_UNISTD_H
#include <unistd.h>
#endif

#include <math.h>
#include <string.h>
#include <ctype.h>

#ifdef HAVE_SYS_TIME_H
#include <sys/time.h>
#endif

#ifdef HAVE_ALLOCA_H
#include <alloca.h>
#endif

#include <mono/utils/memcheck.h>

#include <mono/metadata/assembly.h>
#include <mono/metadata/loader.h>
#include <mono/metadata/tabledefs.h>
#include <mono/metadata/class.h>
#include <mono/metadata/object.h>
#include <mono/metadata/exception.h>
#include <mono/metadata/opcodes.h>
#include <mono/metadata/mono-endian.h>
#include <mono/metadata/tokentype.h>
#include <mono/metadata/tabledefs.h>
#include <mono/metadata/marshal.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/mono-debug.h>
#include <mono/metadata/gc-internal.h>
#include <mono/metadata/security-manager.h>
#include <mono/metadata/threads-types.h>
#include <mono/metadata/security-core-clr.h>
#include <mono/metadata/monitor.h>
#include <mono/metadata/profiler-private.h>
#include <mono/metadata/profiler.h>
#include <mono/utils/mono-compiler.h>
#include <mono/metadata/mono-basic-block.h>

#include <mono/metadata/threads.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/io-layer/io-layer.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/environment.h>
#include <mono/metadata/mono-debug.h>
#include <mono/metadata/gc-internal.h>
#include <mono/metadata/threads-types.h>
#include <mono/metadata/verify.h>
#include <mono/metadata/verify-internals.h>
#include <mono/metadata/mempool-internals.h>
#include <mono/metadata/attach.h>
#include <mono/metadata/runtime.h>
#include <mono/utils/mono-math.h>
#include <mono/utils/mono-logger-internal.h>
#include <mono/utils/mono-mmap.h>
#include <mono/utils/dtrace.h>

#include "mini.h"
#include "trace.h"

#include "ir-emit.h"

#include "jit-icalls.h"
#include "jit.h"
#include "debugger-agent.h"

/* This struct forms a linked list of instruction on when to yield.
 * Each node contains a number that is decremented at each instruction,
 * when it goes to 0, the node is discarded and the hijack method calls
 * Yield, the process repeat until there is no more node which should
 * correspond to the moment when the method body is finished
 */
typedef struct _HijackInterleaving {
	int initial_count;
	int no_yielding_count;
	struct _HijackInterleaving* next;
} HijackInterleaving;

typedef struct {
	const char* name;
	int number_injected_calls;
	int number_interleaving;

	/* GList<HijackInterleaving*>[] - array keyed by number of yield point to a list of interleavings */
	GList** execution_slots;

	/* These fields are changed during the execution of the method */
	int current_yield_count; /* used as execution_slots index for enumeration */
	GList* current_execution_slot; /* iterating through the list of HijackInterleaving */
	HijackInterleaving* current_interleaving; /* Iteration ptr to the current block of the current interleaving */
	int current_num_method; /* Used as an index to tell where the method is in the interleaving call chain */
	int current_call_number; /* Where this methodinfo is in the test call chain, determined by scheduler execution order */
	int current_neighbours_interleaving_count; /* How much execution we have for a single interleaving */
} HijackMethodInfo;

typedef struct {
	GList* c_execution_slot;
	HijackInterleaving* c_slot;
	int c_num_method;
	int c_call_number;
	int c_neighbours_interleaving_count;
} HijackMethodInfoSave;

#define SAVE_CONTEXT(save, method) \
	save.c_slot = method->current_interleaving; \
	save.c_execution_slot = method->current_execution_slot; \
	save.c_num_method = method->current_num_method; \
	save.c_call_number = method->current_call_number; \
	save.c_neighbours_interleaving_count = method->current_neighbours_interleaving_count; \
	method->current_execution_slot = NULL; \
	method->current_interleaving = NULL; \

#define RESTORE_CONTEXT(save, method) \
	method->current_interleaving = save.c_slot; \
	method->current_execution_slot = save.c_execution_slot; \
	method->current_num_method = save.c_num_method; \
	method->current_call_number = save.c_call_number; \
	method->current_neighbours_interleaving_count = save.c_neighbours_interleaving_count;

extern void register_icall (gpointer func, const char *name, const char *sigstr, gboolean save);

static int hijacking = FALSE;
static int total_num_method = 0;
static MonoMethod* scheduler_yield_method = NULL;
static MonoMethod* scheduler_stop_method = NULL;
static int current_num_method_cache = 0;
static int current_method_yield_points = 0;

/* Method pointer is used as a key, since it *shouldn't* change in our workflow
 * it is safe to use it like this. Values is a struct containing interesting 
 * values later used to generate the interleavings scheme we want.
 */
static GHashTable* hijack_methodinfos_storage = NULL;

int
mono_is_hijacking_enabled ()
{
	return hijacking;
}

void
mono_enable_hijack_code ()
{
	hijacking = TRUE;
}

static void
mono_hijack_set_num_methods (int num_method)
{
	total_num_method = num_method;
	current_num_method_cache = 0;
}

void
mono_disable_hijack_code ()
{
	hijacking = FALSE;
}

#define DUMP_INTERLEAVING(s) while (s != NULL) { printf ("%d", s->initial_count); if (s->next != NULL) printf ("-"); s = s->next; }

static void
internal_iterator_method (gpointer key, gpointer value, gpointer user_data)
{
	HijackMethodInfo* method = (HijackMethodInfo*)value;
	HijackInterleaving* slot = method->current_execution_slot->data;
	
	DUMP_INTERLEAVING (slot);
	puts ("");
}

static void
mono_hijack_print_current_interleaving (void)
{
	g_hash_table_foreach (hijack_methodinfos_storage, internal_iterator_method, NULL);
}

static HijackInterleaving*
mono_hijack_slot_insert_yield (HijackInterleaving* initial, int index)
{
	HijackInterleaving* result = g_new0 (HijackInterleaving, 1);
	HijackInterleaving* current = result;
	HijackInterleaving* temp = NULL;

	while (initial->no_yielding_count < index) {
		index -= initial->no_yielding_count;
		current->no_yielding_count = current->initial_count = initial->no_yielding_count;
		temp = g_new0 (HijackInterleaving, 1);
		current->next = temp;
		current = temp;
		initial = initial->next;
	}

	current->no_yielding_count = current->initial_count = index;
	temp = g_new0 (HijackInterleaving, 1);
	temp->no_yielding_count = temp->initial_count = initial->no_yielding_count - index;
	current->next = temp;

	return result;
}

static int
mono_hijack_generate_interleavings_internal (HijackInterleaving* initial, GList** array, int start_index, int length, int depth)
{
	GList* list = NULL;
	GList* c = NULL;
	int i = 0;
	int count = length - start_index;
	int total_size = count;

	if (start_index >= length)
		return 0;

	for (i = start_index; i < length; ++i) {
		list = g_list_prepend (list, mono_hijack_slot_insert_yield (initial, i));
	}

	list = g_list_reverse (list);
	array[depth] = g_list_concat (list, array[depth]);

	c = list;
	for (i = 0; i < count; ++i) {
		total_size += mono_hijack_generate_interleavings_internal (c->data, array, i + start_index + 1, length, depth + 1);
		c = g_list_next (c);
	}

	return total_size;
}

static void
mono_hijack_generate_interleavings (HijackMethodInfo* methodinfo)
{
	int num_injected = methodinfo->number_injected_calls;
	/* Solely used as the initial slot to start the recursive creation of the real interleavings */
	HijackInterleaving* slot = g_new0 (HijackInterleaving, 1);
	GList** array = g_malloc0 (sizeof (GList*) * num_injected - 1);
	int length = 0;

	slot->no_yielding_count = num_injected;
	length = mono_hijack_generate_interleavings_internal (slot, array, 1, num_injected, 0);
	methodinfo->execution_slots = array;
	methodinfo->number_interleaving = length;
	g_free (slot);

	printf ("Generated %d interleavings based on %d instructions\n", length, num_injected);
	//puts ("Dumping content:");
	/*for (i = 0; i < num_injected - 1; i++) {
		foo = array[i];
		while (foo != NULL) {
			slot = foo->data;
			DUMP_INTERLEAVING (slot);
			foo = g_list_next (foo);
			puts ("");
		}

		puts("\n");
		}*/
		
	fflush (stdout);
}

static void
mono_hijack_reset_interleaving (HijackInterleaving* interleaving)
{
	while (interleaving != NULL) {
		interleaving->no_yielding_count = interleaving->initial_count;
		interleaving = interleaving->next;
	}
}

static void
hijack_func (HijackMethodInfo* m)
{
	HijackInterleaving* slot = m->current_interleaving;
	HijackMethodInfoSave save;

	g_return_if_fail (slot != NULL);

	if (slot->no_yielding_count == 0) {
		m->current_interleaving = slot->next;
		/* We save our current progress into a special save variable on the stack
		 * and reset the methodinfos so that if after yielding we end up executing
		 * the same method (think [HeisenTestMethod(Duplicate=2)]) the second invocation
		 * will think it's the first and init itself correctly. When the call to Yield
		 * return, we restore the state from our save
		 */
		SAVE_CONTEXT (save, m);
		mono_runtime_invoke (scheduler_yield_method, NULL, NULL, NULL);
		RESTORE_CONTEXT (save, m);
	} else {
		slot->no_yielding_count--;
	}
}

static int
count_needed_neighbor_interleaving (HijackMethodInfo* m)
{
	GHashTableIter iter;
	HijackMethodInfo* infos;
	int num_execution_needed = 1;

	if (m->current_neighbours_interleaving_count > 0)
		return m->current_neighbours_interleaving_count;

	g_hash_table_iter_init (&iter, hijack_methodinfos_storage);
	while (g_hash_table_iter_next (&iter, NULL, (void**)&infos)) {
		if (infos->current_num_method <= m->current_num_method)
			continue;

		num_execution_needed *= g_list_length (infos->execution_slots[current_method_yield_points]);
	}
	//printf ("Neighbouring interleaving for method %d are %d\n", num_method, num_execution_needed);

	return (m->current_neighbours_interleaving_count = num_execution_needed);
}

/* Only run once at the beginning each the specific method is executed, allow to execute a bunch
 * of initialization, cleanup, saving actions
 * Careful though, depending on the code that is instrumented, a goto instruction (from a loop for example)
 * could end up executing this method more than once so protect what is sensitive.
 */
static void
hijack_func_first (HijackMethodInfo* m)
{
	if (scheduler_yield_method == NULL) {
		/* Find Scheduler.Yield static method */
		MonoAssemblyName* name = mono_assembly_name_new ("Heisen");
		MonoAssembly* assembly = mono_assembly_loaded (name);
		MonoImage* image = mono_assembly_get_image (assembly);
		MonoMethodDesc* yield_desc = mono_method_desc_new ("Heisen.Scheduler:Yield()", TRUE);
		MonoMethodDesc* stop_desc = mono_method_desc_new ("Heisen.Scheduler:Stop()", TRUE);
		scheduler_yield_method = mono_method_desc_search_in_image (yield_desc, image);
		scheduler_stop_method = mono_method_desc_search_in_image (stop_desc, image);
		if (scheduler_yield_method == NULL || scheduler_stop_method == NULL)
			printf ("Scheduler method initialized correctly? %s %s\n",
			        scheduler_yield_method != NULL ? "Yes" : "No",
			        scheduler_stop_method != NULL ? "Yes" : "No");
	}

	if (m->execution_slots == NULL)
		mono_hijack_generate_interleavings (m);

	if (m->current_num_method == -1)
		m->current_num_method = current_num_method_cache++;

	/* First pass in the hijack_func method, we initialize the GList traversal pointer */
	if (m->current_execution_slot == NULL) {
		m->current_interleaving = (m->current_execution_slot = m->execution_slots[current_method_yield_points])->data;
	/* Start of the invocation i.e. the current interleaving is finished, time to get a new one (or go back at the beginning) */
	} else {
		mono_hijack_reset_interleaving (m->current_execution_slot->data);
		/* If our method has been called sufficiently depending on its place in the call chain then change interleaving 
		 * TODO: cache function call result
		 */
		if (count_needed_neighbor_interleaving (m) <= ++m->current_call_number) {
			m->current_call_number = 0;
			m->current_execution_slot = g_list_next (m->current_execution_slot);
			/* No more interleaving for us? If we are the master executing method it means the test is finished
			 * if not we go back at the beginning, it simply means we are at the end of this call chain interleaving
			 */
			if (m->current_execution_slot == NULL) {
				if (m->current_num_method == 0) {
					if ((current_method_yield_points = m->current_yield_count++) >= m->number_injected_calls - 1) {
						mono_runtime_invoke (scheduler_stop_method, NULL, NULL, NULL);
						return;
					} else {
						m->current_execution_slot = m->execution_slots[current_method_yield_points];
						m->current_neighbours_interleaving_count = 0;
					}
				} else {
					m->current_execution_slot = m->execution_slots[current_method_yield_points];
				}
			}
		}

		//printf ("Going with following execution slot for %s(%d)\n", m->name, m->current_num_method);
		//internal_iterator_method (NULL, m, NULL);
		/* In all case we set back current_interleaving to a correct value at some start of an execution slot */
		m->current_interleaving = m->current_execution_slot->data;
	}

	/*printf ("Going with following execution slot for %s(%d)\n", m->name, m->current_num_method);
	  internal_iterator_method (NULL, m, NULL);*/

	hijack_func (m);
}

void
mono_hijack_init ()
{
	hijack_methodinfos_storage = g_hash_table_new (NULL, NULL);

	register_icall (hijack_func, "hijack_func", "void ptr", TRUE);
	register_icall (hijack_func_first, "hijack_func_first", "void ptr", TRUE);
	
	mono_add_internal_call ("Heisen.RuntimeManager::mono_enable_hijack_code",
	                        mono_enable_hijack_code);
	mono_add_internal_call ("Heisen.RuntimeManager::mono_hijack_set_num_methods",
	                        mono_hijack_set_num_methods);
	mono_add_internal_call ("Heisen.RuntimeManager::mono_disable_hijack_code",
	                        mono_disable_hijack_code);
	mono_add_internal_call ("Heisen.RuntimeManager::mono_hijack_print_current_interleaving",
	                        mono_hijack_print_current_interleaving);
}

void
mono_emit_hijack_code (MonoCompile *cfg)
{
	MonoInst* arg[1];
	char* full_name = NULL;
	HijackMethodInfo* methodinfo = NULL;

	/* Skip corlib for now and avoid problems */
	if (cfg->method->klass->image == mono_defaults.corlib)
		return;

	/* Also skip heisen branded method (e.g. DisableInjection) */
	if (g_str_has_prefix ((full_name = mono_type_get_full_name (cfg->method->klass)), "Heisen"))
		return;	

	if (g_str_has_prefix (full_name, "System"))
		return;

	if (g_str_has_prefix (full_name, "Mono"))
		return;

	if (!(methodinfo = g_hash_table_lookup (hijack_methodinfos_storage, cfg->method))) {
		methodinfo = g_new0 (HijackMethodInfo, 1);
		methodinfo->number_injected_calls = 1;
		methodinfo->name = cfg->method->name;
		methodinfo->current_num_method = -1;
		g_hash_table_insert (hijack_methodinfos_storage, cfg->method, methodinfo);

		EMIT_NEW_PCONST (cfg, arg[0], methodinfo);
		mono_emit_jit_icall (cfg, hijack_func_first, arg);
	} else {
		methodinfo->number_injected_calls++;

		EMIT_NEW_PCONST (cfg, arg[0], methodinfo);
		mono_emit_jit_icall (cfg, hijack_func, arg);
	}
}

