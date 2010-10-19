#ifndef __HIJACKING_H__
#define __HIJACKING_H__

/* This struct forms a linked list of instruction on when to yield.
 * Each node contains a number that is decremented at each instruction,
 * when it goes to 0, the node is discarded and the hijack method calls
 * Yield, the process repeat until there is no more node which should
 * correspond to the moment when the method body is finished
 */
typedef struct _HijackInterleaving {
	/* initial_count and no_yielding_count carry the same value except no_yielding_count is decremented by code */
	int initial_count;
	int no_yielding_count;
	/* This is put to false by default and only used in the special jump machinery */
	gboolean disable_jump_tracking;
	struct _HijackInterleaving* next;
} HijackInterleaving;

typedef struct HijackBranchInfo HijackBranchInfo;

typedef struct {
	MonoMethod* mono_method;
	int number_injected_calls;
	int number_interleaving;
	GArray* il_offsets;
	HijackBranchInfo* possible_branching;

	/* GList<HijackInterleaving*>[] - array keyed by number of yield point to a list of interleavings */
	GList** execution_slots;

	/* Here is stored a specially created set of HijackInterleavings when it occurs that during a previously executing
	 * interleaving coming from execution_slots a branch instruction was encountered (either from a if/else structure,
	 * a while or a for). These interleavings all have a common beginning which is copied from the special initial interleaving
	 * so that we always goes back to the situation where the branch had occured. After that, the interleaving differs normally
	 * as they have been generated traditionnaly by mono_hijack_generate_interleavings_internal. When these special interleavings
	 * have been generated there, they are assigned in current_execution slot in their corresponding method infos (every one
	 * of them need to be updated) and the old value of current_execution slot is saved. Finally we call Scheduler force restart
	 * method and after the "sub"-interleavings have been correctly ran (if they correctly ran) we restore the earlier state.
	 * 	GList* special_slot;
	 */
	/* Used in case we recursively need to launch "sub"-interleavings. The end of the list always contains the initial execution
	 * slot that started it all
	 */
	/* GList<HijackSaveSlot*> */
	GList* saved_slots;

	/* These fields are changed during the execution of the method */
	int current_yield_count; /* used as execution_slots index for enumeration */
	GList* current_execution_slot; /* iterating through the list of HijackInterleaving */
	HijackInterleaving* current_interleaving; /* Iteration ptr to the current block of the current interleaving */
	int current_num_method; /* Used as an index to tell where the method is in the interleaving call chain */
	int current_call_number; /* Where this methodinfo is in the test call chain, determined by scheduler execution order */
	int current_neighbours_interleaving_count; /* How much execution we have for a single interleaving */
} HijackMethodInfo;

struct HijackBranchInfo {
	HijackMethodInfo* method;
	gint original_target;
	gint il_offset_position;
};

typedef struct {
	int n_injected_calls;
	int c_yield_count;
	GList* c_execution_slot;
	HijackInterleaving* c_slot;
	int c_num_method;
	int c_call_number;
	int c_neighbours_interleaving_count;
} HijackMethodInfoSave;

typedef struct {
	GList** execution_slots;
	HijackMethodInfoSave save;
} HijackSaveSlot;

#define SAVE_CONTEXT(save, method)	  \
	save.n_injected_calls = method->number_injected_calls; \
	save.c_yield_count = method->current_yield_count; \
	save.c_slot = method->current_interleaving; \
	save.c_execution_slot = method->current_execution_slot; \
	save.c_num_method = method->current_num_method; \
	save.c_call_number = method->current_call_number; \
	save.c_neighbours_interleaving_count = method->current_neighbours_interleaving_count; \
	method->current_execution_slot = NULL; \
	method->current_interleaving = NULL;

#define RESTORE_CONTEXT(save, method)	  \
	method->number_injected_calls = save.n_injected_calls; \
	method->current_yield_count = save.c_yield_count; \
	method->current_interleaving = save.c_slot; \
	method->current_execution_slot = save.c_execution_slot; \
	method->current_num_method = save.c_num_method; \
	method->current_call_number = save.c_call_number; \
	method->current_neighbours_interleaving_count = save.c_neighbours_interleaving_count;

#define CLEAN_CONTEXT(method)	  \
	method->current_interleaving = NULL; \
	method->current_execution_slot = NULL; \

#define MINFO(n) ((HijackMethodInfo*)(n->data))
#define MSAVESLOT(n) ((HijackSaveSlot*)(n->data))

#define g_list_pop(_list) _list = g_list_delete_link (_list, _list)
#define g_list_push(_list, _elem) _list = g_list_prepend (_list, _elem)

typedef struct {
	GString* code;
	gchar node_letter;
	guint node_id;
	GList** yield_ids;
} DotCode;

#define DUMP_INTERLEAVING(_i) do { HijackInterleaving* _s = _i; while (_s != NULL) { printf ("%d", _s->initial_count); if (_s->next != NULL) printf ("-"); _s = _s->next; } } while (0)

#endif
