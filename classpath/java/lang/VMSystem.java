/* VMSystem.java -- helper for java.lang.system
   Copyright (C) 1998, 2002, 2004 Free Software Foundation

This file is part of GNU Classpath.

GNU Classpath is free software; you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation; either version 2, or (at your option)
any later version.

GNU Classpath is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
General Public License for more details.

You should have received a copy of the GNU General Public License
along with GNU Classpath; see the file COPYING.  If not, write to the
Free Software Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA
02111-1307 USA.

Linking this library statically or dynamically with other modules is
making a combined work based on this library.  Thus, the terms and
conditions of the GNU General Public License cover the whole
combination.

As a special exception, the copyright holders of this library give you
permission to link this library with independent modules to produce an
executable, regardless of the license terms of these independent
modules, and to copy and distribute the resulting executable under
terms of your choice, provided that you also meet, for each linked
independent module, the terms and conditions of the license of that
module.  An independent module is a module which is not derived from
or based on this library.  If you modify this library, you may extend
this exception to your version of the library, but you are not
obligated to do so.  If you do not wish to do so, delete this
exception statement from your version. */

package java.lang;

import java.util.Properties;

import java.io.*;
import cli.System.Collections.IDictionary;
import cli.System.Collections.IDictionaryEnumerator;
import cli.System.Reflection.BindingFlags;

/**
 * VMSystem is a package-private helper class for System that the
 * VM must implement.
 *
 * @author John Keiser
 */
final class VMSystem
{
    /**
     * Copy one array onto another from <code>src[srcStart]</code> ...
     * <code>src[srcStart+len-1]</code> to <code>dest[destStart]</code> ...
     * <code>dest[destStart+len-1]</code>. First, the arguments are validated:
     * neither array may be null, they must be of compatible types, and the
     * start and length must fit within both arrays. Then the copying starts,
     * and proceeds through increasing slots.  If src and dest are the same
     * array, this will appear to copy the data to a temporary location first.
     * An ArrayStoreException in the middle of copying will leave earlier
     * elements copied, but later elements unchanged.
     *
     * @param src the array to copy elements from
     * @param srcStart the starting position in src
     * @param dest the array to copy elements to
     * @param destStart the starting position in dest
     * @param len the number of elements to copy
     * @throws NullPointerException if src or dest is null
     * @throws ArrayStoreException if src or dest is not an array, if they are
     *         not compatible array types, or if an incompatible runtime type
     *         is stored in dest
     * @throws IndexOutOfBoundsException if len is negative, or if the start or
     *         end copy position in either array is out of bounds
     */
    static native void arraycopy(Object src, int srcStart, Object dest, int destStart, int len);

    /**
     * Get a hash code computed by the VM for the Object. This hash code will
     * be the same as Object's hashCode() method.  It is usually some
     * convolution of the pointer to the Object internal to the VM.  It
     * follows standard hash code rules, in that it will remain the same for a
     * given Object for the lifetime of that Object.
     *
     * @param o the Object to get the hash code for
     * @return the VM-dependent hash code for this Object
     */
    // FXBUG this is implemented by a non-virtual call to System.Object.GetHashCode (in map.xml),
    // because RuntimeHelpers.GetHashCode is broken (in v1.x) when called in a secondary AppDomain.
    // See http://weblog.ikvm.net/PermaLink.aspx?guid=c2442bc8-7b48-4570-b082-82649cc347dc
    static native int identityHashCode(Object o);

    /**
     * Convert a library name to its platform-specific variant.
     *
     * @param libname the library name, as used in <code>loadLibrary</code>
     * @return the platform-specific mangling of the name
     * @XXX Add this method
     static native String mapLibraryName(String libname);
     */

    /**
     * Set {@link #in} to a new InputStream.
     *
     * @param in the new InputStream
     * @see #setIn(InputStream)
     */
    static void setIn(InputStream in)
    {
        setSystemField("in", in);
    }

    /**
     * Set {@link #out} to a new PrintStream.
     *
     * @param out the new PrintStream
     * @see #setOut(PrintStream)
     */
    static void setOut(PrintStream out)
    {
        setSystemField("out", out);
    }

    /**
     * Set {@link #err} to a new PrintStream.
     *
     * @param err the new PrintStream
     * @see #setErr(PrintStream)
     */
    static void setErr(PrintStream err)
    {
        setSystemField("err", err);
    }

    private static void setSystemField(String field, Object value)
    {
        cli.System.Type.GetType("java.lang.System")
            .GetField(field, BindingFlags.wrap(BindingFlags.NonPublic | BindingFlags.Static))
            .SetValue(null, value);
    }

    /**
     * Get the current time, measured in the number of milliseconds from the
     * beginning of Jan. 1, 1970. This is gathered from the system clock, with
     * any attendant incorrectness (it may be timezone dependent).
     *
     * @return the current time
     * @see java.util.Date
     */
    static long currentTimeMillis()
    {
	final long january_1st_1970 = 62135596800000L;
	return cli.System.DateTime.get_UtcNow().get_Ticks() / 10000L - january_1st_1970;
    }

    static long nanoTime()
    {
        // Note that the epoch is undefined, but we use something similar to the JDK
        final long epoch = 632785401332600000L;
        return (cli.System.DateTime.get_UtcNow().get_Ticks() - epoch) * 100L;
    }

    /**
     * Helper method which creates the standard input stream.
     * VM implementors may choose to construct these streams differently.
     * This method can also return null if the stream is created somewhere 
     * else in the VM startup sequence.
     */

    static InputStream makeStandardInputStream()
    {
	return new BufferedInputStream(new FileInputStream(FileDescriptor.in));
    }

    /**
     * Helper method which creates the standard output stream.
     * VM implementors may choose to construct these streams differently.
     * This method can also return null if the stream is created somewhere 
     * else in the VM startup sequence.
     */

    static PrintStream makeStandardOutputStream()
    {
	return new PrintStream(new BufferedOutputStream(new FileOutputStream(FileDescriptor.out)), true);
    }
    /**
     * Helper method which creates the standard error stream.
     * VM implementors may choose to construct these streams differently.
     * This method can also return null if the stream is created somewhere 
     * else in the VM startup sequence.
     */

    static PrintStream makeStandardErrorStream()
    {
	return new PrintStream(new BufferedOutputStream(new FileOutputStream(FileDescriptor.err)), true);
    }

    /**
     * Gets the value of an environment variable.
     * Always returning null is a valid (but not very useful) implementation.
     *
     * @param name The name of the environment variable (will not be null).
     * @return The string value of the variable or null when the
     *         environment variable is not defined.
     */
    static String getenv(String name)
    {
	return cli.System.Environment.GetEnvironmentVariable(name);
    }

    static java.util.List environ()
    {
	java.util.ArrayList list = new java.util.ArrayList();
        IDictionary env = cli.System.Environment.GetEnvironmentVariables();
        IDictionaryEnumerator e = env.GetEnumerator();
        for(int i = 0; e.MoveNext(); i++)
        {
            list.add(e.get_Key() + "=" + e.get_Value());
        }
        return list;
    }
}
