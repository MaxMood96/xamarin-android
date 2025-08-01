// Copyright (C) 2011 Xamarin, Inc. All rights reserved.
#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks {

	//aapt2 link -o resources.apk.bk --manifest Foo.xml --java . --custom-package com.infinitespace_studios.blankforms -R foo2 -v --auto-add-overlay
	public class Aapt2Link : Aapt2 {
		static readonly Regex exraArgSplitRegEx = new Regex (@"[\""].+?[\""]|[\''].+?[\'']|[^ ]+", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
		public override string TaskPrefix => "A2L";

		[Required]
		public ITaskItem [] ManifestFiles { get; set; } = [];

		[Required]
		public string JavaPlatformJarPath { get; set; } = "";

		public string? PackageName { get; set; }

		public ITaskItem []? AdditionalResourceArchives { get; set; }

		public ITaskItem []? AdditionalAndroidResourcePaths { get; set; }

		public ITaskItem []? LibraryProjectJars { get; set; }

		public ITaskItem? CompiledResourceFlatArchive { get; set; }

		public ITaskItem []? CompiledResourceFlatFiles { get; set; }

		public string? AndroidComponentResgenFlagFile { get; set; }

		public string? AssetsDirectory { get; set; }

		public ITaskItem []? AdditionalAndroidAssetPaths { get; set; }

		public string? ExtraPackages { get; set; }

		public string? ExtraArgs { get; set; }

		public bool CreatePackagePerAbi { get; set; }

		public string []? SupportedAbis { get; set; }

		public string? OutputFile { get; set; }

		public string? JavaDesignerOutputDirectory { get; set; }

		public string? UncompressedFileExtensions { get; set; }

		public string? AndroidSdkPlatform { get; set; }

		public string? VersionCodePattern { get; set; }

		public string? VersionCodeProperties { get; set; }

		public string? AssemblyIdentityMapFile { get; set; }

		public string? OutputImportDirectory { get; set; }

		public string? ImportsDirectory { get; set; }

		public bool NonConstantId { get; set; }

		public bool ProtobufFormat { get; set; }

		public string? ProguardRuleOutput { get; set; }

		AssemblyIdentityMap assemblyMap = new AssemblyIdentityMap ();
		List<string> tempFiles = new List<string> ();
		SortedSet<string> rulesFiles = new SortedSet<string> ();
		Dictionary<string, long> apks = new Dictionary<string, long> ();
		string? resourceSymbolsTextFileTemp;

		protected override int GetRequiredDaemonInstances ()
		{
			return Math.Min (CreatePackagePerAbi ? (SupportedAbis?.Length ?? 1) : 1, DaemonMaxInstanceCount);
		}

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			try {
				assemblyMap.Load (Path.Combine (WorkingDirectory, AssemblyIdentityMapFile));

				resourceSymbolsTextFileTemp = GetTempFile ();

				await this.WhenAll (ManifestFiles, ProcessManifest);

				ProcessOutput ();
				// now check for
				foreach (var kvp in apks) {
					string currentResourceOutputFile = kvp.Key;
					bool aaptResult = Daemon.JobSucceded (kvp.Value);
					LogDebugMessage ($"Processing {currentResourceOutputFile} JobId: {kvp.Value} Exists: {File.Exists (currentResourceOutputFile)} JobWorked: {aaptResult}");
					if (!currentResourceOutputFile.IsNullOrEmpty ()) {
						var tmpfile = currentResourceOutputFile + ".bk";
						// aapt2 might not produce an archive and we must provide
						// and -o foo even if we don't want one.
						if (File.Exists (tmpfile)) {
							if (aaptResult) {
								LogDebugMessage ($"Copying {tmpfile} to {currentResourceOutputFile}");
								Files.CopyIfZipChanged (tmpfile, currentResourceOutputFile);
							}
							File.Delete (tmpfile);
						}
						// Delete the archive on failure
						if (!aaptResult && File.Exists (currentResourceOutputFile)) {
							LogDebugMessage ($"Link did not succeed. Deleting {currentResourceOutputFile}");
							File.Delete (currentResourceOutputFile);
						}
					}
				}
				if (!ProguardRuleOutput.IsNullOrEmpty ()) {
					// combine the "proguard" temp files into one file.
					var sb = new StringBuilder ();
					sb.AppendLine ("#Auto Generated file. Do not Edit.");
					lock (rulesFiles) {
						foreach (var file in rulesFiles) {
							sb.AppendLine ($"# Data from {file}");
							foreach (var line in File.ReadLines (file))
								sb.AppendLine (line);
						}
					}
					Files.CopyIfStringChanged (sb.ToString (), ProguardRuleOutput);
				}
				if (!ResourceSymbolsTextFile.IsNullOrEmpty ())
					Files.CopyIfChanged (resourceSymbolsTextFileTemp, GetFullPath (ResourceSymbolsTextFile));
			} finally {
				lock (tempFiles) {
					foreach (var temp in tempFiles) {
						File.Delete (temp);
					}
					tempFiles.Clear ();
				}
			}
		}

		string [] GenerateCommandLineCommands (string ManifestFile, string? currentAbi, string currentResourceOutputFile)
		{
			List<string> cmd = new List<string> ();
			string manifestDir = Path.Combine (Path.GetDirectoryName (ManifestFile), currentAbi != null ? currentAbi : "manifest");
			Directory.CreateDirectory (manifestDir);
			string manifestFile = Path.Combine (manifestDir, Path.GetFileName (ManifestFile));
			ManifestDocument manifest = new ManifestDocument (ManifestFile);
			manifest.TargetSdkVersion = AndroidSdkPlatform;
			if (!VersionCodePattern.IsNullOrEmpty ()) {
				try {
					manifest.CalculateVersionCode (currentAbi, VersionCodePattern, VersionCodeProperties);
				} catch (ArgumentOutOfRangeException ex) {
					LogCodedError ("XA0003", ManifestFile, 0, ex.Message);
					return cmd.ToArray ();
				}
			}
			if (currentAbi != null && VersionCodePattern.IsNullOrEmpty ()) {
				manifest.SetAbi (currentAbi);
			}
			if (!manifest.ValidateVersionCode (out string error, out string errorCode)) {
				LogCodedError (errorCode, ManifestFile, 0, error);
				return cmd.ToArray ();
			}
			manifest.Save (LogCodedWarning, manifestFile);

			cmd.Add ("link");
			if (MonoAndroidHelper.LogInternalExceptions)
				cmd.Add ("-v");
			cmd.Add ($"--manifest");
			cmd.Add (GetFullPath (manifestFile));
			if (!JavaDesignerOutputDirectory.IsNullOrEmpty ()) {
				var designerDirectory = Path.IsPathRooted (JavaDesignerOutputDirectory) ? JavaDesignerOutputDirectory : Path.Combine (WorkingDirectory, JavaDesignerOutputDirectory);
				Directory.CreateDirectory (designerDirectory);
				cmd.Add ("--java");
				cmd.Add (GetFullPath (JavaDesignerOutputDirectory));
			}
			if (PackageName != null) {
				cmd.Add ("--custom-package");
				cmd.Add (PackageName.ToLowerInvariant ());
			}

			if (AdditionalResourceArchives != null) {
				for (int i = AdditionalResourceArchives.Length - 1; i >= 0; i--) {
					var flata = GetFullPath (AdditionalResourceArchives [i].ItemSpec);
					if (Directory.Exists (flata)) {
						foreach (var line in Directory.EnumerateFiles (flata, "*.flat", SearchOption.TopDirectoryOnly)) {
							cmd.Add ("-R");
							cmd.Add (GetFullPath (line));
						}
					} else if (File.Exists (flata)) {
						cmd.Add ("-R");
						cmd.Add (flata);
					} else {
						LogDebugMessage ($"Archive does not exist: {flata}");
					}
				}
			}

			if (CompiledResourceFlatArchive != null) {
				var flata = GetFullPath (CompiledResourceFlatArchive.ItemSpec);
				if (Directory.Exists (flata)) {
					foreach (var line in Directory.EnumerateFiles (flata, "*.flat", SearchOption.TopDirectoryOnly)) {
						cmd.Add ("-R");
						cmd.Add (GetFullPath (line));
					}
				} else if (File.Exists (flata)) {
					cmd.Add ("-R");
					cmd.Add (flata);
				} else {
					LogDebugMessage ($"Archive does not exist: {flata}");
				}
			}

			if (CompiledResourceFlatFiles != null) {
				var appFiles = new List<string> ();
				for (int i = CompiledResourceFlatFiles.Length - 1; i >= 0; i--) {
					var file = CompiledResourceFlatFiles [i];
					var fullPath = GetFullPath (file.ItemSpec);
					if (!File.Exists (fullPath)) {
						LogDebugMessage ($"File does not exist: {fullPath}");
					} else if (!file.GetMetadata ("ResourceDirectory").IsNullOrEmpty ()) {
						cmd.Add ("-R");
						cmd.Add (fullPath);
					} else {
						appFiles.Add (fullPath);
					}
				}
				foreach (var fullPath in appFiles) {
					cmd.Add ("-R");
					cmd.Add (fullPath);
				}
			}

			cmd.Add ("--auto-add-overlay");

			if (!UncompressedFileExtensions.IsNullOrWhiteSpace ()) {
				foreach (var ext in UncompressedFileExtensions.Split (new char [] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)) {
					cmd.Add ("-0");
					cmd.Add (ext.StartsWith (".", StringComparison.OrdinalIgnoreCase) ? ext : $".{ext}");
				}
			}

			if (!ExtraPackages.IsNullOrEmpty ()) {
				cmd.Add ("--extra-packages");
				cmd.Add (ExtraPackages);
			}

			cmd.Add ("-I");
			cmd.Add (GetFullPath (JavaPlatformJarPath));

			if (!ResourceSymbolsTextFile.IsNullOrEmpty ()) {
				cmd.Add ("--output-text-symbols");
				cmd.Add (GetFullPath (resourceSymbolsTextFileTemp ?? ""));
			}

			if (ProtobufFormat)
				cmd.Add ("--proto-format");

			if (!ExtraArgs.IsNullOrWhiteSpace ()) {
				foreach (Match match in exraArgSplitRegEx.Matches (ExtraArgs)) {
					string value = match.Value.Trim (' ', '"', '\'');
					if (!value.IsNullOrEmpty ())
						cmd.Add (value);
				}
			}

			var hasAssetsErrors = false;
			// When adding Assets the first item found takes precedence.
			// So we need to add the application Assets first.
			if (!AssetsDirectory.IsNullOrEmpty ()) {
				var assetDir = GetFullPath (AssetsDirectory.TrimEnd ('\\'));
				if (Directory.Exists (assetDir)) {
					if (OS.IsWindows && !IsPathOnlyASCII (assetDir)) {
						hasAssetsErrors = true;
						LogCodedError ("APT2265", Properties.Resources.APT2265, assetDir);
					}
					cmd.Add ("-A");
					cmd.Add (assetDir);
				} else {
					LogDebugMessage ($"asset directory did not exist: {assetDir}");
				}
			}

			if (AdditionalAndroidAssetPaths != null) {
				for (int i = 0; i < AdditionalAndroidAssetPaths.Length; i++) {
					var assetDir = GetFullPath (AdditionalAndroidAssetPaths [i].ItemSpec.TrimEnd ('\\'));
					if (!assetDir.IsNullOrWhiteSpace ()) {
						if (Directory.Exists (assetDir)) {
							if (OS.IsWindows && !IsPathOnlyASCII (assetDir)) {
								hasAssetsErrors = true;
								LogCodedError ("APT2265", Properties.Resources.APT2265, assetDir);
								continue;
							}
							cmd.Add ("-A");
							cmd.Add (GetFullPath (assetDir));
						} else {
							LogDebugMessage ($"asset directory did not exist: {assetDir}");
						}
					}
				}
			}

			if (hasAssetsErrors) {
				return [];
			}

			if (!ProguardRuleOutput.IsNullOrEmpty ()) {
				cmd.Add ("--proguard");
				cmd.Add (GetFullPath (GetManifestRulesFile (manifestDir)));
			}
			cmd.Add ("-o");
			cmd.Add (GetFullPath (currentResourceOutputFile));

			return cmd.ToArray ();
		}

		bool ExecuteForAbi (string [] cmd, string currentResourceOutputFile)
		{
			lock (apks)
				apks.Add (currentResourceOutputFile, RunAapt (cmd, currentResourceOutputFile));
			return true;
		}

		bool ManifestIsUpToDate (string manifestFile)
		{
			return !String.IsNullOrEmpty (AndroidComponentResgenFlagFile) &&
				File.Exists (AndroidComponentResgenFlagFile) && File.Exists (manifestFile) &&
				File.GetLastWriteTime (AndroidComponentResgenFlagFile) > File.GetLastWriteTime (manifestFile);
		}

		void ProcessManifest (ITaskItem manifestFile)
		{
			var manifest = GetFullPath (manifestFile.ItemSpec);
			if (!File.Exists (manifest)) {
				LogDebugMessage ("{0} does not exists. Skipping", manifest);
				return;
			}

			bool upToDate = ManifestIsUpToDate (manifest);

			if (AdditionalAndroidResourcePaths != null)
				foreach (var dir in AdditionalAndroidResourcePaths)
					if (!dir.ItemSpec.IsNullOrEmpty ())
						upToDate = upToDate && ManifestIsUpToDate (string.Format ("{0}{1}{2}{3}{4}", dir, Path.DirectorySeparatorChar, "manifest", Path.DirectorySeparatorChar, "AndroidManifest.xml"));

			if (upToDate) {
				LogMessage ("  Additional Android Resources manifsets files are unchanged. Skipping.");
				return;
			}

			var defaultAbi = new string? [] { null };
			var abis = CreatePackagePerAbi && SupportedAbis?.Length > 1 ? defaultAbi.Concat (SupportedAbis) : defaultAbi;
			var outputFile = OutputFile.IsNullOrEmpty () ? GetTempFile () : OutputFile;
			foreach (var abi in abis) {
				var currentResourceOutputFile = abi != null ? string.Format ("{0}-{1}", outputFile, abi) : outputFile;
				if (!currentResourceOutputFile.IsNullOrEmpty () && !Path.IsPathRooted (currentResourceOutputFile))
					currentResourceOutputFile = Path.Combine (WorkingDirectory, currentResourceOutputFile);
				string[] cmd = GenerateCommandLineCommands (manifest, abi, currentResourceOutputFile);
				if (!cmd.Any () || !ExecuteForAbi (cmd, currentResourceOutputFile)) {
					Cancel ();
				}
			}
		}

		string GetManifestRulesFile (string manifestDir)
		{
			string rulesFile = Path.Combine (manifestDir, "aapt_rules.txt");
			lock (rulesFiles)
				rulesFiles.Add (rulesFile);
			return rulesFile;
		}

		string GetTempFile ()
		{
			var temp = Path.GetTempFileName ();
			lock (tempFiles)
				tempFiles.Add (temp);
			return temp;
		}
	}

}
