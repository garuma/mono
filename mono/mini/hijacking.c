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
#include <mono/metadata/marshal.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/mono-debug.h>
#include <mono/metadata/debug-mono-symfile.h>

#include "mini.h"
#include "trace.h"

#include "ir-emit.h"

#include "jit-icalls.h"
#include "jit.h"
#include "debugger-agent.h"

#include "hijacking.h"

extern void register_icall (gpointer func, const char *name, const char *sigstr, gboolean save);

static int hijacking = FALSE;
static int total_num_method = 0;
/* This call the actual Yield logic inside the scheduler and start/resume another task execution */
static MonoMethod* scheduler_yield_method = NULL;
/* Totally stop the scheduler meaning the current test fixture has passed successfully */
static MonoMethod* scheduler_stop_method = NULL;
/* In case the current interleaving needs to be expanded into a special set, tell the scheduler to restart */
static MonoMethod* scheduler_force_restart_method = NULL;
static int current_num_method_cache = 0;
static int current_method_yield_points = 0;

/* Method pointer is used as a key, since it *shouldn't* change in our workflow
 * it is safe to use it like this. Values is a struct containing interesting 
 * values later used to generate the interleavings scheme we want.
 */
/* GHashTable<MonoMethod*, HijackMethodInfo*> */
static GHashTable* hijack_methodinfos_storage = NULL;

/* Track all the method infos that have been created and store them in a 
 * LIFO manner
 */
/* GList<HijackMethodInfo*> */
static GList* method_infos = NULL;

/* Maintain a list of the successive execution of method infos. Every method adds itself
 * there when its execution is started or resumed
 */
/* GList<HijackMethodInfo*> */
static GList* method_execution_flow = NULL;

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
	if (method_execution_flow != NULL) {
		g_list_free (method_execution_flow);
		method_execution_flow = NULL;
	}
}

void
mono_disable_hijack_code ()
{
	hijacking = FALSE;
}

static DotCode*
mono_dot_code_new (void)
{
	DotCode* dotcode = g_new0 (DotCode, 1);
	dotcode->node_letter = 'a';
	dotcode->code = g_string_new (NULL);
	dotcode->yield_ids = g_new0 (GList*, total_num_method);

	return dotcode;
}

static void
mono_dot_code_free (DotCode* dotcode)
{
	int i = 0;

	if (dotcode == NULL)
		return;
	if (dotcode->code)
		g_string_free (dotcode->code, TRUE);

	for (i = 0; i < total_num_method; i++)
		g_list_free (dotcode->yield_ids[i]);

	g_free (dotcode->yield_ids);

	g_free (dotcode);
}

/* From c1num1 to c2num2 */
static void
add_yield_link (DotCode* dcode, gchar c1, gchar c2, guint num1, guint num2)
{
	g_string_append_printf (dcode->code, "%c%u -> %c%u;\n", c1, num1, c2, num2);
}

static void
end_graph (DotCode* dcode)
{
	gchar c = 'a';
	GList* execution_node = method_execution_flow;
	guint id1 = 0, id2 = 0, i = 0;

	g_string_prepend (dcode->code, "}\n");
	for (; c < dcode->node_letter; c += 1) {
		g_string_prepend (dcode->code, "0;\n");
		g_string_prepend_c (dcode->code, c);
	}

	/*	puts ("//BEGIN execution flow");
	while (execution_node != NULL) {
		puts (MINFO(execution_node)->mono_method->name);

		execution_node = g_list_next (execution_node);
	}
	for (i = 0; i < total_num_method; i++)
		printf ("%d ", g_list_length (dcode->yield_ids[i]));
	puts ("");
	puts ("//END");*/

	execution_node = method_execution_flow;
	i = MINFO (execution_node)->current_num_method;
	g_list_pop (dcode->yield_ids[i]);

	while (execution_node != NULL && g_list_next (execution_node) != NULL) {
		i = MINFO(execution_node)->current_num_method;
		//printf ("%d %u\n", i, total_num_method);
		id1 = GPOINTER_TO_INT (dcode->yield_ids [i]->data);
		g_list_pop (dcode->yield_ids [i]);

		i = MINFO(g_list_next (execution_node))->current_num_method;
		id2 = GPOINTER_TO_INT (dcode->yield_ids [i]->data);
		g_list_pop (dcode->yield_ids [i]);

		add_yield_link (dcode,
		                'a' + MINFO(g_list_next (execution_node))->current_num_method,
		                'a' + MINFO(execution_node)->current_num_method,
		                id2, id1);

		execution_node = g_list_delete_link (execution_node, execution_node);
	}

	g_string_prepend (dcode->code, "digraph G {\n{\nrank = same;\n");
	g_string_append (dcode->code, "\n}\n");
}

