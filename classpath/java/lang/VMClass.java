/* VMClass.java -- VM Specific Class methods
   Copyright (C) 2003 Free Software Foundation

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

import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import cli.System.*;
import cli.System.Reflection.*;

/*
 * This class is a reference version, mainly for compiling a class library
 * jar.  It is likely that VM implementers replace this with their own
 * version that can communicate effectively with the VM.
 */

/**
 *
 * @author Etienne Gagnon <etienne.gagnon@uqam.ca>
 * @author Archie Cobbs <archie@dellroad.org>
 * @author C. Brian Jones <cbj@gnu.org>
 */
final class VMClass 
{
    private Object wrapper; // the corresponding TypeWrapper
    private Class clazz;

    // NOTE this is used in classpath.cs to go from a Class object to a TypeWrapper
    // HACK public because we want to create a delegate to call it
    public static Object getWrapperFromClass(Object c)
    {
	return ((Class)c).vmClass.wrapper;
    }

    private VMClass(Object wrapper)
    {
	this.wrapper = wrapper;
    }

    // HACK public because we want to create a delegate to call it
    public static Object createClass(Object wrapper)
    {
	VMClass vmClass = new VMClass(wrapper);
	Class c = new Class(vmClass);
	vmClass.clazz = c;
	return c;
    }

    // HACK we need a way to call ClassLoader.loadClass() from C#, so we need this helper method
    // and its public because we want to create a delegate to call it
    public static Object loadClassHelper(Object loader, String name) throws java.lang.ClassNotFoundException
    {
	return ((ClassLoader)loader).loadClass(name).vmClass.wrapper;
    }
    
    /**
     * Discover whether an Object is an instance of this Class.  Think of it
     * as almost like <code>o instanceof (this class)</code>.
     *
     * @param o the Object to check
     * @return whether o is an instance of this class
     * @since 1.1
     */
    boolean isInstance(Object o)
    {
	return o != null && clazz.isAssignableFrom(o.getClass());
    }

    /**
     * Discover whether an instance of the Class parameter would be an
     * instance of this Class as well.  Think of doing
     * <code>isInstance(c.newInstance())</code> or even
     * <code>c.newInstance() instanceof (this class)</code>. While this
     * checks widening conversions for objects, it must be exact for primitive
     * types.
     *
     * @param c the class to check
     * @return whether an instance of c would be an instance of this class
     *         as well
     * @throws NullPointerException if c is null
     * @since 1.1
     */
    boolean isAssignableFrom(Class c)
    {
	// this is implemented by the "native" code, because
	// remapped types can appear to implement interfaces that they don't
	// actually implement
	return IsAssignableFrom(wrapper, c.vmClass.wrapper);
    }
    private static native boolean IsAssignableFrom(Object w1, Object w2);

    /**
     * Check whether this class is an interface or not.  Array types are not
     * interfaces.
     *
     * @return whether this class is an interface or not
     */
    boolean isInterface()
    {
	return IsInterface(wrapper);
    }
    private static native boolean IsInterface(Object wrapper);

    /**
     * Return whether this class is a primitive type.  A primitive type class
     * is a class representing a kind of "placeholder" for the various
     * primitive types, or void.  You can access the various primitive type
     * classes through java.lang.Boolean.TYPE, java.lang.Integer.TYPE, etc.,
     * or through boolean.class, int.class, etc.
     *
     * @return whether this class is a primitive type
     * @see Boolean#TYPE
     * @see Byte#TYPE
     * @see Character#TYPE
     * @see Short#TYPE
     * @see Integer#TYPE
     * @see Long#TYPE
     * @see Float#TYPE
     * @see Double#TYPE
     * @see Void#TYPE
     * @since 1.1
     */
    boolean isPrimitive()
    {
	return clazz == boolean.class ||
	    clazz == byte.class ||
	    clazz == char.class ||
	    clazz == short.class ||
	    clazz == int.class ||
	    clazz == long.class ||
	    clazz == float.class ||
	    clazz == double.class ||
	    clazz == void.class;
    }

    /**
     * Get the name of this class, separated by dots for package separators.
     * Primitive types and arrays are encoded as:
     * <pre>
     * boolean             Z
     * byte                B
     * char                C
     * short               S
     * int                 I
     * long                J
     * float               F
     * double              D
     * void                V
     * array type          [<em>element type</em>
     * class or interface, alone: &lt;dotted name&gt;
     * class or interface, as element type: L&lt;dotted name&gt;;
     *
     * @return the name of this class
     */
    String getName()
    {
	// getName() is used by the classloader, so it shouldn't trigger a resolve of the class
	return GetName(wrapper);
    }
    private static native String GetName(Object wrapper);

    /**
     * Get the direct superclass of this class.  If this is an interface,
     * Object, a primitive type, or void, it will return null. If this is an
     * array type, it will return Object.
     *
     * @return the direct superclass of this class
     */
    Class getSuperclass()
    {
	return (Class)GetSuperClassFromWrapper(wrapper);
    }
    private native static Object GetSuperClassFromWrapper(Object wrapper);

    /**
     * Get the interfaces this class <EM>directly</EM> implements, in the
     * order that they were declared. This returns an empty array, not null,
     * for Object, primitives, void, and classes or interfaces with no direct
     * superinterface. Array types return Cloneable and Serializable.
     *
     * @return the interfaces this class directly implements
     */
    Class[] getInterfaces()
    {
	Object[] interfaces = GetInterfaces(wrapper);
	Class[] interfacesClass = new Class[interfaces.length];
	System.arraycopy(interfaces, 0, interfacesClass, 0, interfaces.length);
	return interfacesClass;
    }
    private static native Object[] GetInterfaces(Object wrapper);

