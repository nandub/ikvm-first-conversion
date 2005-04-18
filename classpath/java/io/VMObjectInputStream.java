/*
  Copyright (C) 2005 Jeroen Frijters

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
package java.io;

import gnu.classpath.VMStackWalker;
import java.lang.reflect.Constructor;

final class VMObjectInputStream
{
    // TODO move this to ObjectInputStream
    static ClassLoader currentClassLoader(SecurityManager sm)
    {
        Class[] stack = VMStackWalker.getClassContext();
        for (int i = 0; i < stack.length; i++)
        {
            ClassLoader loader = stack[i].getClassLoader();
            if (loader != null)
                return loader;
        }
        return null;
    }

    static native Object allocateObject(Class clazz, Class constr_clazz, Constructor constructor)
        throws InstantiationException;
}
