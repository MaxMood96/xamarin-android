#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// Processes .so files coming from @(ResolvedFileToPublish).
	/// * Checks if ABI is valid
	/// * Fixes up libmonodroid.so based on $(AndroidIncludeDebugSymbols)
	/// </summary>
	public class ProcessNativeLibraries : AndroidTask
	{
		const string MonoComponentPrefix = "libmono-component-";

		public override string TaskPrefix => "PRNL";

		static readonly HashSet<string> DebugNativeLibraries = new HashSet<string> (StringComparer.OrdinalIgnoreCase) {
			"libxamarin-debug-app-helper",
		};

		static readonly HashSet<string> RuntimeNativeLibraryNames = new (StringComparer.OrdinalIgnoreCase);

		[Required]
		public ITaskItem[] KnownRuntimeNativeLibraries { get; set; } = [];

		/// <summary>
		/// Assumed to be .so files only
		/// </summary>
		public ITaskItem []? InputLibraries { get; set; }
		public ITaskItem []? Components { get; set; }
		public string []? ExcludedLibraries { get; set; }

		public bool IncludeDebugSymbols { get; set; }
		public bool NativeRuntimeLinking { get; set; }

		[Output]
		public ITaskItem []? OutputLibraries { get; set; }

		public override bool RunTask ()
		{
			if (InputLibraries == null || InputLibraries.Length == 0)
				return true;

			var wantedComponents = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
			if (Components != null && Components.Length > 0) {
				foreach (ITaskItem item in Components) { ;
					wantedComponents.Add ($"{MonoComponentPrefix}{item.ItemSpec}");
				}
			}

			foreach (ITaskItem lib in KnownRuntimeNativeLibraries) {
				RuntimeNativeLibraryNames.Add (Path.GetFileName (lib.ItemSpec));
			}

			var output = new List<ITaskItem> (InputLibraries.Length);

			foreach (var library in InputLibraries) {
				var abi = AndroidRidAbiHelper.GetNativeLibraryAbi (library);
				if (abi.IsNullOrEmpty ()) {
					var packageId = library.GetMetadata ("NuGetPackageId");
					if (!packageId.IsNullOrEmpty ()) {
						Log.LogCodedWarning ("XA4301", library.ItemSpec, 0, Properties.Resources.XA4301_ABI_NuGet, library.ItemSpec, packageId);
					} else {
						Log.LogCodedWarning ("XA4301", library.ItemSpec, 0, Properties.Resources.XA4301_ABI, library.ItemSpec);
					}
					continue;
				}
				// Both lib{mono,net}-android.debug.so and lib{mono,net}-android.release.so are in InputLibraries.
				// Use IncludeDebugSymbols to determine which one to include.
				// We may eventually have files such as `lib{mono,net}-android-checked+asan.release.so` as well.
				var fileName = Path.GetFileNameWithoutExtension (library.ItemSpec);
				if (ExcludedLibraries != null && ExcludedLibraries.Contains (fileName, StringComparer.OrdinalIgnoreCase)) {
					Log.LogDebugMessage ($"Excluding '{library.ItemSpec}'");
					continue;
				}

				if (fileName.StartsWith ("libmono-android", StringComparison.Ordinal) || fileName.StartsWith ("libnet-android", StringComparison.Ordinal)) {
					if (NativeRuntimeLinking) {
						// We don't need the precompiled runtime, it will be linked during application build
						continue;
					}

					if (fileName.EndsWith (".debug", StringComparison.Ordinal)) {
						if (!IncludeDebugSymbols)
							continue;
						library.SetMetadata ("ArchiveFileName", "libmonodroid.so");
					} else if (fileName.EndsWith (".release", StringComparison.Ordinal)) {
						if (IncludeDebugSymbols)
							continue;
						library.SetMetadata ("ArchiveFileName", "libmonodroid.so");
					}
					Log.LogDebugMessage ($"Including runtime: {library}");
				} else if (DebugNativeLibraries.Contains (fileName)) {
					if (!IncludeDebugSymbols) {
						Log.LogDebugMessage ($"Excluding '{library.ItemSpec}' for release builds.");
						continue;
					}
				} else if (fileName.StartsWith (MonoComponentPrefix, StringComparison.OrdinalIgnoreCase)) {
					if (!wantedComponents.Contains (fileName)) {
						continue;
					}
				}

				if (!IgnoreLibraryWhenLinkingRuntime (library)) {
					output.Add (library);
				} else {
					Log.LogDebugMessage ($"Ignoring '{library.ItemSpec}'");
				}
			}

			OutputLibraries = output.ToArray ();

			return !Log.HasLoggedErrors;
		}

		bool IgnoreLibraryWhenLinkingRuntime (ITaskItem libItem)
		{
			if (!NativeRuntimeLinking) {
				return false;
			}

			// We ignore all the shared libraries coming from the runtime packages, as they are all linked into our runtime and
			// need not be packaged.
			if (MonoAndroidHelper.IsFromAKnownRuntimePack (libItem)) {
				return true;
			}

			// Should `NuGetPackageId` be empty, we check the libs by name, as the last resort.
			return RuntimeNativeLibraryNames.Contains (Path.GetFileName (libItem.ItemSpec));
		}
	}
}
