using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ManagedLibrary;

public partial class EntryHandler
{
    public delegate void EntryDelegate();

    [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static void Entry()
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

        MessageBox(hWnd, message, "Hello from managed .NET hosted by NativeAOT", 0);
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hWnd, string text, string caption, uint type);
}
