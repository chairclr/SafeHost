using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using unsafe LoadAssemblyAndGetFunctionPointerDef = delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, void**, int>;

namespace SafeHost;

internal unsafe partial class DotnetHost
{
    private LoadAssemblyAndGetFunctionPointerDef LoadAssemblyAndGetFunctionPointerDef;

    private void LoadHostfxrDefs(HostfxrHandle* context)
    {
        int rc = HostfxrGetRuntimeDelegate(context, HostfxrDelegateType.LoadAssemblyAndGetFunctionPointer, out void* laPtr);

        if (rc != 0)
        {
            throw FormatException("Failed to get LoadAssemblyAndGetFunctionPointer delegate");
        }

        LoadAssemblyAndGetFunctionPointerDef = (LoadAssemblyAndGetFunctionPointerDef)laPtr;
    }

    private nint LoadAssemblyAndGetFunctionPointer(string path, string assemblyName, string fullyQualifiedTypeName, string functionName)
    {
        void* functionPointer = null;

        int rc = LoadAssemblyAndGetFunctionPointerDef(
            GetStringRef(path),
            GetStringRef($"{fullyQualifiedTypeName}, {assemblyName}"),
            GetStringRef(functionName),
            -1,
            0,
            &functionPointer);

        if (rc != 0 || functionPointer is null)
        {
            throw FormatException($"Failed to load assembly [{path}] and get function pointer for [Assembly = {assemblyName}] [Type: {fullyQualifiedTypeName}] [Name: {functionName}]");
        }

        return (nint)functionPointer;
    }

    private string GetHostfxrPath()
    {
        // Determine the minimum buffer length we will need for the string
        nint bufferSize = 0;
        int rc = GetHostfxrPath(default, &bufferSize, 0);

        if (bufferSize <= 0)
        {
            throw new Exception("Could not get hostfxr path length", Marshal.GetExceptionForHR(rc));
        }

        Span<char> buffer = stackalloc char[(int)bufferSize];

        rc = GetHostfxrPath(buffer, &bufferSize, 0);

        if (rc != 0)
        {
            throw new Exception("Could not get hostfxr path", Marshal.GetExceptionForHR(rc));
        }

        // -1 to create the string without the trailing \0
        return new string(buffer[..((int)bufferSize - 1)]);
    }

    /// <summary>
    /// Helper function to get a string pointer for interop
    /// </summary>
    /// <returns>A native pointer to a C string</returns>
    private nint GetStringRef(string str)
    {
        // TODO: use utf8 strings on other platforms iirc
        return (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference<char>(str + "\0"));
    }

    [LibraryImport("nethost.dll", EntryPoint = "get_hostfxr_path", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int GetHostfxrPath(Span<char> buffer, nint* bufferSize, nint parameters);

    private record struct HostfxrHandle;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void HostfxrErrorFunction(nint message);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct HostfxrInitializeParameters : IDisposable
    {
        public nint Size;

        private nint NativeHostPath;

        private nint NativeDotnetRoot;

        public string HostPath
        {
            set
            {
                if (NativeHostPath != 0)
                {
                    GCHandle.FromIntPtr(NativeDotnetRoot).Free();
                }

                NativeHostPath = GCHandle.Alloc(value, GCHandleType.Pinned).AddrOfPinnedObject();
            }
        }

        public string DotnetRoot
        {
            set
            {
                if (NativeDotnetRoot != 0)
                {
                    GCHandle.FromIntPtr(NativeDotnetRoot).Free();
                }

                NativeDotnetRoot = GCHandle.Alloc(value, GCHandleType.Pinned).AddrOfPinnedObject();
            }
        }

        public void Dispose()
        {
            if (NativeHostPath != 0)
            {
                GCHandle.FromIntPtr(NativeDotnetRoot).Free();
            }

            if (NativeDotnetRoot != 0)
            {
                GCHandle.FromIntPtr(NativeDotnetRoot).Free();
            }

            NativeDotnetRoot = 0;

            NativeDotnetRoot = 0;
        }
    }

    // https://github.com/dotnet/runtime/blob/main/docs/design/features/native-hosting.md#getting-a-delegate-for-runtime-functionality
    private enum HostfxrDelegateType : int
    {
        ComActivation,
        LoadInMemoryAssembly,
        WinRTActivation,
        ComRegister,
        ComUnregister,
        LoadAssemblyAndGetFunctionPointer,
        GetFunctionPointer,
    };

    [LibraryImport("hostfxr.dll", EntryPoint = "hostfxr_set_error_writer", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int HostfxrSetErrorFunction(HostfxrErrorFunction errorFunction);

    [LibraryImport("hostfxr.dll", EntryPoint = "hostfxr_initialize_for_runtime_config", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int HostfxrInitialize(string runtimeConfigPath, in HostfxrInitializeParameters parameters, out HostfxrHandle* hostfxrHandle);

    [LibraryImport("hostfxr.dll", EntryPoint = "hostfxr_get_runtime_delegate", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int HostfxrGetRuntimeDelegate(HostfxrHandle* context, HostfxrDelegateType delegateType, out void* function);

    // https://github.com/dotnet/runtime/blob/main/docs/design/features/native-hosting.md#cleanup
    [LibraryImport("hostfxr.dll", EntryPoint = "hostfxr_close", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int HostfxrClose(HostfxrHandle* context);
}
