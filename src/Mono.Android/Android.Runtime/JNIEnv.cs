using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

using Java.Interop;
using Java.Interop.Tools.TypeNameMappings;
using Microsoft.Android.Runtime;

namespace Android.Runtime {
	public static partial class JNIEnv {
		const DynamicallyAccessedMemberTypes Constructors = DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors;

		[ThreadStatic]
		static byte[]? mvid_bytes;

		public static IntPtr Handle => JniEnvironment.EnvironmentPointer;

		static Array ArrayCreateInstance (Type elementType, int length) =>
			// FIXME: https://github.com/xamarin/xamarin-android/issues/8724
			// IL3050 disabled in source: if someone uses NativeAOT, they will get the warning.
			#pragma warning disable IL3050
			Array.CreateInstance (elementType, length);
			#pragma warning restore IL3050

		static Type MakeArrayType (Type type) =>
			// FIXME: https://github.com/xamarin/xamarin-android/issues/8724
			// IL3050 disabled in source: if someone uses NativeAOT, they will get the warning.
			#pragma warning disable IL3050
			type.MakeArrayType ();
			#pragma warning restore IL3050

		internal static IntPtr IdentityHash (IntPtr v)
		{
			return JniEnvironment.References.GetIdentityHashCode (new JniObjectReference (v));
		}

		public static void CheckHandle (IntPtr jnienv)
		{
			new JniTransition (jnienv).Dispose ();
		}

		internal static bool IsGCUserPeer (IntPtr value)
		{
			if (value == IntPtr.Zero)
				return false;

			return IsInstanceOf (value, JNIEnvInit.grefIGCUserPeer_class) ||
				IsInstanceOf (value, JNIEnvInit.grefGCUserPeerable_class);
		}

		internal static bool ShouldWrapJavaException (Java.Lang.Throwable? t, [CallerMemberName] string? caller = null)
		{
			if (t == null) {
				RuntimeNativeMethods.monodroid_log (LogLevel.Warn,
				               LogCategories.Default,
				               $"ShouldWrapJavaException was not passed a valid `Java.Lang.Throwable` instance. Called from method `{caller}`");
				return false;
			}

			bool wrap = JNIEnvInit.BoundExceptionType == BoundExceptionType.System;
			if (!wrap) {
				RuntimeNativeMethods.monodroid_log (
					LogLevel.Warn,
					LogCategories.Default,
					$"Not wrapping exception of type {t.GetType().FullName} from method `{caller}`. This will change in a future release."
				);
			}

			return wrap;
		}

		static void MonoDroidUnhandledException (Exception ex)
		{
			RuntimeNativeMethods.monodroid_unhandled_exception (ex);
		}

		internal static void PropagateUncaughtException (IntPtr env, IntPtr javaThreadPtr, IntPtr javaExceptionPtr)
		{
			if (!JNIEnvInit.PropagateExceptions)
				return;

			var javaException = JavaObject.GetObject<Java.Lang.Throwable> (env, javaExceptionPtr, JniHandleOwnership.DoNotTransfer)!;

			if (Debugger.IsAttached) {
				AndroidRuntimeInternal.mono_unhandled_exception?.Invoke (javaException);
			}

			try {
				var jltp = javaException as JavaProxyThrowable;
				Exception? innerException = jltp?.InnerException;

				Logger.Log (LogLevel.Info, "MonoDroid", "UNHANDLED EXCEPTION:");
				Logger.Log (LogLevel.Info, "MonoDroid", javaException.ToString ());

				if (RuntimeFeature.IsMonoRuntime) {
					MonoDroidUnhandledException (innerException ?? javaException);
				} else if (RuntimeFeature.IsCoreClrRuntime) {
					// TODO: what to do here?
				} else {
					throw new NotSupportedException ("Internal error: unknown runtime not supported");
				}
			} catch (Exception e) {
				Logger.Log (LogLevel.Error, "monodroid", "Exception thrown while raising AppDomain.UnhandledException event: " + e.ToString ());
			}
		}

		public static void WaitForBridgeProcessing ()
		{
			JniEnvironment.Runtime.ValueManager.WaitForGCBridgeProcessing ();
		}

