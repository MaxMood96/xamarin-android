using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Java.Interop.Tools.Diagnostics;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	// can't be a single ToolTask, because it has to run mkbundle many times for each arch.
	public class MakeBundleNativeCodeExternal : AndroidTask
	{
		public override string TaskPrefix => "MBN";

		const string BundleSharedLibraryName = "libmonodroid_bundle_app.so";

		public string? AndroidNdkDirectory { get; set; }

		[Required]
		public ITaskItem[] Assemblies { get; set; } = [];

		// Which ABIs to include native libs for
		[Required]
		public string [] SupportedAbis { get; set; } = [];

		[Required]
		public string TempOutputPath { get; set; } = "";

		public string? IncludePath { get; set; }

		[Required]
		public string ToolPath { get; set; } = "";

		public bool AutoDeps { get; set; }
		public bool EmbedDebugSymbols { get; set; }
		public bool KeepTemp { get; set; }

		[Required]
		public string BundleApiPath { get; set; } = "";

		[Output]
		public ITaskItem []? OutputNativeLibraries { get; set; }

		public MakeBundleNativeCodeExternal ()
		{
		}

		public override bool RunTask ()
		{
			NdkTools ndk = NdkTools.Create (AndroidNdkDirectory, logErrors: true, log: Log);
			if (Log.HasLoggedErrors) {
				return false; // NdkTools.Create will log appropriate error
			}

			try {
				return DoExecute (ndk);
			} catch (XamarinAndroidException e) {
				Log.LogCodedError (string.Format ("XA{0:0000}", e.Code), e.MessageWithoutCode);
				if (MonoAndroidHelper.LogInternalExceptions)
					Log.LogMessage (e.ToString ());
			} catch (Exception ex) {
				Log.LogErrorFromException (ex);
			}
			return !Log.HasLoggedErrors;
		}

		bool DoExecute (NdkTools ndk)
		{
			var results = new List<ITaskItem> ();
			string bundlepath = Path.Combine (TempOutputPath, "bundles");
			if (!Directory.Exists (bundlepath))
				Directory.CreateDirectory (bundlepath);
			else
				Directory.Delete (bundlepath, true);
			foreach (var abi in SupportedAbis) {
				AndroidTargetArch arch = AndroidTargetArch.Other;
				switch (abi) {
				case "arm64":
				case "arm64-v8a":
				case "aarch64":
					arch = AndroidTargetArch.Arm64;
					break;
				case "armeabi-v7a":
					arch = AndroidTargetArch.Arm;
					break;
				case "x86":
					arch = AndroidTargetArch.X86;
					break;
				case "x86_64":
					arch = AndroidTargetArch.X86_64;
					break;
				case "mips":
					arch = AndroidTargetArch.Mips;
					break;
				}

				if (!ndk.ValidateNdkPlatform (arch, enableLLVM: false)) {
					return false;
				}

				int level = ndk.GetMinimumApiLevelFor (arch);
				var outpath = Path.Combine (bundlepath, abi);
				if (!Directory.Exists (outpath))
					Directory.CreateDirectory (outpath);

				var clb = new CommandLineBuilder ();
				clb.AppendSwitch ("--dos2unix=false");
				clb.AppendSwitch ("--nomain");
				clb.AppendSwitch ("--i18n none");
				clb.AppendSwitch ("--bundled-header");
				clb.AppendSwitch ("--mono-api-struct-path");
				clb.AppendFileNameIfNotNull (BundleApiPath);
				clb.AppendSwitch ("--style");
				clb.AppendSwitch ("linux");
				clb.AppendSwitch ("-c");
				clb.AppendSwitch ("-o");
				clb.AppendFileNameIfNotNull (Path.Combine (outpath, "temp.c"));
				clb.AppendSwitch ("-oo");
				clb.AppendFileNameIfNotNull (Path.Combine (outpath, "assemblies.o"));
				if (AutoDeps)
					clb.AppendSwitch ("--autodeps");
				if (KeepTemp)
					clb.AppendSwitch ("--keeptemp");
				clb.AppendSwitch ("-z"); // Compress
				clb.AppendFileNamesIfNotNull (Assemblies, " ");
				var psi = new ProcessStartInfo () {
					FileName = MkbundlePath,
					Arguments = clb.ToString (),
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
					WindowStyle = ProcessWindowStyle.Hidden,
				};
				string windowsCompilerSwitches = ndk.GetCompilerTargetParameters (arch, level);
				var compilerNoQuotes = ndk.GetToolPath (NdkToolKind.CompilerC, arch, level);
				var compiler = $"\"{compilerNoQuotes}\" {windowsCompilerSwitches}".Trim ();
				var gas = '"' + ndk.GetToolPath (NdkToolKind.Assembler, arch, level) + '"';
				psi.EnvironmentVariables ["CC"] = compiler;
				psi.EnvironmentVariables ["AS"] = gas;
				Log.LogDebugMessage ("CC=" + compiler);
				Log.LogDebugMessage ("AS=" + gas);
				//psi.EnvironmentVariables ["PKG_CONFIG_PATH"] = Path.Combine (Path.GetDirectoryName (MonoDroidSdk.MandroidTool), "lib", abi);
				Log.LogDebugMessage ("[mkbundle] " + psi.FileName + " " + clb);
				var proc = new Process ();
				proc.OutputDataReceived += OnMkbundleOutputData;
				proc.ErrorDataReceived += OnMkbundleErrorData;
				proc.StartInfo = psi;
				proc.Start ();
				proc.BeginOutputReadLine ();
				proc.BeginErrorReadLine ();
				proc.WaitForExit ();
				if (proc.ExitCode != 0) {
					Log.LogCodedError ("XA5102", Properties.Resources.XA5102, proc.ExitCode);
					return false;
				}

				// then compile temp.c into temp.o and ...

				clb = new CommandLineBuilder ();

				// See NdkToolsWithClangWithPlatforms.ctor for reasons why
				if (!String.IsNullOrEmpty (windowsCompilerSwitches))
					clb.AppendTextUnquoted (windowsCompilerSwitches);

				clb.AppendSwitch ("-c");

				// This is necessary only when unified headers are in use but it won't hurt to have it
				// defined even if we don't use them
				clb.AppendSwitch ($"-D__ANDROID_API__={level}");

				// This is necessary because of the injected code, which is reused between libmonodroid
				// and the bundle
				clb.AppendSwitch ("-DANDROID");

				clb.AppendSwitch ("-o");
				clb.AppendFileNameIfNotNull (Path.Combine (outpath, "temp.o"));
				if (!string.IsNullOrWhiteSpace (IncludePath)) {
					clb.AppendSwitch ("-I");
					clb.AppendFileNameIfNotNull (IncludePath);
				}

				string asmIncludePath = ndk.GetDirectoryPath (NdkToolchainDir.AsmInclude, arch, level);
				if (!String.IsNullOrEmpty (asmIncludePath)) {
					clb.AppendSwitch ("-I");
					clb.AppendFileNameIfNotNull (asmIncludePath);
				}

				clb.AppendSwitch ("-I");
				clb.AppendFileNameIfNotNull (ndk.GetDirectoryPath (NdkToolchainDir.PlatformInclude, arch, level));
				clb.AppendFileNameIfNotNull (Path.Combine (outpath, "temp.c"));
				Log.LogDebugMessage ("[CC] " + compiler + " " + clb);
				if (MonoAndroidHelper.RunProcess (compilerNoQuotes, clb.ToString (), OnCcOutputData,  OnCcErrorData) != 0) {
					Log.LogCodedError ("XA5103", Properties.Resources.XA5103, proc.ExitCode);
					return false;
				}

				// ... link temp.o and assemblies.o into app.so

				clb = new CommandLineBuilder ();
				clb.AppendSwitch ("--shared");
				clb.AppendFileNameIfNotNull (Path.Combine (outpath, "temp.o"));
				clb.AppendFileNameIfNotNull (Path.Combine (outpath, "assemblies.o"));

				// API23+ requires that the shared library has its soname set or it won't load
				clb.AppendSwitch ("-soname");
				clb.AppendSwitch (BundleSharedLibraryName);
				clb.AppendSwitch ("-o");
				clb.AppendFileNameIfNotNull (Path.Combine (outpath, BundleSharedLibraryName));
				clb.AppendSwitch ("-L");
				clb.AppendFileNameIfNotNull (ndk.GetDirectoryPath (NdkToolchainDir.PlatformLib, arch, level));
				clb.AppendSwitch ("-lc");
				clb.AppendSwitch ("-lm");
				clb.AppendSwitch ("-ldl");
				clb.AppendSwitch ("-llog");
				clb.AppendSwitch ("-lz"); // Compress
				string ld = ndk.GetToolPath (NdkToolKind.Linker, arch, level);
				Log.LogMessage (MessageImportance.Normal, "[LD] " + ld + " " + clb);
				if (MonoAndroidHelper.RunProcess (ld, clb.ToString (), OnLdOutputData,  OnLdErrorData) != 0) {
					Log.LogCodedError ("XA5201", Properties.Resources.XA5201, proc.ExitCode);
					return false;
				}
				results.Add (new TaskItem (Path.Combine (outpath, "libmonodroid_bundle_app.so")));
			}
			OutputNativeLibraries = results.ToArray ();
			return true;
		}

		void OnCcOutputData (object sender, DataReceivedEventArgs e)
		{
			Log.LogDebugMessage ("[cc stdout] {0}", e.Data ?? "");
		}

		void OnCcErrorData (object sender, DataReceivedEventArgs e)
		{
			Log.LogMessage ("[cc stderr] {0}", e.Data ?? "");
		}

		void OnLdOutputData (object sender, DataReceivedEventArgs e)
		{
			Log.LogDebugMessage ("[ld stdout] {0}", e.Data ?? "");
		}

		void OnLdErrorData (object sender, DataReceivedEventArgs e)
		{
			Log.LogMessage ("[ld stderr] {0}", e.Data ?? "");
		}

		void OnMkbundleOutputData (object sender, DataReceivedEventArgs e)
		{
			Log.LogDebugMessage ("[mkbundle stdout] {0}", e.Data ?? "");
		}

		void OnMkbundleErrorData (object sender, DataReceivedEventArgs e)
		{
			Log.LogMessage ("[mkbundle stderr] {0}", e.Data ?? "");
		}

		string MkbundlePath {
			get {
				return Path.Combine (ToolPath, "mkbundle.exe");
			}
		}
	}
}
