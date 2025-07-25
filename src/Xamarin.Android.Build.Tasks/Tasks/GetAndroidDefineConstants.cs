// Copyright (C) 2011 Xamarin, Inc. All rights reserved.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class GetAndroidDefineConstants : AndroidTask
	{
		public override string TaskPrefix => "GAD";

		[Required]
		public int AndroidApiLevel { get; set; }

		public string? ProductVersion         { get; set; }

		[Output]
		public  ITaskItem[]?    AndroidDefineConstants      { get; set; }

		public override bool RunTask ()
		{
			var constants = new List<ITaskItem> ();

			if (!ProductVersion.IsNullOrEmpty ()) {
				var version = Regex.Replace (ProductVersion, "[^A-Za-z0-9]", "_");
				constants.Add (new TaskItem ($"__XAMARIN_ANDROID_{version}__"));
			}

			constants.Add (new TaskItem ("__MOBILE__"));
			constants.Add (new TaskItem ("__ANDROID__"));

			for (int i = 1; i <= AndroidApiLevel; ++i) {
				constants.Add (new TaskItem ($"__ANDROID_{i}__"));
			}

			AndroidDefineConstants = constants.ToArray ();

			return true;
		}
	}
}