		public static IntPtr AllocObject (string jniClassName)
		{
			IntPtr jniClass = JNIEnv.FindClass (jniClassName);
			try {
				return AllocObject (jniClass);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static IntPtr AllocObject (Type type)
		{
			IntPtr jniClass = JNIEnv.FindClass (type);
			try {
				return AllocObject (jniClass);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static unsafe IntPtr StartCreateInstance (IntPtr jclass, IntPtr constructorId, JValue* constructorParameters)
		{
			return AllocObject (jclass);
		}

		public static unsafe IntPtr StartCreateInstance (IntPtr jclass, IntPtr constructorId, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return StartCreateInstance (jclass, constructorId, cp);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, IntPtr jclass, IntPtr constructorId, JValue* constructorParameters)
		{
			CallNonvirtualVoidMethod (instance, jclass, constructorId, constructorParameters);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, IntPtr jclass, IntPtr constructorId, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				FinishCreateInstance (instance, jclass, constructorId, cp);
		}

		public static unsafe IntPtr StartCreateInstance (Type type, string jniCtorSignature, JValue* constructorParameters)
		{
			return AllocObject (type);
		}

		public static unsafe IntPtr StartCreateInstance (Type type, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return StartCreateInstance (type, jniCtorSignature, cp);
		}

		public static unsafe IntPtr StartCreateInstance (string jniClassName, string jniCtorSignature, JValue* constructorParameters)
		{
			return AllocObject (jniClassName);
		}

		public static unsafe IntPtr StartCreateInstance (string jniClassName, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return StartCreateInstance (jniClassName, jniCtorSignature, cp);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, string jniCtorSignature, JValue* constructorParameters)
		{
			InvokeConstructor (instance, jniCtorSignature, constructorParameters);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				FinishCreateInstance (instance, jniCtorSignature, cp);
		}

		public static unsafe void InvokeConstructor (IntPtr instance, string jniCtorSignature, JValue* constructorParameters)
		{
			IntPtr lrefClass = GetObjectClass (instance);
			try {
				IntPtr ctor = JNIEnv.GetMethodID (lrefClass, "<init>", jniCtorSignature);
				if (ctor == IntPtr.Zero)
					throw new ArgumentException (FormattableString.Invariant (
						$"Could not find constructor JNI signature '{jniCtorSignature}' on type '{JniEnvironment.Types.GetJniTypeNameFromClass (new JniObjectReference (lrefClass))}'."));
				CallNonvirtualVoidMethod (instance, lrefClass, ctor, constructorParameters);
			} finally {
				DeleteLocalRef (lrefClass);
			}
		}

		public static unsafe void InvokeConstructor (IntPtr instance, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				InvokeConstructor (instance, jniCtorSignature, cp);
		}

		public static unsafe IntPtr CreateInstance (IntPtr jniClass, string signature, JValue* constructorParameters)
		{
			IntPtr ctor = JNIEnv.GetMethodID (jniClass, "<init>", signature);
			if (ctor == IntPtr.Zero)
				throw new ArgumentException (FormattableString.Invariant (
					$"Could not find constructor JNI signature '{signature}' on type '{JniEnvironment.Types.GetJniTypeNameFromClass (new JniObjectReference (jniClass))}'."));
			return JNIEnv.NewObject (jniClass, ctor, constructorParameters);
		}

		public static unsafe IntPtr CreateInstance (IntPtr jniClass, string signature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return CreateInstance (jniClass, signature, cp);
		}

		public static unsafe IntPtr CreateInstance (string jniClassName, string signature, JValue* constructorParameters)
		{
			IntPtr jniClass = JNIEnv.FindClass (jniClassName);
			try {
				return CreateInstance (jniClass, signature, constructorParameters);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static unsafe IntPtr CreateInstance (string jniClassName, string signature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return CreateInstance (jniClassName, signature, cp);
		}

		public static unsafe IntPtr CreateInstance (Type type, string signature, JValue* constructorParameters)
		{
			IntPtr jniClass = JNIEnv.FindClass (type);
			try {
				return CreateInstance (jniClass, signature, constructorParameters);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static unsafe IntPtr CreateInstance (Type type, string signature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return CreateInstance (type, signature, cp);
		}

		public static IntPtr FindClass (System.Type type)
		{
			int rank = JavaNativeTypeManager.GetArrayInfo (type, out type);
			try {
				var sig  = JNIEnvInit.androidRuntime?.TypeManager.GetTypeSignature (type) ?? default;
				if (!sig.IsValid || sig.SimpleReference == null) {
					sig = new JniTypeSignature ("java/lang/Object");
				}
				sig = sig.AddArrayRank (rank);

				JniObjectReference local_ref = JniEnvironment.Types.FindClass (sig.Name);
				IntPtr global_ref = local_ref.NewGlobalRef ().Handle;
				JniObjectReference.Dispose (ref local_ref);
				return global_ref;
			} catch (Java.Lang.Throwable e) {
				if (!((e is Java.Lang.NoClassDefFoundError) || (e is Java.Lang.ClassNotFoundException)))
					throw;
				RuntimeNativeMethods.monodroid_log (LogLevel.Warn, LogCategories.Default, $"JNIEnv.FindClass(Type) caught unexpected exception: {e}");
				var jni = Java.Interop.TypeManager.GetJniTypeName (type);
				if (jni != null) {
					e.Dispose ();
					return FindClass (JavaNativeTypeManager.ToJniName (jni, rank));
				}

				// Though it's tempting to call TypeManager.RegisterType() to avoid
				// calling GetCustomAttributes() again, this isn't necessary as
				// JNIEnv.FindClass() will invoke the static constructor for the type,
				// which will (indirectly) call TypeManager.RegisterType().
				jni = JavaNativeTypeManager.ToJniNameFromAttributes (type);
				if (jni != null) {
					e.Dispose ();
					return FindClass (JavaNativeTypeManager.ToJniName (jni, rank));
				}
				throw;
			}
		}

		public static IntPtr FindClass (string classname)
		{
			JniObjectReference local_ref = JniEnvironment.Types.FindClass (classname);
			IntPtr global_ref = NewGlobalRef (local_ref.Handle);
			JniObjectReference.Dispose (ref local_ref);
			return global_ref;
		}

		public static IntPtr FindClass (string className, ref IntPtr cachedJniClassHandle)
		{
			if (cachedJniClassHandle != IntPtr.Zero)
				return cachedJniClassHandle;
			IntPtr h = FindClass (className);
			if (Interlocked.CompareExchange (ref cachedJniClassHandle, h, IntPtr.Zero) != IntPtr.Zero)
				DeleteGlobalRef (h);
			return cachedJniClassHandle;
		}

		public static void Throw (IntPtr obj)
		{
			if (obj == IntPtr.Zero)
				throw new ArgumentException ("'obj' must not be IntPtr.Zero.", "obj");

			JniEnvironment.Exceptions.Throw (new JniObjectReference (obj));
		}

		public static void ThrowNew (IntPtr clazz, string message)
		{
			if (message == null)
				throw new ArgumentNullException ("message");

			JniEnvironment.Exceptions.ThrowNew (new JniObjectReference (clazz), message);
		}

		public static void PushLocalFrame (int capacity)
		{
			JniEnvironment.References.PushLocalFrame (capacity);
		}

		public static void EnsureLocalCapacity (int capacity)
		{
			JniEnvironment.References.EnsureLocalCapacity (capacity);
		}

		internal static void DeleteRef (IntPtr handle, JniHandleOwnership transfer)
		{
			switch (transfer) {
			case JniHandleOwnership.DoNotTransfer:
				break;
			case JniHandleOwnership.TransferLocalRef:
				JNIEnv.DeleteLocalRef (handle);
				break;
			case JniHandleOwnership.TransferGlobalRef:
				JNIEnv.DeleteGlobalRef (handle);
				break;
			}
		}

		public static IntPtr NewGlobalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject);
			return r.NewGlobalRef ().Handle;
		}

		public static void DeleteGlobalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject, JniObjectReferenceType.Global);
			JniObjectReference.Dispose (ref r);
		}

		public static IntPtr NewLocalRef (IntPtr jobject)
		{
			return new JniObjectReference (jobject).NewLocalRef ().Handle;
		}

		public static void DeleteLocalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject, JniObjectReferenceType.Local);
			JniObjectReference.Dispose (ref r);
		}

		public static void DeleteWeakGlobalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject, JniObjectReferenceType.WeakGlobal);
			JniObjectReference.Dispose (ref r);
		}