#define add_item(_dcode, _char, _num, _il, _line) g_string_append_printf (_dcode->code, "%c%u [label = \"IL %#04x\\nline %u\"];\n", _char, _num, _il, _line)

static void
add_link (DotCode* dcode, gchar c, guint num1, guint num2, gboolean directed)
{
	g_string_append_printf (dcode->code, "%c%u -> %c%u [weight = 10.0", c, num1, c, num2);
	if (!directed)
		g_string_append (dcode->code, ", arrowhead = none, color=grey, penwidth=0.8];\n");
	else
		g_string_append (dcode->code, "];\n");
}

#define add_method_name(_dcode, _name, _char, _first) g_string_append_printf (_dcode->code, "\"%s\" [label = \"%s\", labeljust = l, shape = none];\n\"%s\" -> %c0 [style = dotted%s];\n", _name, _name, _name, _char, _first ? "" : ",arrowhead = none")

static void
internal_iterator_method (gpointer data, gpointer user_data)
{
	HijackMethodInfo* method = (HijackMethodInfo*)data;
	//g_return_if_fail (method->current_execution_slot != NULL);
	HijackInterleaving* slot = method->current_execution_slot->data;
	MonoDebugMethodInfo* mdebug = mono_debug_lookup_method (method->mono_method);
	guint offset = 0, accumulator = 0, i = 0;
	MonoDebugSourceLocation *location;
	DotCode* dotcode = user_data;
	
	printf ("Method %s interleaving: ", method->mono_method->name);

	DUMP_INTERLEAVING (slot);
	puts ("");

	if (mdebug == NULL)
		return;

	add_method_name (dotcode, method->mono_method->name, dotcode->node_letter, method->current_num_method == 0);

	puts ("Yielding summary:");
	slot = method->current_execution_slot->data;
	while (slot != NULL && slot->next != NULL) {
		offset = g_array_index (method->il_offsets, guint, (accumulator += slot->initial_count));

		location = mono_debug_symfile_lookup_location (mdebug, offset);
		if (location == NULL)
			continue;

		printf ("%s:%d (IL offset: %#04x, original offset: %u)\n",
		        location->source_file,
		        location->row,
		        location->il_offset,
		        offset);

		slot = slot->next;
	}

	slot = method->current_execution_slot->data;
	accumulator = 0;

	while (slot != NULL) {
		accumulator += slot->initial_count;
		for (i = accumulator - slot->initial_count; i < accumulator; i++) {
			offset = g_array_index (method->il_offsets, guint, i);
			location = mono_debug_symfile_lookup_location (mdebug, offset);
			if (location == NULL) {
				puts ("No debug infos");
				continue;
			}
			add_item(dotcode, dotcode->node_letter, dotcode->node_id++, location->il_offset, location->row);

			if (dotcode->node_id > 1) {
				if (i != accumulator - slot->initial_count) {
					add_link (dotcode, dotcode->node_letter, dotcode->node_id - 2, dotcode->node_id - 1, TRUE);
				} else {
					add_link (dotcode, dotcode->node_letter, dotcode->node_id - 2, dotcode->node_id - 1, FALSE);
					dotcode->yield_ids[method->current_num_method] = g_list_prepend (dotcode->yield_ids[method->current_num_method], GINT_TO_POINTER (dotcode->node_id - 2));
					dotcode->yield_ids[method->current_num_method] = g_list_prepend (dotcode->yield_ids[method->current_num_method], GINT_TO_POINTER (dotcode->node_id - 1));
				}
			}
		}

		if (slot->next == NULL) {
			dotcode->yield_ids[method->current_num_method] = g_list_prepend (dotcode->yield_ids[method->current_num_method], GINT_TO_POINTER (dotcode->node_id - 1));
		}

		slot = slot->next;
	}
	dotcode->yield_ids[method->current_num_method] = g_list_append (dotcode->yield_ids[method->current_num_method], GINT_TO_POINTER (0));

	puts("");

	dotcode->node_letter++;
	dotcode->node_id = 0;
}

