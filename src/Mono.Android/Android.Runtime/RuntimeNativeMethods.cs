#if INSIDE_MONO_ANDROID_RUNTIME
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Java;
using System.Text;

namespace Android.Runtime
{
	// NOTE: Keep this in sync with the native side in src/native/common/include/managed-interface.hh
	[Flags]
	enum TraceKind : uint
	{
		Java    = 0x01,
		Managed = 0x02,
		Native  = 0x04,
		Signals = 0x08,

		All     = Java | Managed | Native | Signals,
	}

	internal unsafe static class RuntimeNativeMethods
	{
		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static void monodroid_log (LogLevel level, LogCategories category, string message);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static IntPtr monodroid_timing_start (string? message);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static void monodroid_timing_stop (IntPtr sequence, string? message);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static void monodroid_free (IntPtr ptr);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_gref_get ();

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_weak_gref_get ();

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern IntPtr _monodroid_lookup_replacement_type (string jniSimpleReference);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern IntPtr _monodroid_lookup_replacement_method_info (string jniSourceType, string jniMethodName, string jniMethodSignature);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern IntPtr _monodroid_timezone_get_default_id ();

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_getifaddrs (out IntPtr ifap);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_freeifaddrs (IntPtr ifap);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_detect_cpu_and_architecture (ref ushort built_for_cpu, ref ushort running_on_cpu, ref byte is64bit);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static void _monodroid_gc_wait_for_bridge_processing ();

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_gref_log (string message);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_gref_log_new (IntPtr curHandle, byte curType, IntPtr newHandle, byte newType, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_gref_log_delete (IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_weak_gref_new (IntPtr curHandle, byte curType, IntPtr newHandle, byte newType, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_weak_gref_delete (IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_lref_log_new (int lrefc, IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder from, int from_writable);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_lref_log_delete (int lrefc, IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder from, int from_writable);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern IntPtr monodroid_TypeManager_get_java_class_name (IntPtr klass);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_max_gref_get ();

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern IntPtr clr_typemap_managed_to_java (string fullName, IntPtr mvid);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern bool clr_typemap_java_to_managed (string java_type_name, out IntPtr managed_assembly_name, out uint managed_type_token_id);

		[DllImport (RuntimeConstants.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern delegate* unmanaged<MarkCrossReferencesArgs*, void> clr_initialize_gc_bridge (
			delegate* unmanaged<MarkCrossReferencesArgs*, void> bridge_processing_started_callback,
			delegate* unmanaged<MarkCrossReferencesArgs*, void> bridge_processing_finished_callback);

		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		internal static extern void monodroid_unhandled_exception (Exception javaException);

		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		internal static extern unsafe void monodroid_debugger_unhandled_exception (Exception e);
	}
}
#endif // INSIDE_MONO_ANDROID_RUNTIME