		public static IntPtr NewObject (IntPtr jclass, IntPtr jmethod)
		{
			var r = JniEnvironment.Object.NewObject (new JniObjectReference (jclass), new JniMethodInfo (jmethod, isStatic: false));
			return r.Handle;
		}

		public static unsafe IntPtr NewObject (IntPtr jclass, IntPtr jmethod, JValue* parms)
		{
			var r = JniEnvironment.Object.NewObject (new JniObjectReference (jclass), new JniMethodInfo (jmethod, isStatic: false), (JniArgumentValue*) parms);
			return r.Handle;
		}

		public static unsafe IntPtr NewObject (IntPtr jclass, IntPtr jmethod, params JValue[] parms)
		{
			fixed (JValue* p = parms)
				return NewObject (jclass, jmethod, p);
		}

		public static string GetClassNameFromInstance (IntPtr jobject)
		{
			return JniEnvironment.Types.GetJniTypeNameFromInstance (new JniObjectReference (jobject));
		}

		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		static extern unsafe IntPtr monodroid_typemap_managed_to_java (Type type, byte* mvid);

		internal static void LogTypemapTrace (StackTrace st)
		{
			string? trace = st.ToString ()?.Trim ();
			if (String.IsNullOrEmpty (trace))
				return;

			RuntimeNativeMethods.monodroid_log (LogLevel.Warn, LogCategories.Assembly, "typemap: called from");
			foreach (string line in trace!.Split ('\n')) {
				RuntimeNativeMethods.monodroid_log (LogLevel.Warn, LogCategories.Assembly, line);
			}
		}

		// We need this proxy method because if `TypeManagedToJava` contained the call to `monodroid_typemap_managed_to_java`
		// (which is an icall, or ecall in CoreCLR parlance), CoreCLR JIT would throw an exception, refusing to compile the
		// method.  The exception would be thrown even if the icall weren't called (e.g. hidden behind a runtime type check)
		static unsafe IntPtr monovm_typemap_managed_to_java (Type type, byte* mvidptr)
		{
			return monodroid_typemap_managed_to_java (type, mvidptr);
		}

		internal static unsafe string? TypemapManagedToJava (Type type)
		{
			if (mvid_bytes == null)
				mvid_bytes = new byte[16];

			var mvid = new Span<byte>(mvid_bytes);
			byte[]? mvid_data = null;
			if (!type.Module.ModuleVersionId.TryWriteBytes (mvid)) {
				RuntimeNativeMethods.monodroid_log (LogLevel.Warn, LogCategories.Default, $"Failed to obtain module MVID using the fast method, falling back to the slow one");
				mvid_data = type.Module.ModuleVersionId.ToByteArray ();
			} else {
				mvid_data = mvid_bytes;
			}

			IntPtr ret;
			fixed (byte* mvidptr = mvid_data) {
				if (RuntimeFeature.IsMonoRuntime) {
					ret = monovm_typemap_managed_to_java (type, mvidptr);
				} else if (RuntimeFeature.IsCoreClrRuntime) {
					ret = RuntimeNativeMethods.clr_typemap_managed_to_java (type.FullName, (IntPtr)mvidptr);
				} else {
					throw new NotSupportedException ("Internal error: unknown runtime not supported");
				}
			}

			if (ret == IntPtr.Zero) {
				if (Logger.LogAssembly) {
					RuntimeNativeMethods.monodroid_log (LogLevel.Warn, LogCategories.Default, $"typemap: failed to map managed type to Java type: {type.AssemblyQualifiedName} (Module ID: {type.Module.ModuleVersionId}; Type token: {type.MetadataToken})");
					LogTypemapTrace (new StackTrace (true));
				}

				return null;
			}

			return Marshal.PtrToStringAnsi (ret);
		}

		public static string GetJniName (Type type)
		{
			if (type == null)
				throw new ArgumentNullException ("type");

			JniTypeSignature sig = JNIEnvInit.androidRuntime?.TypeManager.GetTypeSignature (type) ?? default;

			return sig.SimpleReference == null
				? JavaNativeTypeManager.ToJniName (type)
				: sig.Name;
		}

		public static IntPtr ToJniHandle (IJavaObject? value)
		{
			if (value == null)
				return IntPtr.Zero;
			return value.Handle;
		}

		public static IntPtr ToLocalJniHandle (IJavaObject? value)
		{
			if (value == null)
				return IntPtr.Zero;
			var ex = value as IJavaObjectEx;
			if (ex != null)
				return ex.ToLocalJniHandle ();
			return NewLocalRef (value.Handle);
		}

		public static string? GetCharSequence (IntPtr jobject, JniHandleOwnership transfer)
		{
			if (jobject == IntPtr.Zero)
				return null;

			var r = JniEnvironment.Object.ToString (new JniObjectReference (jobject));
			return JniEnvironment.Strings.ToString (ref r, JniObjectReferenceOptions.CopyAndDispose);
		}

		public static unsafe string? GetString (IntPtr value, JniHandleOwnership transfer)
		{
			if (value == IntPtr.Zero)
				return null;

			var s = JniEnvironment.Strings.ToString (new JniObjectReference (value));
			DeleteRef (value, transfer);
			return s;
		}

		public static unsafe IntPtr NewString (string? text)
		{
			if (text == null)
				return IntPtr.Zero;

			return JniEnvironment.Strings.NewString (text).Handle;
		}

		public static unsafe IntPtr NewString (char[]? text, int length)
		{
			if (text == null)
				return IntPtr.Zero;

			fixed (char *s = text)
				return JniEnvironment.Strings.NewString (s, length).Handle;
		}

		static void AssertCompatibleArrayTypes (Type sourceType, IntPtr destArray)
		{
			IntPtr grefSource = FindClass (sourceType);
			IntPtr lrefDest   = GetObjectClass (destArray);
			try {
				if (!IsAssignableFrom (grefSource, lrefDest)) {
					throw new InvalidCastException (FormattableString.Invariant (
						$"Unable to cast from '{JniEnvironment.Types.GetJniTypeNameFromClass (new JniObjectReference (grefSource))}' to '{JniEnvironment.Types.GetJniTypeNameFromClass (new JniObjectReference (lrefDest))}'."));
				}
			} finally {
				DeleteGlobalRef (grefSource);
				DeleteLocalRef (lrefDest);
			}
		}

