/*
  Copyright (C) 2006 Jeroen Frijters

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.

  Jeroen Frijters
  jeroen@frijters.net
  
*/

package gnu.java.lang.management;

import cli.System.Diagnostics.Process;
import cli.System.Environment;

final class VMRuntimeMXBeanImpl
{
    static String[] getInputArguments()
    {
        // TODO we should only return the VM args
        return Environment.GetCommandLineArgs();
    }

    static String getName()
    {
        Process p = Process.GetCurrentProcess();
        try
        {
            return p.get_Id() + "@" + Environment.get_MachineName();
        }
        finally
        {
            p.Dispose();
        }
    }

    static long getStartTime()
    {
        final long january_1st_1970 = 62135596800000L;
        Process p = Process.GetCurrentProcess();
        try
        {
            return p.get_StartTime().ToUniversalTime().get_Ticks() / 10000L - january_1st_1970;
        }
        finally
        {
            p.Dispose();
        }
    }
}
