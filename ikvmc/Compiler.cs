/*
  Copyright (C) 2002, 2003, 2004, 2005 Jeroen Frijters

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
using System.Collections;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Threading;
using ICSharpCode.SharpZipLib.Zip;
using IKVM.Internal;

class Compiler
{
	private static string manifestMainClass;
	private static ArrayList classes = new ArrayList();
	private static Hashtable resources = new Hashtable();

	private static ArrayList GetArgs(string[] args)
	{
		ArrayList arglist = new ArrayList();
		foreach(string s in args)
		{
			if(s.StartsWith("@"))
			{
				using(StreamReader sr = new StreamReader(s.Substring(1)))
				{
					string line;
					while((line = sr.ReadLine()) != null)
					{
						arglist.Add(line);
					}
				}
			}
			else
			{
				arglist.Add(s);
			}
		}
		return arglist;
	}

	static void Main(string[] args)
	{
		// FXBUG if we run a static initializer that starts a thread, we would never end,
		// so we force an exit here
		Environment.Exit(RealMain(args));
	}

	static int RealMain(string[] args)
	{
		AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
		System.Threading.Thread.CurrentThread.Name = "compiler";
		Tracer.EnableTraceForDebug();
		JVM.CompilerOptions options = new JVM.CompilerOptions();
		options.target = System.Reflection.Emit.PEFileKinds.ConsoleApplication;
		options.guessFileKind = true;
		options.version = "0.0.0.0";
		options.apartment = ApartmentState.STA;
		string defaultAssemblyName = null;
		ArrayList classesToExclude = new ArrayList();
		ArrayList references = new ArrayList();
		ArrayList arglist = GetArgs(args);
		options.props = new Hashtable();
		if(arglist.Count == 0)
		{
			Console.Error.WriteLine("usage: ikvmc [-options] <classOrJar1> ... <classOrJarN>");
			Console.Error.WriteLine();
			Console.Error.WriteLine("options:");
			Console.Error.WriteLine("    -out:<outputfile>          Specify the output filename");
			Console.Error.WriteLine("    -assembly:<name>           Specify assembly name");
			Console.Error.WriteLine("    -target:exe                Build a console executable");
			Console.Error.WriteLine("    -target:winexe             Build a windows executable");
			Console.Error.WriteLine("    -target:library            Build a library");
			Console.Error.WriteLine("    -target:module             Build a module for use by the linker");
			Console.Error.WriteLine("    -keyfile:<keyfilename>     Use keyfile to sign the assembly");
			Console.Error.WriteLine("    -version:<M.m.b.r>         Assembly version");
			Console.Error.WriteLine("    -main:<class>              Specify the class containing the main method");
			Console.Error.WriteLine("    -reference:<filespec>      Reference an assembly (short form -r:<filespec>)");
			Console.Error.WriteLine("    -recurse:<filespec>        Recurse directory and include matching files");
			Console.Error.WriteLine("    -nojni                     Do not generate JNI stub for native methods");
			Console.Error.WriteLine("    -resource:<name>=<path>    Include file as Java resource");
			Console.Error.WriteLine("    -exclude:<filename>        A file containing a list of classes to exclude");
			Console.Error.WriteLine("    -debug                     Generate debug info for the output file");
			Console.Error.WriteLine("    -srcpath:<path>            Prepend path and package name to source file");
			Console.Error.WriteLine("    -apartment:sta             (default) Apply STAThreadAttribute to main");
			Console.Error.WriteLine("    -apartment:mta             Apply MTAThreadAttribute to main");
			Console.Error.WriteLine("    -apartment:none            Don't apply STAThreadAttribute to main");
			Console.Error.WriteLine("    -noglobbing                Don't glob the arguments");
			Console.Error.WriteLine("    -D<name>=<value>           Set system property (at runtime)");
			Console.Error.WriteLine("    -ea[:<packagename>...|:<classname>]");
			Console.Error.WriteLine("    -enableassertions[:<packagename>...|:<classname>]");
			Console.Error.WriteLine("                               Set system property to enable assertions");
			Console.Error.WriteLine("    -da[:<packagename>...|:<classname>]");
			Console.Error.WriteLine("    -disableassertions[:<packagename>...|:<classname>]");
			Console.Error.WriteLine("                               Set system property to disable assertions");
			Console.Error.WriteLine("    -nostacktraceinfo          Don't create metadata to emit rich stack traces");
			Console.Error.WriteLine("    -opt:fields                Remove unused private fields");
			Console.Error.WriteLine("    -Xtrace:<string>           Displays all tracepoints with the given name");
			Console.Error.WriteLine("    -Xmethodtrace:<string>     Build tracing into the specified output methods");
			Console.Error.WriteLine("    -monoBugWorkaround         Workaround metadata bug in Mono 1.0.5 and 1.1.3");
			return 1;
		}
		foreach(string s in arglist)
		{
			if(s[0] == '-')
			{
				if(s.StartsWith("-out:"))
				{
					options.path = s.Substring(5);
				}
				else if(s.StartsWith("-Xtrace:"))
				{
					Tracer.SetTraceLevel(s.Substring(8));
				}
				else if(s.StartsWith("-Xmethodtrace:"))
				{
					Tracer.HandleMethodTrace(s.Substring(14));
				}
				else if(s.StartsWith("-assembly:"))
				{
					options.assembly = s.Substring(10);
				}
				else if(s.StartsWith("-target:"))
				{
					switch(s)
					{
						case "-target:exe":
							options.target = System.Reflection.Emit.PEFileKinds.ConsoleApplication;
							options.guessFileKind = false;
							break;
						case "-target:winexe":
							options.target = System.Reflection.Emit.PEFileKinds.WindowApplication;
							options.guessFileKind = false;
							break;
						case "-target:module":
							options.targetIsModule = true;
							options.target = System.Reflection.Emit.PEFileKinds.Dll;
							options.guessFileKind = false;
							break;
						case "-target:library":
							options.target = System.Reflection.Emit.PEFileKinds.Dll;
							options.guessFileKind = false;
							break;
						default:
							Console.Error.WriteLine("Warning: unrecognized option: {0}", s);
							break;
					}
				}
				else if(s.StartsWith("-apartment:"))
				{
					switch(s)
					{
						case "-apartment:sta":
							options.apartment = ApartmentState.STA;
							break;
						case "-apartment:mta":
							options.apartment = ApartmentState.MTA;
							break;
						case "-apartment:none":
							options.apartment = ApartmentState.Unknown;
							break;
						default:
							Console.Error.WriteLine("Warning: unrecognized option: {0}", s);
							break;
					}
				}
				else if(s == "-noglobbing")
				{
					options.noglobbing = true;
				}
				else if(s.StartsWith("-D"))
				{
					string[] keyvalue = s.Substring(2).Split('=');
					if(keyvalue.Length != 2)
					{
						keyvalue = new string[] { keyvalue[0], "" };
					}
					options.props[keyvalue[0]] = keyvalue[1];
				}
				else if(s == "-ea" || s == "-enableassertions")
				{
					options.props["ikvm.assert.default"] = "true";
				}
				else if(s == "-da" || s == "-disableassertions")
				{
					options.props["ikvm.assert.default"] = "false";
				}
				else if(s.StartsWith("-ea:") || s.StartsWith("-enableassertions:"))
				{
					options.props["ikvm.assert.enable"] = s.Substring(s.IndexOf(':') + 1);
				}
				else if(s.StartsWith("-da:") || s.StartsWith("-disableassertions:"))
				{
					options.props["ikvm.assert.disable"] = s.Substring(s.IndexOf(':') + 1);
				}
				else if(s.StartsWith("-main:"))
				{
					options.mainClass = s.Substring(6);
				}
				else if(s.StartsWith("-reference:") || s.StartsWith("-r:"))
				{
					string r = s.Substring(s.IndexOf(':') + 1);
					string path = Path.GetDirectoryName(r);
					string[] files = Directory.GetFiles(path == "" ? "." : path, Path.GetFileName(r));
					if(files.Length == 0)
					{
						Console.Error.WriteLine("Error: reference not found: {0}", r);
						return 1;
					}
					foreach(string f in files)
					{
						references.Add(f);
					}
				}
				else if(s.StartsWith("-recurse:"))
				{
					string spec = s.Substring(9);
					bool exists = false;
					// MONOBUG On Mono 1.0.2, Directory.Exists throws an exception if we pass an invalid directory name
					try
					{
						exists = Directory.Exists(spec);
					}
					catch(IOException)
					{
					}
					if(exists)
					{
						DirectoryInfo dir = new DirectoryInfo(spec);
						Recurse(dir, dir, "*");
					}
					else
					{
						try
						{
							DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(spec));
							Recurse(dir, dir, Path.GetFileName(spec));
						}
						catch(PathTooLongException)
						{
							Console.Error.WriteLine("Error: path too long: {0}", spec);
							return 1;
						}
						catch(ArgumentException)
						{
							Console.Error.WriteLine("Error: invalid path: {0}", spec);
							return 1;
						}
					}
				}
				else if(s.StartsWith("-resource:"))
				{
					string[] spec = s.Substring(10).Split('=');
					if(resources.ContainsKey(spec[0]))
					{
						Console.Error.WriteLine("Warning: skipping resource (name clash): " + spec[0]);
					}
					else
					{
						try
						{
							using(FileStream fs = new FileStream(spec[1], FileMode.Open))
							{
								byte[] b = new byte[fs.Length];
								fs.Read(b, 0, b.Length);
								resources.Add(spec[0], b);
							}
						}
						catch(Exception x)
						{
							Console.Error.WriteLine("Error: {0}: {1}", x.Message, spec[1]);
							return 1;
						}
					}
				}
				else if(s == "-nojni")
				{
					options.nojni = true;
				}
				else if(s.StartsWith("-exclude:"))
				{
					ProcessExclusionFile(classesToExclude, s.Substring(9));
				}
				else if(s.StartsWith("-version:"))
				{
					options.version = s.Substring(9);
					if(options.version.EndsWith(".*"))
					{
						options.version = options.version.Substring(0, options.version.Length - 1);
						int count = options.version.Split('.').Length;
						// NOTE this is the published algorithm for generating automatic build and revision numbers
						// (see AssemblyVersionAttribute constructor docs), but it turns out that the revision
						// number is off an hour (on my system)...
						DateTime now = DateTime.Now;
						int seconds = (int)(now.TimeOfDay.TotalSeconds / 2);
						int days = (int)(now - new DateTime(2000, 1, 1)).TotalDays;
						if(count == 3)
						{
							options.version += days + "." + seconds;
						}
						else if(count == 4)
						{
							options.version += seconds;
						}
						else
						{
							Console.Error.WriteLine("Error: Invalid version specified: {0}*", options.version);
							return 1;
						}
					}
				}
				else if(s.StartsWith("-keyfile:"))
				{
					options.keyfilename = s.Substring(9);
				}
				else if(s == "-debug")
				{
					JVM.Debug = true;
				}
				else if(s.StartsWith("-srcpath:"))
				{
					JVM.SourcePath = s.Substring(9);
				}
				else if(s.StartsWith("-remap:"))
				{
					options.remapfile = s.Substring(7);
				}
				else if(s == "-nostacktraceinfo")
				{
					options.nostacktraceinfo = true;
				}
				else if(s == "-monoBugWorkaround")
				{
					options.monoBugWorkaround = true;
				}
				else if(s == "-opt:fields")
				{
					options.removeUnusedFields = true;
				}
				else
				{
					Console.Error.WriteLine("Warning: unrecognized option: {0}", s);
				}
			}
			else
			{
				if(defaultAssemblyName == null)
				{
					try
					{
						defaultAssemblyName = new FileInfo(Path.GetFileName(s)).Name;
					}
					catch(ArgumentException)
					{
						// if the filename contains a wildcard (or any other invalid character), we ignore
						// it as a potential default assembly name
					}
				}
				string[] files;
				try
				{
					string path = Path.GetDirectoryName(s);
					files = Directory.GetFiles(path == "" ? "." : path, Path.GetFileName(s));
				}
				catch(Exception)
				{
					Console.Error.WriteLine("Error: invalid filename: {0}", s);
					return 1;
				}
				if(files.Length == 0)
				{
					Console.Error.WriteLine("Error: file not found: {0}", s);
					return 1;
				}
				foreach(string f in files)
				{
					ProcessFile(null, f);
				}
			}
		}
		if(options.assembly == null)
		{
			string basename = options.path == null ? defaultAssemblyName : new FileInfo(options.path).Name;
			if(basename == null)
			{
				Console.Error.WriteLine("Error: no output file specified");
				return 1;
			}
			int idx = basename.LastIndexOf('.');
			if(idx > 0)
			{
				options.assembly = basename.Substring(0, idx);
			}
			else
			{
				options.assembly = basename;
			}
		}
		if(options.path != null && options.guessFileKind)
		{
			if(options.path.ToLower().EndsWith(".dll"))
			{
				options.target = System.Reflection.Emit.PEFileKinds.Dll;
			}
			options.guessFileKind = false;
		}
		if(options.mainClass == null && manifestMainClass != null && (options.guessFileKind || options.target != System.Reflection.Emit.PEFileKinds.Dll))
		{
			Console.Error.WriteLine("Note: using main class {0} based on jar manifest", manifestMainClass);
			options.mainClass = manifestMainClass;
		}
		try
		{
			options.classes = (byte[][])classes.ToArray(typeof(byte[]));
			options.references = (string[])references.ToArray(typeof(string));
			options.resources = resources;
			options.classesToExclude = (string[])classesToExclude.ToArray(typeof(string));
			return JVM.Compile(options);
		}
		catch(Exception x)
		{
			Console.Error.WriteLine(x);
			return 1;
		}
	}

	private static byte[] ReadFromZip(ZipFile zf, ZipEntry ze)
	{
		byte[] buf = new byte[ze.Size];
		int pos = 0;
		Stream s = zf.GetInputStream(ze);
		while(pos < buf.Length)
		{
			pos += s.Read(buf, pos, buf.Length - pos);
		}
		return buf;
	}

	private static void ProcessZipFile(string file)
	{
		ZipFile zf = new ZipFile(file);
		try
		{
			foreach(ZipEntry ze in zf)
			{
				if(ze.Name.ToLower().EndsWith(".class"))
				{
					classes.Add(ReadFromZip(zf, ze));
				}
				else
				{
					// if it's not a class, we treat it as a resource and the manifest
					// is examined to find the Main-Class
					if(ze.Name == "META-INF/MANIFEST.MF" && manifestMainClass == null)
					{
						// read main class from manifest
						// TODO find out if we can use other information from manifest
						StreamReader rdr = new StreamReader(zf.GetInputStream(ze));
						string line;
						while((line = rdr.ReadLine()) != null)
						{
							if(line.StartsWith("Main-Class: "))
							{
								manifestMainClass = line.Substring(12).Replace('/', '.');
								break;
							}
						}
					}
					if(resources.ContainsKey(ze.Name))
					{
						Console.Error.WriteLine("Warning: skipping resource (name clash): " + ze.Name);
					}
					else
					{
						resources.Add(ze.Name, ReadFromZip(zf, ze));
					}
				}
			}
		}
		finally
		{
			zf.Close();
		}
	}

	private static void ProcessFile(DirectoryInfo baseDir, string file)
	{
		switch(new FileInfo(file).Extension.ToLower())
		{
			case ".class":
				using(FileStream fs = new FileStream(file, FileMode.Open))
				{
					byte[] b = new byte[fs.Length];
					fs.Read(b, 0, b.Length);
					classes.Add(b);
				}
				break;
			case ".jar":
			case ".zip":
				try
				{
					ProcessZipFile(file);
				}
				catch(ICSharpCode.SharpZipLib.ZipException x)
				{
					Console.Error.WriteLine("Warning: error reading {0}: {1}", file, x.Message);
				}
				break;
			default:
			{
				if(baseDir == null)
				{
					Console.Error.WriteLine("Warning: unknown file type: {0}", file);
				}
				else
				{
					// include as resource
					try 
					{
						using(FileStream fs = new FileStream(file, FileMode.Open))
						{
							byte[] b = new byte[fs.Length];
							fs.Read(b, 0, b.Length);
							// extract the resource name by chopping off the base directory
							string name = file.Substring(baseDir.FullName.Length);
							if(name.Length > 0 && name[0] == Path.DirectorySeparatorChar)
							{
								name = name.Substring(1);
							}
							name = name.Replace('\\', '/');
							resources.Add(name, b);
						}
					}
					catch(UnauthorizedAccessException)
					{
						Console.Error.WriteLine("Warning: error reading file {0}: Access Denied", file);
					}
				}
				break;
			}
		}
	}

	private static void Recurse(DirectoryInfo baseDir, DirectoryInfo dir, string spec)
	{
		foreach(FileInfo file in dir.GetFiles(spec))
		{
			ProcessFile(baseDir, file.FullName);
		}
		foreach(DirectoryInfo sub in dir.GetDirectories())
		{
			Recurse(baseDir, sub, spec);
		}
	}

	//This processes an exclusion file with a single regular expression per line
	private static void ProcessExclusionFile(ArrayList classesToExclude, String filename)
	{
		try 
		{
			using(StreamReader file = new StreamReader(filename))
			{
				String line;
				while((line = file.ReadLine()) != null)
				{
					line = line.Trim();
					if(!line.StartsWith("//") && line.Length != 0)
					{
						classesToExclude.Add(line);
					}
				}
			}
		} 
		catch(FileNotFoundException) 
		{
			Console.Error.WriteLine("Warning: could not find exclusion file '{0}'", filename);
		}
	}

	private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
	{
		// make sure all the referenced assemblies are visible (they are loaded with LoadFrom, so
		// they end up in the LoadFrom context [unless they happen to be available in one of the probe paths])
		foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())
		{
			if(a.FullName == args.Name)
			{
				return a;
			}
		}
		return null;
	}
}