static void
mono_hijack_print_current_interleaving (void)
{
	DotCode* dotcode = mono_dot_code_new ();
	FILE* file = fopen ("result.dot", "w");

	method_infos = g_list_reverse (method_infos);
	g_list_foreach (method_infos, internal_iterator_method, dotcode);
	end_graph (dotcode);

	fputs (dotcode->code->str, file);
	fflush (file);
	fclose (file);

	mono_dot_code_free (dotcode);
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

/* Free up memory of a single interleaving chain */
static void
mono_hijack_interleaving_free (HijackInterleaving* interleaving)
{
	if (interleaving == NULL)
		return;

	mono_hijack_interleaving_free (interleaving->next);
	g_free (interleaving);
}

/* Free up memory of a whole set of interleaving chain */
static void
mono_hijack_interleavings_free (GList** array, gint length)
{
	int i = 0;

	for (i = 0; i < length; ++i) {
		GList* iter = array[i];
		for (; iter != NULL; iter = g_list_next (iter))
			mono_hijack_interleaving_free ((HijackInterleaving*)iter->data);
	}
}

/* This method is executed when a branch instruction (BR, BRTRUE, BRFALSE, BEQ, ...) caused by a statement, like `while'
 * or `if', was executed and thus changed the control flow and broke our yielding pattern. This
 * function's goal is thus to stop the execution*, create another set of temporary interleaving to cope with the branch,
 * register them correctly and start the simulation again
 */
static void
mono_hijack_process_branch (HijackBranchInfo* branch)
{
	GList* node = method_infos;

	g_return_if_fail (branch != NULL);
	g_return_if_fail (branch->il_offset_position != -1);

	/* Short-circuit code. There is a case where the branch has no fundamental impact:
	 * when all the methods above the concerned method in the call chain are finished and
	 * there is no method below waiting for launch or finishing. In that case we simply clean
	 * the branch flag, add the required amount of interleaving count so that the method finish
	 * and return directly
	 */
	if (branch->method == MINFO(node)) {
		puts ("Trying short circuit");
		gboolean last = TRUE;
		GList* tnode = node->next;

		while (tnode != NULL) {
			HijackMethodInfo* ti = tnode->data;
			if (ti->current_interleaving != NULL &&
			    ti->current_interleaving->next != NULL) {
				last = FALSE;
				break;
			}
			tnode = tnode->next;
		}

		if (last) {
			branch->method->current_interleaving->no_yielding_count += branch->method->il_offsets->len - branch->il_offset_position;
			branch->method->possible_branching = NULL;
			puts("Short-circuit taken");

			return;
		}
	}


	/* Some test method may not have been launched yet, so move the pointer
	 * to the first run one (every other afterwards should be initialized)
	 */
	while (MINFO(node)->current_execution_slot == NULL)
		node = node->next;

	// Walk through the executing methods infos to dump them
	while (node != NULL) {
		HijackMethodInfo* m = MINFO(node);
		HijackInterleaving* interleaving = NULL;
		HijackInterleaving* last = NULL;
		HijackInterleaving* iter = m->current_execution_slot->data;
		HijackInterleaving* temp = NULL;
		int num_injected = m->number_injected_calls;
		int depth = m->current_yield_count;
		int accumulator = 0;

		printf ("Processing %s\n", m->mono_method->name);
		printf ("Before: ");
		DUMP_INTERLEAVING(iter);
		puts("");

		// Save the interleaving path that led to the jump
		while (iter != m->current_interleaving) {
			temp = g_new0 (HijackInterleaving, 1);
			temp->initial_count = temp->no_yielding_count = iter->initial_count;
			accumulator += temp->initial_count;
			temp->disable_jump_tracking = TRUE;

			if (last != NULL) {
				last->next = temp;
				last = temp;
			} else {
				interleaving = last = temp;
			}

			iter = iter->next;
		}

		// Add the remaining count to reach our jump
		if (iter->initial_count > iter->no_yielding_count)
			accumulator += iter->initial_count - iter->no_yielding_count;

		// Acccumulate the remaining count into the last node for the specialized interleavings creation
		temp = g_new0 (HijackInterleaving, 1);
		last->next = temp;
		temp->disable_jump_tracking = TRUE;

		while (iter != NULL) {
			temp->no_yielding_count = (temp->initial_count += iter->initial_count);
			iter = iter->next;
			if (iter != NULL)
				depth -= 1;
		}

		// Add the equivalent of extra instructions caused by the jump
		if (m == branch->method) {
			temp->no_yielding_count = (temp->initial_count += branch->method->il_offsets->len - branch->il_offset_position);
			m->number_injected_calls = (num_injected += branch->method->il_offsets->len - branch->il_offset_position);
		}

		printf ("After (%d) : ", accumulator);
		DUMP_INTERLEAVING(interleaving);
		puts("");

		{
			HijackSaveSlot* slot = g_new0 (HijackSaveSlot, 1);
			SAVE_CONTEXT (slot->save, m);
			CLEAN_CONTEXT (m);
			slot->execution_slots = m->execution_slots;
			g_list_push (m->saved_slots, slot);
		}

		// Register the new special executions slots (if necessary)
		if (accumulator + 1 != num_injected) {
			int i = 0;
			GList** array = g_malloc0 (sizeof (GList*) * num_injected - 1);
			mono_hijack_generate_interleavings_internal (interleaving, array, accumulator + 2, num_injected, depth);
			m->execution_slots = array;

			puts ("Dumping content:");
			for (i = 0; i < num_injected - 1; i++) {
				GList* foo = array[i];
				while (foo != NULL) {
					HijackInterleaving* slot = foo->data;
					DUMP_INTERLEAVING (slot);
					foo = g_list_next (foo);
					puts ("");
				}
				puts("\n");
			}
		}

		if (m->current_num_method == 0)
			current_method_yield_points = m->current_yield_count++;

		node = node->next;
	}

	branch->method->possible_branching = NULL;

	// Restart scheduler
	mono_runtime_invoke (scheduler_force_restart_method, NULL, NULL, NULL);
}

static void
hijack_func (HijackMethodInfo* m)
{
	HijackInterleaving* slot = m->current_interleaving;
	HijackMethodInfoSave save;

	g_return_if_fail (slot != NULL);

	// If there is a branch that has been registered, launch branching machinery
	if (m->possible_branching != NULL && !slot->disable_jump_tracking) {
		puts ("Successful jump detected in the program flow, launching special code");
		mono_hijack_process_branch (m->possible_branching);
		return;
	}

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
		/* Got waked up, so we add ourselves to method_execution_flow */
		method_execution_flow = g_list_prepend (method_execution_flow, m);
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
		/* Find Scheduler static methods */
		MonoAssemblyName* name = mono_assembly_name_new ("Heisen");
		MonoAssembly* assembly = mono_assembly_loaded (name);
		MonoImage* image = mono_assembly_get_image (assembly);
		MonoMethodDesc* yield_desc = mono_method_desc_new ("Heisen.Scheduler:Yield()", TRUE);
		MonoMethodDesc* stop_desc = mono_method_desc_new ("Heisen.Scheduler:Stop()", TRUE);
		MonoMethodDesc* restart_desc = mono_method_desc_new ("Heisen.Scheduler:ForceRestart()", TRUE);
		scheduler_yield_method = mono_method_desc_search_in_image (yield_desc, image);
		scheduler_stop_method = mono_method_desc_search_in_image (stop_desc, image);
		scheduler_force_restart_method = mono_method_desc_search_in_image (restart_desc, image);
		if (scheduler_yield_method == NULL || scheduler_stop_method == NULL || scheduler_force_restart_method == NULL)
			printf ("Scheduler method initialized correctly? %s %s %s\n",
			        scheduler_yield_method != NULL ? "Yes" : "No",
			        scheduler_stop_method != NULL ? "Yes" : "No",
			        scheduler_force_restart_method != NULL ? "Yes" : "No");

		mono_method_desc_free (yield_desc);
		mono_method_desc_free (stop_desc);
		mono_method_desc_free (restart_desc);
	}

	g_return_if_fail (m != NULL);

	if (m->execution_slots == NULL)
		mono_hijack_generate_interleavings (m);

	if (m->current_num_method == -1)
		m->current_num_method = current_num_method_cache++;

	method_execution_flow = g_list_prepend (method_execution_flow, m);

	puts ("First of first");

	do {
		/* First pass in the hijack_func method, we initialize the GList traversal pointer */
		if (m->current_execution_slot == NULL) {
			puts ("Set up");
			m->current_execution_slot = m->execution_slots[current_method_yield_points];
			m->current_interleaving = m->current_execution_slot->data;
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
							// In case some context is saved, we restore it on all HijackMethodInfo
							if (m->saved_slots != NULL) {
								GList* iter = method_infos;

								for (; iter != NULL; iter = g_list_next (iter)) {
									HijackMethodInfo* minfo = MINFO(iter);
									mono_hijack_interleavings_free (minfo->execution_slots, minfo->number_injected_calls - 1);
									minfo->execution_slots = MSAVESLOT(minfo->saved_slots)->execution_slots;
									RESTORE_CONTEXT(MSAVESLOT(minfo->saved_slots)->save, minfo);
									g_list_pop (minfo->saved_slots);
								}

								current_method_yield_points = m->current_yield_count - 1;

								continue;
							}
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
		}
		break;
	} while (1);

	//printf ("Going with following execution slot for %s(%d)\n", m->mono_method->name, m->current_num_method);
	//internal_iterator_method (NULL, m, NULL);
	/* In all case we set back current_interleaving to a correct value at some start of an execution slot */
	m->current_interleaving = m->current_execution_slot->data;
	/*printf ("Going with following execution slot for %s(%d)\n", m->mono_method->name, m->current_num_method);
	  internal_iterator_method (NULL, m, NULL);*/
	hijack_func (m);
}