		static void AssertCompatibleArrayTypes (IntPtr sourceArray, Type destType)
		{
			IntPtr grefDest   = FindClass (destType);
			IntPtr lrefSource = GetObjectClass (sourceArray);
			try {
				if (!IsAssignableFrom (lrefSource, grefDest)) {
					throw new InvalidCastException (FormattableString.Invariant (
						$"Unable to cast from '{JniEnvironment.Types.GetJniTypeNameFromClass (new JniObjectReference (lrefSource))}' to '{JniEnvironment.Types.GetJniTypeNameFromClass (new JniObjectReference (grefDest))}'."));
				}
			} finally {
				DeleteGlobalRef (grefDest);
				DeleteLocalRef (lrefSource);
			}
		}

		public static void CopyArray (IntPtr src, bool[] dest)
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			AssertCompatibleArrayTypes (src, typeof (bool[]));

			_GetBooleanArrayRegion (src, 0, dest.Length, dest);
		}

		public static void CopyArray (IntPtr src, string[] dest)
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			for (int i = 0; i < dest.Length; i++)
				dest [i] = GetString (GetObjectArrayElement (src, i), JniHandleOwnership.TransferLocalRef)!;
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, object?>>? nativeArrayElementToManaged;
		static Dictionary<Type, Func<Type?, IntPtr, int, object?>> NativeArrayElementToManaged {
			get {
				if (nativeArrayElementToManaged != null)
					return nativeArrayElementToManaged;

				var newValue = CreateNativeArrayElementToManaged ();
				Interlocked.CompareExchange (ref nativeArrayElementToManaged, newValue, null);
				return nativeArrayElementToManaged;
			}
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, object?>> CreateNativeArrayElementToManaged ()
		{
			return new Dictionary<Type, Func<Type?, IntPtr, int, object?>> () {
				{ typeof (bool), (type, source, index) => {
					var r = new bool [1];
					_GetBooleanArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (byte), (type, source, index) => {
					var r = new byte [1];
					_GetByteArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (char), (type, source, index) => {
					var r = new char [1];
					_GetCharArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (short), (type, source, index) => {
					var r = new short [1];
					_GetShortArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (int), (type, source, index) => {
					var r = new int [1];
					_GetIntArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (long), (type, source, index) => {
					var r = new long [1];
					_GetLongArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (float), (type, source, index) => {
					var r = new float [1];
					_GetFloatArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (double), (type, source, index) => {
					var r = new double [1];
					_GetDoubleArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (string), (type, source, index) => {
					IntPtr elem = GetObjectArrayElement (source, index);
					if (type == typeof (Java.Lang.String))
						return new Java.Lang.String (elem, JniHandleOwnership.TransferLocalRef);
					return GetString (elem, JniHandleOwnership.TransferLocalRef);
				} },
				{ typeof (IJavaObject), (type, source, index) => {
					AssertIsJavaObject (type);

					IntPtr elem = GetObjectArrayElement (source, index);
					return GetObject (elem, type);

					// FIXME: Since a Dictionary<Type, Func> is used here, the trimmer will not be able to properly analyze `Type t`
					// error IL2111: Method 'lambda expression' with parameters or return value with `DynamicallyAccessedMembersAttribute` is accessed via reflection. Trimmer can't guarantee availability of the requirements of the method.
					[UnconditionalSuppressMessage ("Trimming", "IL2067", Justification = "FIXME: https://github.com/xamarin/xamarin-android/issues/8724")]
					static object? GetObject (IntPtr e, Type t) =>
						Java.Lang.Object.GetObject (e, JniHandleOwnership.TransferLocalRef, t);
				} },
				{ typeof (Array), (type, source, index) => {
					IntPtr  elem      = GetObjectArrayElement (source, index);
					return GetArray (elem, JniHandleOwnership.TransferLocalRef, type);
				} },
			};
		}

		static TValue GetConverter<TValue>(Dictionary<Type, TValue> dict, Type? elementType, IntPtr array)
		{
			TValue? converter;

			if (elementType != null) {
				if (elementType.IsEnum)
					elementType = Enum.GetUnderlyingType (elementType);
				if (dict.TryGetValue (elementType, out converter))
					return converter;
			}

			if (array != IntPtr.Zero) {
				string type = GetClassNameFromInstance (array);
				if (type == null || type.Length < 1 || type [0] != '[')
					throw new InvalidOperationException ("Unsupported java array type: " + type);

				switch (type [1]) {
				case 'B': return dict [typeof (byte)];
				case 'C': return dict [typeof (char)];
				case 'D': return dict [typeof (double)];
				case 'F': return dict [typeof (float)];
				case 'I': return dict [typeof (int)];
				case 'J': return dict [typeof (long)];
				case 'S': return dict [typeof (short)];
				case 'Z': return dict [typeof (bool)];
				case '[':
					if (elementType == null || elementType.IsArray)
						return dict [typeof (Array)];
					break;
				}

				if (type == "[Ljava/lang/String;")
					return dict [typeof (string)];
			}

			if (elementType != null && elementType.IsArray)
				return dict [typeof (Array)];

			AssertIsJavaObject (elementType);
			return dict [typeof (IJavaObject)];
		}

		static unsafe void _GetBooleanArrayRegion (IntPtr array, int start, int length, bool[] buffer)
		{
			fixed (bool* p = buffer)
				JniEnvironment.Arrays.GetBooleanArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetByteArrayRegion (IntPtr array, int start, int length, byte[] buffer)
		{
			fixed (byte* p = buffer)
				JniEnvironment.Arrays.GetByteArrayRegion (new JniObjectReference (array), start, length, (sbyte*) p);
		}

		static unsafe void _GetCharArrayRegion (IntPtr array, int start, int length, char[] buffer)
		{
			fixed (char* p = buffer)
				JniEnvironment.Arrays.GetCharArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetShortArrayRegion (IntPtr array, int start, int length, short[] buffer)
		{
			fixed (short* p = buffer)
				JniEnvironment.Arrays.GetShortArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetIntArrayRegion (IntPtr array, int start, int length, int[] buffer)
		{
			fixed (int* p = buffer)
				JniEnvironment.Arrays.GetIntArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetLongArrayRegion (IntPtr array, int start, int length, long[] buffer)
		{
			fixed (long* p = buffer)
				JniEnvironment.Arrays.GetLongArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetFloatArrayRegion (IntPtr array, int start, int length, float[] buffer)
		{
			fixed (float* p = buffer)
				JniEnvironment.Arrays.GetFloatArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetDoubleArrayRegion (IntPtr array, int start, int length, double[] buffer)
		{
			fixed (double* p = buffer)
				JniEnvironment.Arrays.GetDoubleArrayRegion (new JniObjectReference (array), start, length, p);
		}

#pragma warning disable RS0027 // API with optional parameter(s) should have the most parameters amongst its public overloads
		public static void CopyArray (IntPtr src, Array dest, Type? elementType = null)
#pragma warning restore RS0027 // API with optional parameter(s) should have the most parameters amongst its public overloads
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			if (elementType != null && elementType.IsValueType)
				AssertCompatibleArrayTypes (src, MakeArrayType (elementType));

			if (elementType != null && elementType.IsArray) {
				for (int i = 0; i < dest.Length; ++i) {
					IntPtr a = GetObjectArrayElement (src, i);
					try {
						var d = (Array?) dest.GetValue (i);
						if (d == null)
							dest.SetValue (GetArray (a, JniHandleOwnership.DoNotTransfer, elementType.GetElementType ()), i);
						else
							CopyArray (a, d, elementType.GetElementType ());
					} finally {
						DeleteLocalRef (a);
					}
				}
				return;
			}

			var converter = GetConverter (NativeArrayElementToManaged, elementType, src);

			for (int i = 0; i < dest.Length; i++)
				dest.SetValue (converter (elementType, src, i), i);
		}

		static void AssertIsJavaObject (Type? targetType)
		{
			if (targetType != null && !typeof (IJavaObject).IsAssignableFrom (targetType))
				throw new NotSupportedException ("Don't know how to convert type '" + targetType.FullName + "' to an Android.Runtime.IJavaObject.");
		}

		public static void CopyArray<T> (IntPtr src, T[] dest)
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			if (typeof (T).IsValueType)
				AssertCompatibleArrayTypes (src, typeof (T[]));

			if (typeof (T).IsArray) {
				CopyArray (src, dest, typeof (T));
				return;
			}

			var converter = GetConverter (NativeArrayElementToManaged, typeof (T), src);

			for (int i = 0; i < dest.Length; i++)
				dest [i] = (T) converter (typeof (T), src, i)!;
		}

		public static unsafe void CopyArray (bool[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			AssertCompatibleArrayTypes (typeof (bool[]), dest);

			fixed (bool* p = src)
				JniEnvironment.Arrays.SetBooleanArrayRegion (new JniObjectReference (dest), 0, src.Length, p);
		}

		public static void CopyArray (string[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			for (int i = 0; i < src.Length; i++) {
				IntPtr native = NewString (src [i]);
				JniEnvironment.Arrays.SetObjectArrayElement (new JniObjectReference (dest), i, new JniObjectReference (native));
				DeleteLocalRef (native);
			}
		}

		public static void CopyArray (IJavaObject[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			for (int i = 0; i < src.Length; i++) {
				IJavaObject o = src [i];
				JniEnvironment.Arrays.SetObjectArrayElement (new JniObjectReference (dest), i, new JniObjectReference (o == null ? IntPtr.Zero : o.Handle));
			}
		}

		static Dictionary<Type, Action<Array, IntPtr>>? copyManagedToNativeArray;
		static Dictionary<Type, Action<Array, IntPtr>> CopyManagedToNativeArray {
			get {
				if (copyManagedToNativeArray != null)
					return copyManagedToNativeArray;

				var newValue = CreateCopyManagedToNativeArray ();
				Interlocked.CompareExchange (ref copyManagedToNativeArray, newValue, null);
				return copyManagedToNativeArray;
			}
		}

		static Dictionary<Type, Action<Array, IntPtr>> CreateCopyManagedToNativeArray ()
		{
			return new Dictionary<Type, Action<Array, IntPtr>> () {
				{ typeof (bool),        (source, dest) => CopyArray ((bool[]) source, dest) },
				{ typeof (byte),        (source, dest) => CopyArray ((byte[]) source, dest) },
				{ typeof (char),        (source, dest) => CopyArray ((char[]) source, dest) },
				{ typeof (short),       (source, dest) => CopyArray ((short[]) source, dest) },
				{ typeof (int),         (source, dest) => CopyArray ((int[]) source, dest) },
				{ typeof (long),        (source, dest) => CopyArray ((long[]) source, dest) },
				{ typeof (float),       (source, dest) => CopyArray ((float[]) source, dest) },
				{ typeof (double),      (source, dest) => CopyArray ((double[]) source, dest) },
				{ typeof (string),      (source, dest) => {
					var s = source as string[];
					if (s != null) {
						CopyArray (s, dest);
						return;
					}
					var ijo = source as IJavaObject[];
					if (ijo != null) {
						CopyArray (ijo, dest);
						return;
					}
					throw new NotSupportedException ("Don't know how to copy '" +
							source.GetType ().FullName + "' to '" +
							GetClassNameFromInstance (dest) +
							"'.");
				} },
				{ typeof (IJavaObject), (source, dest) => CopyArray ((IJavaObject[]) source, dest) },
				{ typeof (Array),       (source, dest) => {
					int len = source.GetLength (0);
					for (int i = 0; i < len; ++i) {
						IntPtr _dest    = GetObjectArrayElement (dest, i);
						Array  _source  = (Array) source.GetValue (i)!;
						CopyArray (_source, _source.GetType ().GetElementType ()!, _dest);
						JNIEnv.DeleteLocalRef (_dest);
					}
				} },
			};
		}

		public static void CopyArray (Array source, Type elementType, IntPtr dest)
		{
			if (source == null)
				throw new ArgumentNullException ("source");
			if (elementType == null)
				throw new ArgumentNullException ("elementType");

			if (elementType.IsValueType)
				AssertCompatibleArrayTypes (MakeArrayType (elementType), dest);

			Action<Array, IntPtr> converter = GetConverter (CopyManagedToNativeArray, elementType, dest);

			converter (source, dest);
		}

		public static void CopyArray<T> (T[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			CopyArray (src, typeof (T), dest);
		}

		public static Array? GetArray (IntPtr array_ptr, JniHandleOwnership transfer, Type? element_type = null)
		{
			try {
				return _GetArray (array_ptr, element_type);
			}
			finally {
				DeleteRef (array_ptr, transfer);
			}
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, Array>>? nativeArrayToManaged;
		static Dictionary<Type, Func<Type?, IntPtr, int, Array>> NativeArrayToManaged {
			get {
				if (nativeArrayToManaged != null)
					return nativeArrayToManaged;

				var newValue = CreateNativeArrayToManaged ();
				Interlocked.CompareExchange (ref nativeArrayToManaged, newValue, null);
				return nativeArrayToManaged;
			}
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, Array>> CreateNativeArrayToManaged ()
		{
			return new Dictionary<Type, Func<Type?, IntPtr, int, Array>> () {
				{ typeof (bool), (type, source, len) => {
					var r = new bool [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (byte), (type, source, len) => {
					var r = new byte[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (char), (type, source, len) => {
					var r = new char [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (short), (type, source, len) => {
					var r = new short [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (ushort), (type, source, len) => {
					var r = new ushort [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (int), (type, source, len) => {
					var r = new int[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (uint), (type, source, len) => {
					var r = new uint[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (long), (type, source, len) => {
					var r = new long[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (ulong), (type, source, len) => {
					var r = new ulong[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (float), (type, source, len) => {
					var r = new float[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (double), (type, source, len) => {
					var r = new double [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (string), (type, source, len) => {
					if (type != null && typeof (Java.Lang.Object).IsAssignableFrom (type)) {
						var r = new Java.Lang.String [len];
						CopyArray (source, r);
						return r;
					} else {
						var r = new string [len];
						CopyArray (source, r);
						return r;
					}
				} },
				{ typeof (IJavaObject), (type, source, len) => {
					var r = ArrayCreateInstance (type!, len);
					CopyArray (source, r, type);
					return r;
				} },
				{ typeof (Array), (type, source, len) => {
					var r = ArrayCreateInstance (type!, len);
					CopyArray (source, r, type);
					return r;
				} },
			};
		}

		static Array? _GetArray (IntPtr array_ptr, Type? element_type)
		{
			if (array_ptr == IntPtr.Zero)
				return null;

			if (element_type != null && element_type.IsValueType)
				AssertCompatibleArrayTypes (array_ptr, MakeArrayType (element_type));

			int cnt = _GetArrayLength (array_ptr);

			var converter = GetConverter (NativeArrayToManaged, element_type, array_ptr);

			return converter (element_type, array_ptr, cnt);
		}

		static int _GetArrayLength (IntPtr array_ptr)
		{
			return JniEnvironment.Arrays.GetArrayLength (new JniObjectReference (array_ptr));
		}

		public static object?[]? GetObjectArray (IntPtr array_ptr, Type[] element_types)
		{
			if (array_ptr == IntPtr.Zero)
				return null;

			int cnt = _GetArrayLength (array_ptr);
			if (cnt == 0)
				return Array.Empty<object> ();

			var converter = GetConverter (NativeArrayElementToManaged, null, array_ptr);

			object?[] ret = new object [cnt];

			for (int i = 0; i < cnt; i++) {
				Type? targetType	= (element_types != null && i < element_types.Length) ? element_types [i] : null;
				object? value    = converter ((targetType == null || targetType.IsValueType) ? null : targetType,
						array_ptr, i);

				ret [i] = value;
				ret [i] = targetType == null || targetType.IsInstanceOfType (value)
					? value
					: Convert.ChangeType (value, targetType, CultureInfo.InvariantCulture);
			}

			return ret;
		}

		public static T[]? GetArray<T> (IntPtr array_ptr)
		{
			if (array_ptr == IntPtr.Zero)
				return null;

			if (typeof (T).IsValueType)
				AssertCompatibleArrayTypes (array_ptr, typeof (T[]));

			int cnt = _GetArrayLength (array_ptr);
			T[] ret = new T [cnt];
			CopyArray<T> (array_ptr, ret);
			return ret;
		}

		public static T[]? GetArray<
				[DynamicallyAccessedMembers (Constructors)]
				T
		> (Java.Lang.Object[] array)
		{
			if (array == null)
				return null;
			T[] ret = new T [array.Length];
			for (int i = 0; i < array.Length; i++)
				ret [i] = JavaConvert.FromJavaObject<T> (array [i])!;
			return ret;
		}

		public static T GetArrayItem<T> (IntPtr array_ptr, int index)
		{
			if (array_ptr == IntPtr.Zero)
				throw new ArgumentException ("array_ptr");
			if (index < 0 || index >= GetArrayLength (array_ptr))
				throw new ArgumentOutOfRangeException ("index");

			var converter = GetConverter (NativeArrayElementToManaged, typeof (T), array_ptr);

			return (T) converter (typeof (T), array_ptr, index)!;
		}

		public static int GetArrayLength (IntPtr array_ptr)
		{
			if (array_ptr == IntPtr.Zero)
				return 0;
			return _GetArrayLength (array_ptr);
		}

		public static unsafe IntPtr NewArray (bool[]? array)
		{
			if (array == null)
				return IntPtr.Zero;
			IntPtr result;

			var r   = JniEnvironment.Arrays.NewBooleanArray (array.Length);
			fixed (bool* p = array)
				JniEnvironment.Arrays.SetBooleanArrayRegion (r, 0, array.Length, p);
			result  = r.Handle;

			return result;
		}

		public static IntPtr NewArray (string[]? array)
		{
			if (array == null)
				return IntPtr.Zero;

			IntPtr result = NewObjectArray (array.Length, Java.Lang.Class.String, IntPtr.Zero);
			CopyArray (array, result);

			return result;
		}

		// We do these translations here instead of in the binding to force a compile
		// time error if using an older Java.Interop.dll instead of a runtime error
		public static IntPtr NewArray (uint[] array) => NewArray ((int[])(object)array);

		public static IntPtr NewArray (ushort[] array) => NewArray ((short[])(object)array);

		public static IntPtr NewArray (ulong[] array) => NewArray ((long[])(object)array);

		public static IntPtr NewObjectArray (int length, IntPtr elementClass)
		{
			return NewObjectArray (length, elementClass, IntPtr.Zero);
		}

		public static IntPtr NewObjectArray (int length, IntPtr elementClass, IntPtr initialElement)
		{
			return JniEnvironment.Arrays.NewObjectArray (length, new JniObjectReference (elementClass), new JniObjectReference (initialElement)).Handle;
		}

		public static IntPtr NewObjectArray<T>(params T[]? values)
		{
			if (values == null)
				return IntPtr.Zero;

			IntPtr grefArrayElementClass = GetArrayElementClass (values);
			if (JniEnvironment.Types.GetJniTypeNameFromClass (new JniObjectReference (grefArrayElementClass)) == "mono/android/runtime/JavaObject") {
				DeleteGlobalRef (grefArrayElementClass);
				grefArrayElementClass = NewGlobalRef (Java.Lang.Class.Object);
			}
			try {
				IntPtr lrefArray = NewObjectArray (values.Length, grefArrayElementClass, IntPtr.Zero);

				for (int i = 0; i < values.Length; ++i) {
					JavaConvert.WithLocalJniHandle (values [i], lref => {
							SetObjectArrayElement (lrefArray, i, lref);
							return IntPtr.Zero;
					});
				}

				return lrefArray;
			}
			finally {
				DeleteGlobalRef (grefArrayElementClass);
			}

		}

		static IntPtr GetArrayElementClass<T>(T[] values)
		{
			Type    elementType = typeof (T);
			var jniClass    = JavaConvert.GetJniClassForType (elementType);
			if (jniClass != null) {
				return FindClass (jniClass);
			}

			if (elementType.IsValueType)
				return NewGlobalRef (Java.Lang.Class.Object);

			return FindClass (elementType);
		}

		public static void CopyObjectArray<
				[DynamicallyAccessedMembers (Constructors)]
				T
		>(IntPtr source, T[] destination)
		{
			if (source == IntPtr.Zero)
				return;
			if (destination == null)
				throw new ArgumentNullException ("destination");

			int len = Math.Min (GetArrayLength (source), destination.Length);
			for (int i = 0; i < len; ++i) {
				IntPtr value = GetObjectArrayElement (source, i);
				destination [i] = JavaConvert.FromJniHandle<T>(value, JniHandleOwnership.TransferLocalRef)!;
			}
		}

		public static void CopyObjectArray<T>(T[]? source, IntPtr destination)
		{
			if (source == null)
				return;
			if (destination == IntPtr.Zero)
				throw new ArgumentException ("Destination is a null JNI handle!", "destination");

			int len = Math.Min (source.Length, GetArrayLength (destination));
			for (int i = 0; i < len; ++i) {
				JavaConvert.WithLocalJniHandle (source [i], lref => {
						SetObjectArrayElement (destination, i, lref);
						return IntPtr.Zero;
				});
			}
		}

		public static IntPtr NewArray (IJavaObject[]? array)
		{
			if (array == null)
				return IntPtr.Zero;

			IntPtr result;
			IntPtr grefClass = FindClass (array.GetType ().GetElementType ()!);
			try {
				result = NewObjectArray (array.Length, grefClass, IntPtr.Zero);
			} finally {
				DeleteGlobalRef (grefClass);
			}

			CopyArray (array, result);

			return result;
		}

		static Dictionary<Type, Func<Array, IntPtr>>? createManagedToNativeArray;
		static Dictionary<Type, Func<Array, IntPtr>> CreateManagedToNativeArray {
			get {
				if (createManagedToNativeArray != null)
					return createManagedToNativeArray;

				var newValue = CreateCreateManagedToNativeArray ();
				Interlocked.CompareExchange (ref createManagedToNativeArray, newValue, null);
				return createManagedToNativeArray;
			}
		}

		static Dictionary<Type, Func<Array, IntPtr>> CreateCreateManagedToNativeArray ()
		{
			return new Dictionary<Type, Func<Array, IntPtr>> () {
				{ typeof (bool),          (source) => NewArray ((bool[]) source) },
				{ typeof (byte),          (source) => NewArray ((byte[]) source) },
				{ typeof (char),          (source) => NewArray ((char[]) source) },
				{ typeof (short),         (source) => NewArray ((short[]) source) },
				{ typeof (int),           (source) => NewArray ((int[]) source) },
				{ typeof (long),          (source) => NewArray ((long[]) source) },
				{ typeof (float),         (source) => NewArray ((float[]) source) },
				{ typeof (double),        (source) => NewArray ((double[]) source) },
				{ typeof (string),        (source) => NewArray ((string[]) source) },
				{ typeof (IJavaObject),   (source) => NewArray ((IJavaObject[]) source) },
				{ typeof (Array),         (source) => NewArray (source) },
			};
		}

		public static IntPtr NewArray (Array value, Type? elementType = null)
		{
			if (value == null)
				throw new ArgumentNullException ("value");

			elementType ??= value.GetType ().GetElementType ()!;

			if (elementType.IsArray) {
				IntPtr grefArrayElementClass = FindClass (elementType);
				try {
					return NewArray (value, elementType, grefArrayElementClass);
				} finally {
					DeleteGlobalRef (grefArrayElementClass);
				}
			}

			Func<Array, IntPtr> creator = GetConverter (CreateManagedToNativeArray, elementType, IntPtr.Zero);

			return creator (value);
		}

		public static IntPtr NewArray<T> (T[]? array)
		{
			if (array == null)
				return IntPtr.Zero;

			if (typeof (T).IsArray) {
				IntPtr grefArrayElementClass = FindClass (typeof (T));
				try {
					return NewArray (array, typeof (T), grefArrayElementClass);
				} finally {
					DeleteGlobalRef (grefArrayElementClass);
				}
			}

			Func<Array, IntPtr> creator = GetConverter (CreateManagedToNativeArray, typeof (T), IntPtr.Zero);

			return creator (array);
		}

		static IntPtr NewArray (Array value, Type elementType, IntPtr elementClass)
		{
			if (value == null)
				throw new ArgumentNullException ("value");

			IntPtr array = IntPtr.Zero;
			try {
				array = NewObjectArray (value.Length, elementClass, IntPtr.Zero);

				for (int i = 0; i < value.Length; ++i) {
					IntPtr subarray = NewArray ((Array) value.GetValue (i)!, elementType.GetElementType ());
					SetObjectArrayElement (array, i, subarray);
					DeleteLocalRef (subarray);
				}

				return array;
			} catch {
				DeleteLocalRef (array);
				throw;
			}
		}

		static Dictionary<Type, Action<IntPtr, int, object?>>? setNativeArrayElement;
		static Dictionary<Type, Action<IntPtr, int, object?>> SetNativeArrayElement {
			get {
				if (setNativeArrayElement != null)
					return setNativeArrayElement;

				var newValue = CreateSetNativeArrayElement ();
				Interlocked.CompareExchange (ref setNativeArrayElement, newValue, null);
				return setNativeArrayElement;
			}
		}

		static Dictionary<Type, Action<IntPtr, int, object?>> CreateSetNativeArrayElement ()
		{
			return new Dictionary<Type, Action<IntPtr, int, object?>> () {
				{ typeof (bool), (dest, index, value) => {
					var _value = new[]{(bool) value!};
					_SetBooleanArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (byte), (dest, index, value) => {
					var _value = new[]{(byte) value!};
					_SetByteArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (char), (dest, index, value) => {
					var _value = new[]{(char) value!};
					_SetCharArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (short), (dest, index, value) => {
					var _value = new[]{(short) value!};
					_SetShortArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (int), (dest, index, value) => {
					var _value = new[]{(int) value!};
					_SetIntArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (long), (dest, index, value) => {
					var _value = new[]{(long) value!};
					_SetLongArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (float), (dest, index, value) => {
					var _value = new[]{(float) value!};
					_SetFloatArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (double), (dest, index, value) => {
					var _value = new[]{(double) value!};
					_SetDoubleArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (string), (dest, index, value) => {
					IntPtr s = NewString (value!.ToString ());
					try {
						SetObjectArrayElement (dest, index, s);
					} finally {
						DeleteLocalRef (s);
					}
				} },
				{ typeof (IJavaObject), (dest, index, value) => {
					SetObjectArrayElement (dest, index, value == null ? IntPtr.Zero : ((IJavaObject) value).Handle);
				} },
				{ typeof (Array), (dest, index, value) => {
					IntPtr _v = NewArray ((Array) value!);
					SetObjectArrayElement (dest, index, _v);
					JNIEnv.DeleteLocalRef (_v);
				} },
			};
		}

		static unsafe void _SetBooleanArrayRegion (IntPtr array, int start, int length, bool[] buffer)
		{
			fixed (bool* p = buffer)
				JniEnvironment.Arrays.SetBooleanArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetByteArrayRegion (IntPtr array, int start, int length, byte[] buffer)
		{
			fixed (byte* p = buffer)
				JniEnvironment.Arrays.SetByteArrayRegion (new JniObjectReference (array), start, length, (sbyte*) p);
		}

		static unsafe void _SetCharArrayRegion (IntPtr array, int start, int length, char[] buffer)
		{
			fixed (char* p = buffer)
				JniEnvironment.Arrays.SetCharArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetShortArrayRegion (IntPtr array, int start, int length, short[] buffer)
		{
			fixed (short* p = buffer)
				JniEnvironment.Arrays.SetShortArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetIntArrayRegion (IntPtr array, int start, int length, int[] buffer)
		{
			fixed (int* p = buffer)
				JniEnvironment.Arrays.SetIntArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetLongArrayRegion (IntPtr array, int start, int length, long[] buffer)
		{
			fixed (long* p = buffer)
				JniEnvironment.Arrays.SetLongArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetFloatArrayRegion (IntPtr array, int start, int length, float[] buffer)
		{
			fixed (float* p = buffer)
				JniEnvironment.Arrays.SetFloatArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetDoubleArrayRegion (IntPtr array, int start, int length, double[] buffer)
		{
			fixed (double* p = buffer)
				JniEnvironment.Arrays.SetDoubleArrayRegion (new JniObjectReference (array), start, length, p);
		}

		public static void SetArrayItem<T> (IntPtr array_ptr, int index, T value)
		{
			if (array_ptr == IntPtr.Zero)
				throw new ArgumentException ("array_ptr");
			if (index < 0 || index >= GetArrayLength (array_ptr))
				throw new ArgumentOutOfRangeException ("index");

			var setter = GetConverter (SetNativeArrayElement, typeof (T), array_ptr);

			setter (array_ptr, index, value);
		}

		[return: NotNullIfNotNull (parameterName: "array")]
		public static Java.Lang.Object[]? ToObjectArray<T> (T[]? array)
		{
			if (array == null)
				return null;
			Java.Lang.Object[] ret = new Java.Lang.Object [array.Length];
			for (int i = 0; i < array.Length; i++)
				ret [i] = JavaObjectExtensions.JavaCast<Java.Lang.Object>(JavaConvert.ToJavaObject (array [i]))!;
			return ret;
		}

		public static unsafe void CopyArray (IntPtr src, uint[] dest)
		{
			if (src == IntPtr.Zero)
				return;
			fixed (uint* __p = dest)
				JniEnvironment.Arrays.GetIntArrayRegion (new JniObjectReference (src), 0, dest.Length, (int*) __p);
		}

		public static unsafe void CopyArray (IntPtr src, ushort[] dest)
		{
			if (src == IntPtr.Zero)
				return;
			fixed (ushort* __p = dest)
				JniEnvironment.Arrays.GetShortArrayRegion (new JniObjectReference (src), 0, dest.Length, (short*) __p);
		}

		public static unsafe void CopyArray (IntPtr src, ulong[] dest)
		{
			if (src == IntPtr.Zero)
				return;
			fixed (ulong* __p = dest)
				JniEnvironment.Arrays.GetLongArrayRegion (new JniObjectReference (src), 0, dest.Length, (long*) __p);
		}
#if ANDROID_8
		[DllImport ("libjnigraphics.so")]
		static extern int AndroidBitmap_getInfo (IntPtr env, IntPtr jbitmap, out Android.Graphics.AndroidBitmapInfo info);

		[DllImport ("libjnigraphics.so")]
		static extern int AndroidBitmap_lockPixels (IntPtr env, IntPtr jbitmap, out IntPtr addrPtr);

		[DllImport ("libjnigraphics.so")]
		static extern int AndroidBitmap_unlockPixels(IntPtr env, IntPtr jbitmap);

		internal static int AndroidBitmap_getInfo (IntPtr jbitmap, out Android.Graphics.AndroidBitmapInfo info)
		{
			return AndroidBitmap_getInfo (Handle, jbitmap, out info);
		}

		internal static int AndroidBitmap_lockPixels (IntPtr jbitmap, out IntPtr addrPtr)
		{
			return AndroidBitmap_lockPixels (Handle, jbitmap, out addrPtr);
		}

		internal static int AndroidBitmap_unlockPixels (IntPtr jbitmap)
		{
			return AndroidBitmap_unlockPixels (Handle, jbitmap);
		}
#endif  // ANDROID_8
	}
}
