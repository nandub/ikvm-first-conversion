/*
 * Copyright (c) 1999, 2005, Oracle and/or its affiliates. All rights reserved.
 * DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
 *
 * This code is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License version 2 only, as
 * published by the Free Software Foundation.  Oracle designates this
 * particular file as subject to the "Classpath" exception as provided
 * by Oracle in the LICENSE file that accompanied this code.
 *
 * This code is distributed in the hope that it will be useful, but WITHOUT
 * ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
 * FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
 * version 2 for more details (a copy is included in the LICENSE file that
 * accompanied this code).
 *
 * You should have received a copy of the GNU General Public License version
 * 2 along with this work; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin St, Fifth Floor, Boston, MA 02110-1301 USA.
 *
 * Please contact Oracle, 500 Oracle Parkway, Redwood Shores, CA 94065 USA
 * or visit www.oracle.com if you need additional information or have any
 * questions.
 */

package java.lang;

import java.util.ArrayList;
import cli.System.AppDomain;
import cli.System.EventArgs;
import cli.System.EventHandler;

/**
 * Package-private utility class containing data structures and logic
 * governing the virtual-machine shutdown sequence.
 *
 * @author   Mark Reinhold
 * @since    1.3
 */
@ikvm.lang.Internal
public final class Shutdown {

    /* Shutdown state */
    private static final int RUNNING = 0;
    private static final int HOOKS = 1;
    private static final int FINALIZERS = 2;
    private static int state = RUNNING;

    /* Should we run all finalizers upon exit? */
    static volatile boolean runFinalizersOnExit = false;

    /* The set of registered, wrapped hooks, or null if there aren't any */
    private static ArrayList<Runnable> hooks = new ArrayList<Runnable>();

    /* The preceding static fields are protected by this lock */
    private static class Lock { };
    private static Object lock = new Lock();

    /* Lock object for the native halt method */
    private static Object haltLock = new Lock();

    /* Invoked by Runtime.runFinalizersOnExit */
    static void setRunFinalizersOnExit(boolean run) {
        synchronized (lock) {
            runFinalizersOnExit = run;
        }
    }
    
    private static boolean initialized;
    
    public static void init() {
        synchronized (lock) {
            if (initialized || state > RUNNING)
                return;
            initialized = true;
            try
            {
                // MONOBUG Mono doesn't support starting a new thread during ProcessExit
                // (and application shutdown hooks are based on threads)
                // see https://bugzilla.xamarin.com/show_bug.cgi?id=5650
                if (!ikvm.internal.Util.MONO) {
                    // AppDomain.ProcessExit has a LinkDemand, so we have to have a separate method
                    registerShutdownHook();
                    if (false) throw new cli.System.Security.SecurityException();
                }
            }
            catch (cli.System.Security.SecurityException _)
            {
            }
            // The order in with the hooks are added here is important as it
            // determines the order in which they are run. 
            // (1)Console restore hook needs to be called first.
            // (2)Application hooks must be run before calling deleteOnExitHook.
            hooks.add(sun.misc.SharedSecrets.getJavaIOAccess().consoleRestoreHook());
            hooks.add(ApplicationShutdownHooks.hook());
            hooks.add(sun.misc.SharedSecrets.getJavaIODeleteOnExitAccess());
        }
    }

    private static void registerShutdownHook()
    {
        AppDomain.get_CurrentDomain().add_ProcessExit(new EventHandler(new EventHandler.Method() {
            public void Invoke(Object sender, EventArgs e) {
                shutdown();
            }
        }));
    }

    /* Add a new shutdown hook.  Checks the shutdown state and the hook itself,
     * but does not do any security checks.
     */
    static void add(Runnable hook) {
        synchronized (lock) {
            if (state > RUNNING)
                throw new IllegalStateException("Shutdown in progress");

            init();
            hooks.add(hook);
        }
    }


