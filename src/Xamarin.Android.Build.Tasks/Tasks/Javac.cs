// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

#nullable enable

using System;
using System.Linq;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Text;
using System.Collections.Generic;
using Xamarin.Tools.Zip;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class Javac : JavaCompileToolTask
	{
		public override string TaskPrefix => "JVC";

		[Required]
		public string ClassesOutputDirectory { get; set; } = "";

		public string? ClassesZip { get; set; }

		public string? JavaPlatformJarPath { get; set; }

		public string? JavacTargetVersion { get; set; }
		public string? JavacSourceVersion { get; set; }

		public string? JdkVersion { get; set; }

		public override string DefaultErrorCode => "JAVAC0000";

		public override bool RunTask ()
		{
			if (!Directory.Exists (ClassesOutputDirectory))
				Directory.CreateDirectory (ClassesOutputDirectory);
			var result = base.RunTask ();
			if (!result)
				return result;
			// compress all the class files
			if (!ClassesZip.IsNullOrEmpty ()) {
				using (var zip = new ZipArchiveEx (ClassesZip, FileMode.OpenOrCreate)) {
					zip.AutoFlush = false;
					zip.AddDirectory (ClassesOutputDirectory, "", CompressionMethod.Store);
				}
			}
			return result;
		}

		protected override string GenerateCommandLineCommands ()
		{
			//   Running command: C:\Program Files (x86)\Java\jdk1.6.0_20\bin\javac.exe
			//     "-J-Dfile.encoding=UTF8"
			//     "-d" "bin\classes"
			//     "-classpath" "C:\Users\Jonathan\Documents\Visual Studio 2010\Projects\AndroidMSBuildTest\AndroidMSBuildTest\obj\Debug\android\bin\mono.android.jar";"C:\Program Files (x86)\Android\android-sdk-windows\platforms\android-8\android.jar"
			//     "-encoding" "UTF-8"
			//     "@C:\Users\Jonathan\AppData\Local\Temp\tmp79c4ac38.tmp"

			var cmd = new CommandLineBuilder ();

			cmd.AppendSwitchIfNotNull ("-J-Dfile.encoding=", "UTF8");

			cmd.AppendFileNameIfNotNull (string.Format ("@{0}", TemporarySourceListFile));

			if (int.TryParse (JavacSourceVersion, out int sourceVersion) &&
					int.TryParse (JavacTargetVersion, out int targetVersion) &&
					JavacSupportsRelease ()) {
				cmd.AppendSwitchIfNotNull ("--release ", Math.Max (sourceVersion, targetVersion).ToString ());
			} else {
				cmd.AppendSwitchIfNotNull ("-target ", JavacTargetVersion);
				cmd.AppendSwitchIfNotNull ("-source ", JavacSourceVersion);
				// Ignore warning when targeting older Java versions
				// JAVAC : warning : [options] source value 8 is obsolete and will be removed in a future release
				// JAVAC : warning : [options] target value 8 is obsolete and will be removed in a future release
				cmd.AppendSwitchIfNotNull ("-Xlint:", "-options");
			}

			return cmd.ToString ();
		}

		bool JavacSupportsRelease ()
		{
			if (JdkVersion.IsNullOrEmpty ()) {
				return false;
			}
			var jdkVersion  = Version.Parse (JdkVersion);
			return jdkVersion.Major >= 17;
		}

		protected override void WriteOptionsToResponseFile (StreamWriter sw)
		{
			var jars = new List<string> ();
			if (Jars != null)
				jars.AddRange (Jars.Select (i => i.ItemSpec.Replace (@"\", @"\\")));
			if (JavaPlatformJarPath != null)
				jars.Add (JavaPlatformJarPath.Replace (@"\", @"\\"));

			sw.WriteLine ($"-d \"{ClassesOutputDirectory.Replace (@"\", @"\\")}\"");
			sw.WriteLine ("-classpath \"{0}\"", string.Join (Path.PathSeparator.ToString (), jars));
			sw.WriteLine ($"-encoding UTF8");
		}
	}
}