void
mono_hijack_init ()
{
	hijack_methodinfos_storage = g_hash_table_new (NULL, NULL);

	register_icall (hijack_func, "hijack_func", "void ptr", TRUE);
	register_icall (hijack_func_first, "hijack_func_first", "void ptr", TRUE);
	//register_icall (hijack_branch_func, "hijack_branch_func", "void ptr", TRUE);
	
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
		methodinfo->mono_method = cfg->method;
		methodinfo->current_num_method = -1;
		methodinfo->il_offsets = g_array_new (FALSE, TRUE, sizeof (guint));
		g_hash_table_insert (hijack_methodinfos_storage, cfg->method, methodinfo);
		method_infos = g_list_prepend (method_infos, methodinfo);

		EMIT_NEW_PCONST (cfg, arg[0], methodinfo);
		mono_emit_jit_icall (cfg, hijack_func_first, arg);
	} else {
		methodinfo->number_injected_calls++;

		EMIT_NEW_PCONST (cfg, arg[0], methodinfo);
		mono_emit_jit_icall (cfg, hijack_func, arg);
	}

	g_array_append_val (methodinfo->il_offsets, cfg->real_offset);
}

/* The way it works: just before a branch instruction is happening, we call mono_emit_hijack_branch_code
 * that will create a branch marker, set it up and register it.
 * If the jump happens this marker won't be cleared so the hijack code can launch the machinery, if the 
 * jump isn't done then the marker is clear by mono_emit_hijack_end_branch_code called just after the
 * jump instruction
 */
