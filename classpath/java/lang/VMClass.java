/*
  Copyright (C) 2002, 2003, 2004 Jeroen Frijters

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

import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import cli.System.Type;

abstract class VMClass 
{
    private VMClass() {}

    // NOTE this is used in classpath.cs to go from a Class object to a TypeWrapper
    // HACK public because we want to create a delegate to call it
    public static Object getWrapperFromClass(Object c)
    {
	return ((Class)c).vmdata;
    }

    // HACK public because we want to create a delegate to call it
    public static Object createClass(Object wrapper, Object protectionDomain)
    {
	return new Class(wrapper, (java.security.ProtectionDomain)protectionDomain);
    }

    // HACK we need a way to call ClassLoader.loadClass() from C#, so we need this helper method
    // and its public because we want to create a delegate to call it
    public static Object loadClassHelper(Object loader, String name) throws java.lang.ClassNotFoundException
    {
	return ((ClassLoader)loader).loadClass(name).vmdata;
    }
    
    static boolean isInstance(Class clazz, Object o)
    {
	return o != null && clazz.isAssignableFrom(o.getClass());
    }

    static boolean isAssignableFrom(Class clazz, Class c)
    {
	// this is implemented by the "native" code, because
	// remapped types can appear to implement interfaces that they don't
	// actually implement
	return IsAssignableFrom(clazz.vmdata, c.vmdata);
    }
    private static native boolean IsAssignableFrom(Object w1, Object w2);

    static boolean isInterface(Class clazz)
    {
	return IsInterface(clazz.vmdata);
    }
    private static native boolean IsInterface(Object wrapper);

    static boolean isPrimitive(Class clazz)
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

    static String getName(Class clazz)
    {
	// getName() is used by the classloader, so it shouldn't trigger a resolve of the class
	return GetName(clazz.vmdata);
    }
    private static native String GetName(Object wrapper);

    static Class getSuperclass(Class clazz)
    {
	return (Class)GetSuperClassFromWrapper(clazz.vmdata);
    }
    private native static Object GetSuperClassFromWrapper(Object wrapper);

    static Class[] getInterfaces(Class clazz)
    {
	Object[] interfaces = GetInterfaces(clazz.vmdata);
	Class[] interfacesClass = new Class[interfaces.length];
	System.arraycopy(interfaces, 0, interfacesClass, 0, interfaces.length);
	return interfacesClass;
    }
    private static native Object[] GetInterfaces(Object wrapper);

    static Class getComponentType(Class clazz)
    {
	// .NET array types can have unfinished element types, but we don't
	// want to expose those, so we may need to finish the type
	return (Class)getComponentClassFromWrapper(clazz.vmdata);
    }
    private static native Object getComponentClassFromWrapper(Object wrapper);

    static int getModifiers(Class clazz)
    {
	return GetModifiers(clazz.vmdata);
    }
    private static native int GetModifiers(Object wrapper);

    static Class getDeclaringClass(Class clazz)
    {
	return (Class)GetDeclaringClass(clazz.vmdata);
    }
    private native static Object GetDeclaringClass(Object wrapper);

    static Class[] getDeclaredClasses(Class clazz, boolean publicOnly)
    {
	Object[] classes = GetDeclaredClasses(clazz.vmdata, publicOnly);
	Class[] classesClass = new Class[classes.length];
	System.arraycopy(classes, 0, classesClass, 0, classes.length);
	return classesClass;
    }
    private static native Object[] GetDeclaredClasses(Object wrapper, boolean publicOnly);

    static Field[] getDeclaredFields(Class clazz, boolean publicOnly)
    {
	Object[] fieldCookies = GetDeclaredFields(clazz.vmdata, publicOnly);
	Field[] fields = new Field[fieldCookies.length];
	for(int i = 0; i < fields.length; i++)
	{
	    fields[i] = createField(clazz, fieldCookies[i]);
	}
	return fields;
    }
    private static native Object[] GetDeclaredFields(Object wrapper, boolean publicOnly);

    static Method[] getDeclaredMethods(Class clazz, boolean publicOnly)
    {
	Object[] methodCookies = GetDeclaredMethods(clazz.vmdata, true, publicOnly);
	Method[] methods = new Method[methodCookies.length];
	for(int i = 0; i < methodCookies.length; i++)
	{
	    methods[i] = createMethod(clazz, methodCookies[i]);
	}
	return methods;
    }
    private static native Object[] GetDeclaredMethods(Object wrapper, boolean methods, boolean publicOnly);

    static Constructor[] getDeclaredConstructors(Class clazz, boolean publicOnly)
    {
	Object[] methodCookies = GetDeclaredMethods(clazz.vmdata, false, publicOnly);
	Constructor[] constructors = new Constructor[methodCookies.length];
	for(int i = 0; i < methodCookies.length; i++)
	{
	    constructors[i] = createConstructor(clazz, methodCookies[i]);
	}
	return constructors;
    }

    // these methods live in map.xml to access package accessible constructors
    private static native Field createField(Class declaringClass, Object fieldCookie);
    private static native Method createMethod(Class declaringClass, Object methodCookie);
    private static native Constructor createConstructor(Class declaringClass, Object methodCookie);

    static ClassLoader getClassLoader(Class clazz)
    {
	// getClassLoader() can be used by the classloader, so it shouldn't trigger a resolve of the class
	return getClassLoader0(clazz.vmdata);
    }
    private static native ClassLoader getClassLoader0(Object wrapper);

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

    static void initialize(Class clazz)
    {
	initialize(clazz.vmdata);
    }
    private static native void initialize(Object wrapper);

    static native Class loadArrayClass(String name, Object classLoader);

    static native void throwException(Throwable t);

    static boolean isArray(Class clazz)
    {
	return IsArray(clazz.vmdata);
    }
    private static native boolean IsArray(Object wrapper);
}
