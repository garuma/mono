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

extern void register_icall (gpointer func, const char *name, const char *sigstr, gboolean save);

static int hijacking = FALSE;

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

void
mono_disable_hijack_code ()
{
	hijacking = FALSE;
}

/* Method pointer is used as a key, since it *shouldn't* change in our workflow
 * it safe to use it like this. Values is the corresponding MonoContinuation struct
 */
/*static GHashTable* hijack_continuation_storage = NULL;*/

void
mono_hijack_init ()
{
	/*hijack_continuation_storage = g_hash_table_new (NULL, NULL);*/

	register_icall (hijack_func, "hijack_func", "void", TRUE);
	
	mono_add_internal_call ("Heisen.RuntimeManager::mono_enable_hijack_code",
	                        mono_enable_hijack_code);
	mono_add_internal_call ("Heisen.RuntimeManager::mono_disable_hijack_code",
	                        mono_disable_hijack_code);
}

void
mono_emit_hijack_code (MonoCompile *cfg)
{
	/*MonoInst* arg[1];*/
	char* full_name = NULL;

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
			
	/*EMIT_NEW_PCONST (cfg, arg[0], cfg->method);*/
	mono_emit_jit_icall (cfg, hijack_func, NULL);
}

/* TODO: for the moment I'm using the MonoMethod passed in instance as a way to find back my
 * Scheduler method, but later on I will go back to inserting my method in crude way without
 * arg to generate as less noise as possible, watch out in that case
 */
void
hijack_func ()
{
	static MonoMethod* scheduler_method = NULL;

	if (scheduler_method == NULL) {
		/* Find Scheduler.Yield static method */
		MonoAssemblyName* name = mono_assembly_name_new ("HeisenLib");
		MonoAssembly* assembly = mono_assembly_loaded (name);
		MonoImage* image = mono_assembly_get_image (assembly);
		MonoMethodDesc* desc = mono_method_desc_new ("Heisen.Scheduler:Yield()", TRUE);
		scheduler_method = mono_method_desc_search_in_image (desc, image);
		printf ("Scheduler method initialized correctly? %s\n", scheduler_method != NULL ? "Yes" : "No");
	}

	/*printf ("I'm in ur runtime, hijacking your JIT from %s:%s\n", (method == NULL) ? "(null)" : mono_type_get_full_name (method->klass), (method == NULL) ? "(null)" : method->name);*/
	mono_runtime_invoke (scheduler_method, NULL, NULL, NULL);
}
