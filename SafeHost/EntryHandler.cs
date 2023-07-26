using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SafeHost;

public partial class EntryHandler
{
    private static nint SafeHostModule = 0;

    [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe bool DllMain(nint hinstDLL, uint fdwReason, nint lpvReserved)
    {
        SafeHostModule = hinstDLL;

        switch (fdwReason)
        {
            // DLL_PROCESS_ATTACH
            case 1:
                // We can't actually do a whole lot from inside of DllMain
                // Infact, we can't even use C# threads
                // So we must use a Windows API call to create a thread
                // https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createthread
                CreateThread(0, 0, (nint)(delegate* unmanaged[Cdecl]<void>)&Load, 0, 0, 0);
                break;
            default:
                break;
        }

        return true;
    }

    [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    private static void Load()
    {
        Process currentProcess = Process.GetCurrentProcess();

        nint hWnd = currentProcess.MainWindowHandle;

        Assembly assembly = Assembly.GetExecutingAssembly();

        string message =
            $"""
            Load() has been called

            Process Id: {Environment.ProcessId}
            HWND (for MessageBox): {hWnd}
            Thread Id: {Environment.CurrentManagedThreadId}
            Architecture: {(Environment.Is64BitProcess ? "x86_64" : "x86")}
            Assembly Name: {assembly.GetName()}
            Base Directory: {AppContext.BaseDirectory}
            Stack Trace:
            {new StackTrace(true)}
            """;

        MessageBox(hWnd, message, "Hello from NativeAOT injection", 0);

        // Find the path to this module
        ProcessModule? safeHostModule = null;

        for (int i = 0; i < currentProcess.Modules.Count; i++)
        {
            if (currentProcess.Modules[i].BaseAddress == SafeHostModule)
            {
                safeHostModule = currentProcess.Modules[i];

                break;
            }
        }

        if (safeHostModule is null)
        {
            throw new Exception($"Could not find SafeHost (self) module in processs {currentProcess.ProcessName}/{currentProcess.Id}");
        }

        string dir = Path.GetDirectoryName(safeHostModule.FileName)!;

        DotnetHost dotnetHost = new DotnetHost(dir);

        dotnetHost.Load();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateThread(nint lpThreadAttributes, uint dwStackSize, nint lpStartAddress, nint lpParameter, uint dwCreationFlags, nint lpThreadId);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hWnd, string text, string caption, uint type);
}
