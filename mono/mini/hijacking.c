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

/* Back definitions from tasklets.c because the function definitions there
 * are declared static and only accessible via an icall (which would be
 * counterproductive to use since we are on unmanaged side)
 */
/*#include "tasklets.h"

extern void* continuation_alloc (void);
extern void continuation_free (MonoContinuation* cont);
extern MonoException* continuation_mark_frame_num (MonoContinuation *, int, MonoMethod**);
extern int continuation_store (MonoContinuation *cont, int state, MonoException **e);
extern MonoException* continuation_restore (MonoContinuation *cont, int state);*/

/* This struct forms a linked list of instruction on when to held.
 * Each node contains a number that is decremented at each instruction,
 * when it goes to 0, the node is discarded and the hijack method calls
 * Yield, the process repeat until there is no more node which should
 * correspond to the moment when the method body is finished
 */
typedef struct _HijackExecutionSlot {
	int initial_count;
	int no_yielding_count;
	struct _HijackExecutionSlot* next;
} HijackExecutionSlot;

typedef struct {
	const char* name;
	int number_injected_calls;
	/* GList<HijackExecutionSlot*> */
	GList* execution_slots;
	GList* current_execution_slot;
	HijackExecutionSlot* current_slot;
} HijackMethodInfo;


extern void register_icall (gpointer func, const char *name, const char *sigstr, gboolean save);

static int hijacking = FALSE;
static int total_num_method;

/* Method pointer is used as a key, since it *shouldn't* change in our workflow
 * it safe to use it like this. Values is a struct containing interesting values
 * later used to generate the interleavings scheme we want.
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
mono_enable_hijack_code_num (int num_method)
{
	hijacking = TRUE;
	total_num_method = num_method;
}

void
mono_disable_hijack_code ()
{
	hijacking = FALSE;
}

static void
internal_iterator_method (gpointer key, gpointer value, gpointer user_data)
{
	HijackMethodInfo* method = (HijackMethodInfo*)value;
	HijackExecutionSlot* slot = method->current_execution_slot->data;
	
	while (slot != NULL) {
		printf ("%d", slot->initial_count);

		if (slot->next != NULL)
			printf ("-");
		slot = slot->next;
	}
	puts ("");
}

static void
mono_hijack_print_current_interleaving ()
{
	g_hash_table_foreach (hijack_methodinfos_storage, internal_iterator_method, NULL);
}

static HijackExecutionSlot*
mono_hijack_slot_insert_yield (HijackExecutionSlot* initial, int index)
{
	HijackExecutionSlot* result = g_new0 (HijackExecutionSlot, 1);
	HijackExecutionSlot* current = result;
	HijackExecutionSlot* temp = NULL;

	while (initial->no_yielding_count < index) {
		index -= initial->no_yielding_count;
		current->no_yielding_count = current->initial_count = initial->no_yielding_count;
		temp = g_new0 (HijackExecutionSlot, 1);
		current->next = temp;
		current = temp;
		initial = initial->next;
	}

	current->no_yielding_count = current->initial_count = index;
	temp = g_new0 (HijackExecutionSlot, 1);
	temp->no_yielding_count = temp->initial_count = initial->no_yielding_count - index;
	current->next = temp;

	return result;
}

static GList*
mono_hijack_generate_interleaving (HijackExecutionSlot* initial, int start_index, int length)
{
	GList* list = NULL;
	GList* c = NULL;
	int i = 0;
	int count = length - start_index;

	if (start_index >= length)
		return NULL;

	for (i = start_index; i < length; ++i) {
		list = g_list_append (list, mono_hijack_slot_insert_yield (initial, i));
	}

	c = list;
	for (i = 0; i < count; ++i) {
		list = g_list_concat (list, mono_hijack_generate_interleaving (c->data, i + start_index + 1, length));
		c = g_list_next (c);
	}

	return list;
}

static void
mono_hijack_generate_interleavings (HijackMethodInfo* methodinfo)
{
	int num_injected = methodinfo->number_injected_calls;
	HijackExecutionSlot* slot = g_new0 (HijackExecutionSlot, 1);
	GList* list = NULL;

	slot->no_yielding_count = num_injected;
	list = mono_hijack_generate_interleaving (slot, 1, num_injected);
	methodinfo->execution_slots = list;

	printf ("Generated %d interleavings based on %d instructions\n", g_list_length (list), num_injected);
	fflush (stdout);
}

/*static void
mono_hijack_generate_interleavings (HijackMethodInfo* methodinfo)
{
	int num_injected = methodinfo->number_injected_calls;
	HijackExecutionSlot* slot = NULL, *other = NULL;
	int i = 0;

	for (i = 1; num_injected > 0; ++i) {
		slot = g_new0 (HijackExecutionSlot, 1);
		slot->no_yielding_count = MIN (i, num_injected);
		num_injected = MAX (0, num_injected - i);

		if (methodinfo->execution_slots == NULL) {
			methodinfo->execution_slots = slot;
		} else {
			other = methodinfo->execution_slots;
			while (other->next != NULL)
				other = other->next;

			other->next = slot;
		}
	}
	}*/

 /*static void print_hijack_execution_slot (HijackExecutionSlot* slot)
{
	int i = 0;
	
	while (slot != NULL) {
		for (i = 0; i < slot->no_yielding_count; i++)
			printf ("*");

		if (slot->next != NULL)
			printf ("-");
		slot = slot->next;
	}
	puts ("");
	}*/