    /**
     * If this is an array, get the Class representing the type of array.
     * Examples: "[[Ljava.lang.String;" would return "[Ljava.lang.String;", and
     * calling getComponentType on that would give "java.lang.String".  If
     * this is not an array, returns null.
     *
     * @return the array type of this class, or null
     * @see Array
     * @since 1.1
     */
    Class getComponentType()
    {
	// .NET array types can have unfinished element types, but we don't
	// want to expose those, so we may need to finish the type
	return (Class)getComponentClassFromWrapper(wrapper);
    }
    private static native Object getComponentClassFromWrapper(Object wrapper);

    /**
     * Get the modifiers of this class.  These can be decoded using Modifier,
     * and is limited to one of public, protected, or private, and any of
     * final, static, abstract, or interface. An array class has the same
     * public, protected, or private modifier as its component type, and is
     * marked final but not an interface. Primitive types and void are marked
     * public and final, but not an interface.
     *
     * @return the modifiers of this class
     * @see Modifer
     * @since 1.1
     */
    int getModifiers()
    {
	return GetModifiers(wrapper);
    }
    private static native int GetModifiers(Object wrapper);

    /**
     * If this is a nested or inner class, return the class that declared it.
     * If not, return null.
     *
     * @return the declaring class of this class
     * @since 1.1
     */
    Class getDeclaringClass()
    {
	return (Class)GetDeclaringClass(wrapper);
    }
    private native static Object GetDeclaringClass(Object wrapper);

    /**
     * Like <code>getDeclaredClasses()</code> but without the security checks.
     *
     * @param pulicOnly Only public classes should be returned
     */
    Class[] getDeclaredClasses(boolean publicOnly)
    {
	Object[] classes = GetDeclaredClasses(wrapper, publicOnly);
	Class[] classesClass = new Class[classes.length];
	System.arraycopy(classes, 0, classesClass, 0, classes.length);
	return classesClass;
    }
    private static native Object[] GetDeclaredClasses(Object wrapper, boolean publicOnly);

    /**
     * Like <code>getDeclaredFields()</code> but without the security checks.
     *
     * @param pulicOnly Only public fields should be returned
     */
    Field[] getDeclaredFields(boolean publicOnly)
    {
	Object[] fieldCookies = GetDeclaredFields(wrapper, publicOnly);
	Field[] fields = new Field[fieldCookies.length];
	for(int i = 0; i < fields.length; i++)
	{
	    fields[i] = new Field(clazz, fieldCookies[i]);
	}
	return fields;
    }
    private static native Object[] GetDeclaredFields(Object wrapper, boolean publicOnly);

    /**
     * Like <code>getDeclaredMethods()</code> but without the security checks.
     *
     * @param pulicOnly Only public methods should be returned
     */
    Method[] getDeclaredMethods(boolean publicOnly)
    {
	Object[] methodCookies = GetDeclaredMethods(wrapper, true, publicOnly);
	Method[] methods = new Method[methodCookies.length];
	for(int i = 0; i < methodCookies.length; i++)
	{
	    methods[i] = new Method(clazz, methodCookies[i]);
	}
	return methods;
    }
    private static native Object[] GetDeclaredMethods(Object wrapper, boolean methods, boolean publicOnly);

    /**
     * Like <code>getDeclaredConstructors()</code> but without
     * the security checks.
     *
     * @param pulicOnly Only public constructors should be returned
     */
    Constructor[] getDeclaredConstructors(boolean publicOnly)
    {
	Object[] methodCookies = GetDeclaredMethods(wrapper, false, publicOnly);
	Constructor[] constructors = new Constructor[methodCookies.length];
	for(int i = 0; i < methodCookies.length; i++)
	{
	    constructors[i] = new Constructor(clazz, methodCookies[i]);
	}
	return constructors;
    }

    /**
     * Return the class loader of this class.
     *
     * @return the class loader
     */
    ClassLoader getClassLoader()
    {
	// getClassLoader() can be used by the classloader, so it shouldn't trigger a resolve of the class
	return getClassLoader0(wrapper);
    }
    private static native ClassLoader getClassLoader0(Object wrapper);

    /**
     * VM implementors are free to make this method a noop if 
     * the default implementation is acceptable.
     *
     * @param name the name of the class to find
     * @return the Class object representing the class or null for noop
     * @throws ClassNotFoundException if the class was not found by the
     *         classloader
     * @throws LinkageError if linking the class fails
     * @throws ExceptionInInitializerError if the class loads, but an exception
     *         occurs during initialization
     */
    static Class forName(String name) throws ClassNotFoundException
    {
	// if we ever get back to using a separate assembly for each class loader, it
	// might be faster to use Assembly.GetCallingAssembly here...
	cli.System.Diagnostics.StackFrame frame = new cli.System.Diagnostics.StackFrame(1);
	// HACK a lame way to deal with potential inlining of this method (or Class.forName)
	if(frame.GetMethod().get_Name().equals("forName"))
	{
	    frame = new cli.System.Diagnostics.StackFrame(2);
	}
	ClassLoader cl = getClassLoaderFromType(frame.GetMethod().get_DeclaringType());
	return Class.forName(name, true, cl);
    }
    private static native ClassLoader getClassLoaderFromType(Type type);

    void initialize()
    {
	initialize(wrapper);
    }
    private static native void initialize(Object wrapper);

    static native Class loadArrayClass(String name, Object classLoader);
    static native Class loadBootstrapClass(String name, boolean initialize);

    static native void throwException(Throwable t);

    /**
     * Return whether this class is an array type.
     *
     * @return 1 if this class is an array type, 0 otherwise, -1 if unsupported
     * operation
     */
    int isArray()
    {
	return IsArray(wrapper) ? 1 : 0;
    }
    private static native boolean IsArray(Object wrapper);
}
