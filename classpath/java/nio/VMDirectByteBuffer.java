/*
  Copyright (C) 2004, 2005, 2006 Jeroen Frijters

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
package java.nio;

import gnu.classpath.Pointer;
import gnu.classpath.PointerUtil;
import cli.System.IntPtr;
import cli.System.Runtime.InteropServices.Marshal;

@ikvm.lang.Internal
public class VMDirectByteBuffer
{
    // this method is used by JNI.NewDirectByteBuffer
    public static ByteBuffer NewDirectByteBuffer(IntPtr p, int capacity)
    {
        return new DirectByteBufferImpl(null, p, capacity, capacity, 0);
    }

    public static IntPtr GetDirectBufferAddress(Buffer buf)
    {
        return buf.address != null ? PointerUtil.toIntPtr(buf.address) : IntPtr.Zero;
    }

    static Pointer adjustAddress(Pointer r, int pos)
    {
        return PointerUtil.add(r, pos);
    }
}
