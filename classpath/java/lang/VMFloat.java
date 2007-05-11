/*
  Copyright (C) 2003, 2007 Jeroen Frijters

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

package java.lang;

import sun.misc.FloatingDecimal;

final class VMFloat
{
    static float intBitsToFloat(int v)
    {
	return cli.System.BitConverter.ToSingle(cli.System.BitConverter.GetBytes(v), 0);
    }

    static int floatToIntBits(float v)
    {
	if(Float.isNaN(v))
	{
	    return 0x7fc00000;
	}
	return cli.System.BitConverter.ToInt32(cli.System.BitConverter.GetBytes(v), 0);
    }

    static int floatToRawIntBits(float v)
    {
	return cli.System.BitConverter.ToInt32(cli.System.BitConverter.GetBytes(v), 0);
    }

    static String toString(float f)
    {
	return new FloatingDecimal(f).toJavaFormatString();
    }

    static float parseFloat(String str)
    {
	return FloatingDecimal.readJavaFormatString(str).floatValue();
    }
}
