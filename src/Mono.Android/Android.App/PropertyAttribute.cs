//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by 'manifest-attribute-codegen'.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

#nullable enable

using System;

namespace Android.App;

[Serializable]
[AttributeUsage (AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed partial class PropertyAttribute : Attribute {
	public string Name { get; private set; }

	public string? Resource { get; set; }

	public string? Value { get; set; }

#if XABT_MANIFEST_EXTENSIONS
	static Xamarin.Android.Manifest.ManifestDocumentElement<PropertyAttribute> mapping = new ("property");

	static PropertyAttribute ()
	{
		mapping.Add (
			member: "Name",
			attributeName: "name",
			getter: self => self.Name,
			setter: null
		);
		mapping.Add (
			member: "Resource",
			attributeName: "resource",
			getter: self => self.Resource,
			setter: (self, value) => self.Resource = (string?) value
		);
		mapping.Add (
			member: "Value",
			attributeName: "value",
			getter: self => self.Value,
			setter: (self, value) => self.Value = (string?) value
		);

		AddManualMapping ();
	}

	static partial void AddManualMapping ();
#endif // XABT_MANIFEST_EXTENSIONS
}