    /* Remove a previously-registered hook.  Like the add method, this method
     * does not do any security checks.
     */
    static boolean remove(Runnable hook) {
        synchronized (lock) {
            if (state > RUNNING)
                throw new IllegalStateException("Shutdown in progress");
            if (hook == null) throw new NullPointerException();
            if (hooks == null) {
                return false;
            } else {
                return hooks.remove(hook);
            }
        }
    }


    /* Run all registered shutdown hooks
     */
    private static void runHooks() {
        /* We needn't bother acquiring the lock just to read the hooks field,
         * since the hooks can't be modified once shutdown is in progress
         */
        for (Runnable hook : hooks) {
            try {
                hook.run();
            } catch(Throwable t) {
                if (t instanceof ThreadDeath) {
                    ThreadDeath td = (ThreadDeath)t;
                    throw td;
                }
            }
        }
    }

    /* The halt method is synchronized on the halt lock
     * to avoid corruption of the delete-on-shutdown file list.
     * It invokes the true native halt method.
     */
    static void halt(int status) {
        synchronized (haltLock) {
            halt0(status);
        }
    }

    static void halt0(int status) {
        cli.System.Environment.Exit(status);
    }

    /* Wormhole for invoking java.lang.ref.Finalizer.runAllFinalizers */
    private static void runAllFinalizers() { /* [IKVM] Don't need to do anything here */ }


    /* The actual shutdown sequence is defined here.
     *
     * If it weren't for runFinalizersOnExit, this would be simple -- we'd just
     * run the hooks and then halt.  Instead we need to keep track of whether
     * we're running hooks or finalizers.  In the latter case a finalizer could
     * invoke exit(1) to cause immediate termination, while in the former case
     * any further invocations of exit(n), for any n, simply stall.  Note that
     * if on-exit finalizers are enabled they're run iff the shutdown is
     * initiated by an exit(0); they're never run on exit(n) for n != 0 or in
     * response to SIGINT, SIGTERM, etc.
     */
    private static void sequence() {
        synchronized (lock) {
            /* Guard against the possibility of a daemon thread invoking exit
             * after DestroyJavaVM initiates the shutdown sequence
             */
            if (state != HOOKS) return;
        }
        runHooks();
        boolean rfoe;
        synchronized (lock) {
            state = FINALIZERS;
            rfoe = runFinalizersOnExit;
        }
        if (rfoe) runAllFinalizers();
    }


    /* Invoked by Runtime.exit, which does all the security checks.
     * Also invoked by handlers for system-provided termination events,
     * which should pass a nonzero status code.
     */
    static void exit(int status) {
        boolean runMoreFinalizers = false;
        synchronized (lock) {
            if (status != 0) runFinalizersOnExit = false;
            switch (state) {
            case RUNNING:       /* Initiate shutdown */
                state = HOOKS;
                break;
            case HOOKS:         /* Stall and halt */
                break;
            case FINALIZERS:
                if (status != 0) {
                    /* Halt immediately on nonzero status */
                    halt(status);
                } else {
                    /* Compatibility with old behavior:
                     * Run more finalizers and then halt
                     */
                    runMoreFinalizers = runFinalizersOnExit;
                }
                break;
            }
        }
        if (runMoreFinalizers) {
            runAllFinalizers();
            halt(status);
        }
        synchronized (Shutdown.class) {
            /* Synchronize on the class object, causing any other thread
             * that attempts to initiate shutdown to stall indefinitely
             */
            sequence();
            halt(status);
        }
    }


    /* Invoked by the JNI DestroyJavaVM procedure when the last non-daemon
     * thread has finished.  Unlike the exit method, this method does not
     * actually halt the VM.
     */
    static void shutdown() {
        synchronized (lock) {
            switch (state) {
            case RUNNING:       /* Initiate shutdown */
                state = HOOKS;
                break;
            case HOOKS:         /* Stall and then return */
            case FINALIZERS:
                break;
            }
        }
        synchronized (Shutdown.class) {
            sequence();
        }
    }

}
