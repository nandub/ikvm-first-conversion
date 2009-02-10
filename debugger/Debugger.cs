/*
  Copyright (C) 2009 Volker Berlin (vberlin@inetsoftware.de)

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
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ikvm.debugger
{
    /// <summary>
    /// This is the start class of the debugger.
    /// </summary>
    class Debugger
    {
        static void Main(string[] args)
        {
            JdwpParameters parameters = null;
            int pid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                String str = args[i];
                Console.Out.WriteLine(str);
                Console.Out.Flush();
                if (str.StartsWith("-Xrunjdwp") || str.StartsWith("-agentlib:jdwp"))
                {
                    parameters = new JdwpParameters();
                    parameters.Parse(str);

                }
                if (str.StartsWith("-pid:"))
                {
                    pid = Int32.Parse(str.Substring(5, str.Length - 5));
                }
            }
            if (parameters != null && pid != 0)
            {
                JdwpConnection conn = new JdwpConnection(parameters);
                conn.Connect();
                Console.WriteLine("Started");
                TargetVM target = new TargetVM(pid);
                JdwpHandler handler = new JdwpHandler(conn, target);
                handler.Run();
                //System.Threading.Thread.Sleep(5000);
            }
            else
            {
                Environment.Exit(3);
            }
        }
    }
}