static void
hijack_func (HijackMethodInfo* method)
{
	static MonoMethod* scheduler_yield_method = NULL;
	static MonoMethod* scheduler_stop_method = NULL;
	HijackExecutionSlot* slot = NULL;

	if (scheduler_yield_method == NULL) {
		/* Find Scheduler.Yield static method */
		MonoAssemblyName* name = mono_assembly_name_new ("HeisenLib");
		MonoAssembly* assembly = mono_assembly_loaded (name);
		MonoImage* image = mono_assembly_get_image (assembly);
		MonoMethodDesc* yield_desc = mono_method_desc_new ("Heisen.Scheduler:Yield()", TRUE);
		MonoMethodDesc* stop_desc = mono_method_desc_new ("Heisen.Scheduler:Stop()", TRUE);
		scheduler_yield_method = mono_method_desc_search_in_image (yield_desc, image);
		scheduler_stop_method = mono_method_desc_search_in_image (stop_desc, image);
		printf ("Scheduler method initialized correctly? %s %s\n",
		        scheduler_yield_method != NULL ? "Yes" : "No",
		        scheduler_stop_method != NULL ? "Yes" : "No");
	}

	if (method->execution_slots == NULL)
		mono_hijack_generate_interleavings (method);

	if (method->current_execution_slot == NULL) {
		slot = method->current_slot = (method->current_execution_slot = method->execution_slots)->data;
	} else if (method->current_slot == NULL) {
		//puts ("Changing");
		method->current_execution_slot = g_list_next (method->current_execution_slot);
		if (method->current_execution_slot == NULL) {
			//puts ("Stopping");
			mono_runtime_invoke (scheduler_stop_method, NULL, NULL, NULL);
			return;
		}
		slot = method->current_slot = method->current_execution_slot->data;
	} else {
		slot = method->current_slot;
	}

	if (slot == NULL) {
		puts ("Hu?");
		return;
	}

	/*printf ("Method %s ", method->name);
	  print_hijack_execution_slot (slot);*/

	if (!slot->no_yielding_count) {
		method->current_slot = slot->next;
		//g_free (slot);
		//puts ("Yielding");
		mono_runtime_invoke (scheduler_yield_method, NULL, NULL, NULL);
	} else {
		slot->no_yielding_count--;
	}

	//printf ("Method %s, no_yielding_count is %d\n", method->name, slot->no_yielding_count);
}

void
mono_hijack_init ()
{
	hijack_methodinfos_storage = g_hash_table_new (NULL, NULL);

	register_icall (hijack_func, "hijack_func", "void ptr", TRUE);
	
	mono_add_internal_call ("Heisen.RuntimeManager::mono_enable_hijack_code",
	                        mono_enable_hijack_code);
	mono_add_internal_call ("Heisen.RuntimeManager::mono_enable_hijack_code_num",
	                        mono_enable_hijack_code_num);
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

	/*puts (cfg->method->name);
	  fflush (stdout);*/

	if (!(methodinfo = g_hash_table_lookup (hijack_methodinfos_storage, cfg->method))) {
		methodinfo = g_new0 (HijackMethodInfo, 1);
		methodinfo->number_injected_calls = 1;
		methodinfo->name = cfg->method->name;
		g_hash_table_insert (hijack_methodinfos_storage, cfg->method, methodinfo);
	} else {
		methodinfo->number_injected_calls++;
	}

	EMIT_NEW_PCONST (cfg, arg[0], methodinfo);
	mono_emit_jit_icall (cfg, hijack_func, arg);
}


