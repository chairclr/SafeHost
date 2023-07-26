using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SafeHost;

internal unsafe partial class DotnetHost
{
    public readonly string SafeHostDirectory;

    private static List<string> ErrorLogs = new List<string>();

    public DotnetHost(string safeHostDirectory)
    {
        SafeHostDirectory = safeHostDirectory;

        string hostfxrPath = GetHostfxrPath();

        if (NativeLibrary.Load(hostfxrPath) == 0)
        {
            throw new Exception($"Failed to load hostfxr.dll from path \"{hostfxrPath}\"");
        }
    }

    public bool Load()
    {
        // Set an error call back for more informative error logging
        HostfxrSetErrorFunction(x =>
        {
            string str = new string(MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)x));

            Console.WriteLine(str);
            Debug.WriteLine(str);

            ErrorLogs.Add(str);
        });

        // TODO: use a safe API
        // TODO: proper error checking for if ManagedLibrary/ManagedLibrary.dll/.runtimeconfig.json exist
        int rc = HostfxrInitialize(Path.Combine(SafeHostDirectory, "ManagedLibrary", "ManagedLibrary.runtimeconfig.json"), Unsafe.NullRef<HostfxrInitializeParameters>(), out HostfxrHandle* context);

        if (rc != 0 || context == null)
        {
            throw FormatException("Failed to initialize hostfxr");
        }

        LoadHostfxrDefs(context);

        rc = HostfxrClose(context);

        if (rc != 0)
        {
            throw FormatException("Failed to close hostfxr context");
        }

        // TODO: make this not use function pointers
        // NOTE: maybe have this reference the target managed assembly to get type info more consistently?
        delegate* unmanaged[Cdecl]<void> entryDelegate = (delegate* unmanaged[Cdecl]<void>)LoadAssemblyAndGetFunctionPointer(
            Path.Combine(SafeHostDirectory, "ManagedLibrary", "ManagedLibrary.dll"),
            "ManagedLibrary",
            "ManagedLibrary.EntryHandler",
            "Entry");

        entryDelegate();

        return true;
    }

    private string FormatErrorLogs()
    {
        StringBuilder builder = new StringBuilder();

        builder.AppendLine("Hostfxr Errors:\n");

        foreach (string error in ErrorLogs)
        {
            builder.Append("  ");
            builder.AppendLine(error);
        }

        // Remove trailing newline
        builder.Length -= 1;

        return builder.ToString();
    }

    private Exception FormatException(string message)
    {
        return new Exception($"{message}\n{FormatErrorLogs()}");
    }
}
