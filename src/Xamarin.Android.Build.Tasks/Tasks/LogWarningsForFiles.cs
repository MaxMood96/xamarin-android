#nullable enable
using System;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class LogWarningsForFiles : AndroidTask
	{
		public override string TaskPrefix => "LWF";

		[Required]
		public ITaskItem[] Files { get; set; } = [];

		[Required]
		public string Code { get; set; } = "";

		[Required]
		public string Text { get; set; } = "";

		public string? SubCategory { get; set; }

		public string? HelpKeyword { get; set; }

		public override bool RunTask ()
		{
			foreach (var item in Files) {
				Log.LogWarning (SubCategory, Code, HelpKeyword, item.ItemSpec
					, 0, 0, 0, 0, Text);
			}
			return !Log.HasLoggedErrors;
		}
	}
}

