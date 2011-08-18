/*
 * Copyright (c) 2000, 2010, Oracle and/or its affiliates. All rights reserved.
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

package sun.nio.ch;

import java.io.*;
import cli.Microsoft.Win32.SafeHandles.SafeFileHandle;
import cli.System.IntPtr;
import cli.System.IO.FileStream;
import cli.System.Runtime.InteropServices.DllImportAttribute;
import cli.System.Runtime.InteropServices.StructLayoutAttribute;
import cli.System.Runtime.InteropServices.LayoutKind;
import static ikvm.internal.Util.WINDOWS;
import ikvm.internal.NotYetImplementedError;

class FileDispatcherImpl extends FileDispatcher
{
    /**
     * Indicates if the dispatcher should first advance the file position
     * to the end of file when writing.
     */
    private final boolean append;

    FileDispatcherImpl(boolean append) {
        this.append = append;
    }

    FileDispatcherImpl() {
        this(false);
    }

    int force(FileDescriptor fd, boolean metaData) throws IOException {
        fd.sync();
        return 0;
    }

    int truncate(FileDescriptor fd, long size) throws IOException {
        fd.setLength(size);
        return 0;
    }

    long size(FileDescriptor fd) throws IOException {
        return fd.length();
    }

    @StructLayoutAttribute.Annotation(LayoutKind.__Enum.Sequential)
    private static final class OVERLAPPED extends cli.System.Object
    {
        IntPtr Internal;
        IntPtr InternalHigh;
        int OffsetLow;
        int OffsetHigh;
        IntPtr hEvent;
    }

    @cli.System.Security.SecuritySafeCriticalAttribute.Annotation
    int lock(FileDescriptor fd, boolean blocking, long pos, long size,
             boolean shared) throws IOException
    {
        FileStream fs = (FileStream)fd.getStream();
        if (WINDOWS)
        {
            int LOCKFILE_FAIL_IMMEDIATELY = 1;
            int LOCKFILE_EXCLUSIVE_LOCK = 2;
            int ERROR_LOCK_VIOLATION = 33;
            int flags = 0;
            OVERLAPPED o = new OVERLAPPED();
            o.OffsetLow = (int)pos;
            o.OffsetHigh = (int)(pos >> 32);
            if (!blocking)
            {
                flags |= LOCKFILE_FAIL_IMMEDIATELY;
            }
            if (!shared)
            {
                flags |= LOCKFILE_EXCLUSIVE_LOCK;
            }
            int result = LockFileEx(fs.get_SafeFileHandle(), flags, 0, (int)size, (int)(size >> 32), o);
            if (result == 0)
            {
                int error = cli.System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (!blocking && error == ERROR_LOCK_VIOLATION)
                {
                    return NO_LOCK;
                }
                throw new IOException("Lock failed");
            }
            return LOCKED;
        }
        else
        {
            try
            {
                if (false) throw new cli.System.ArgumentOutOfRangeException();
                for (;;)
                {
                    try
                    {
                        if (false) throw new cli.System.IO.IOException();
                        if (false) throw new cli.System.ObjectDisposedException("");
                        fs.Lock(pos, size);
                        return shared ? RET_EX_LOCK : LOCKED;
                    }
                    catch (cli.System.IO.IOException x)
                    {
                        if (!blocking)
                        {
                            return NO_LOCK;
                        }
                        cli.System.Threading.Thread.Sleep(100);
                    }
                    catch (cli.System.ObjectDisposedException x)
                    {
                        throw new IOException(x.getMessage());
                    }
                }
            }
            catch (cli.System.ArgumentOutOfRangeException x)
            {
                throw new IOException(x.getMessage());
            }
        }
    }

    @cli.System.Security.SecuritySafeCriticalAttribute.Annotation
    void release(FileDescriptor fd, long pos, long size) throws IOException {
        FileStream fs = (FileStream)fd.getStream();
        if (WINDOWS)
        {
            OVERLAPPED o = new OVERLAPPED();
            o.OffsetLow = (int)pos;
            o.OffsetHigh = (int)(pos >> 32);
            int result = UnlockFileEx(fs.get_SafeFileHandle(), 0, (int)size, (int)(size >> 32), o);
            if (result == 0)
            {
                throw new IOException("Release failed");
            }
        }
        else
        {
            try
            {
                if (false) throw new cli.System.ArgumentOutOfRangeException();
                if (false) throw new cli.System.IO.IOException();
                if (false) throw new cli.System.ObjectDisposedException("");
                fs.Unlock(pos, size);
            }
            catch (cli.System.ArgumentOutOfRangeException
                | cli.System.IO.IOException
                | cli.System.ObjectDisposedException x)
            {
                throw new IOException(x.getMessage());
            }
        }
    }

    void close(FileDescriptor fd) throws IOException {
        fd.close();
    }

    FileDescriptor duplicateForMapping(FileDescriptor fd) throws IOException {
        return fd;
    }

    @DllImportAttribute.Annotation(value="kernel32", SetLastError=true)
    private static native int LockFileEx(SafeFileHandle hFile, int dwFlags, int dwReserved, int nNumberOfBytesToLockLow, int nNumberOfBytesToLockHigh, OVERLAPPED lpOverlapped);

    @DllImportAttribute.Annotation("kernel32")
    private static native int UnlockFileEx(SafeFileHandle hFile, int dwReserved, int nNumberOfBytesToUnlockLow, int nNumberOfBytesToUnlockHigh, OVERLAPPED lpOverlapped);
}