void
mono_emit_hijack_branch_code (MonoCompile* cfg, gint target)
{
	HijackBranchInfo* branch = g_new0 (HijackBranchInfo, 1);
	HijackMethodInfo* info = g_hash_table_lookup (hijack_methodinfos_storage, cfg->method);
	gint index = 0;
	MonoInst* arg[1];

	if (info == NULL) {
		g_free (branch);
		return;
	}

	branch->method = info;
	branch->original_target = target;

	while (index < info->il_offsets->len) {
		if (g_array_index (info->il_offsets, guint, index) == target)
			break;
		index++;
	}

	branch->il_offset_position = index >= info->il_offsets->len ? -1 : index;

	EMIT_NEW_PCONST (cfg, arg[0], &info->possible_branching);
	MONO_EMIT_NEW_STORE_MEMBASE_IMM (cfg, OP_STOREP_MEMBASE_IMM, arg[0]->dreg, 0, branch);
	//mono_emit_jit_icall (cfg, hijack_branch_func, arg);
}

void mono_emit_hijack_end_branch_code (MonoCompile* cfg)
{
	HijackMethodInfo* info = g_hash_table_lookup (hijack_methodinfos_storage, cfg->method);
	MonoInst* arg[1];

	if (info == NULL)
		return;

	EMIT_NEW_PCONST (cfg, arg[0], &info->possible_branching);
	MONO_EMIT_NEW_STORE_MEMBASE_IMM (cfg, OP_STOREP_MEMBASE_IMM, arg[0]->dreg, 0, NULL);
}
