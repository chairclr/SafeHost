using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NativeInjector;

internal class Injector
{
    public static void InjectIntoProcess(Process process, string filePath, string entryPoint)
    {
        if (!File.Exists(filePath))
        {
            filePath = Path.GetFullPath(filePath);
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Invalid file path", filePath);
        }

        if (process.TryGetModuleByPath(filePath, out _))
        {
            throw new InvalidOperationException($"Process already contains module '{filePath}'");
        }

        int openProcFlags = NativeMethods.PROCESS_CREATE_THREAD |
            NativeMethods.PROCESS_QUERY_INFORMATION |
            NativeMethods.PROCESS_VM_OPERATION |
            NativeMethods.PROCESS_VM_WRITE |
            NativeMethods.PROCESS_VM_READ;

        nint openProcHandle = NativeMethods.OpenProcess(openProcFlags, false, process!.Id);

        if (openProcHandle == 0)
        {
            throw new Exception($"Failed to obtain handle to process '{process}'");
        }

        try
        {
            LoadLibrary(process, openProcHandle, filePath);

            // We must refresh the process since we've loaded a new module
            process.Refresh();

            // Check if there's actually a loaded module
            if (!process.TryGetModuleByPath(filePath, out _))
            {
                throw new Exception("Failed to get injected module");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(openProcHandle);
        }
    }

    private static void LoadLibrary(Process process, nint openProcHandle, string filePath)
    {
        uint length = (uint)filePath.Length + 1;

        if (!process.TryGetModuleByName("kernel32.dll", out ProcessModule? kernel32Module, ignoreCase: true))
        {
            throw new Exception("Failed to obtain remote module kernel32");
        }

        nint loadLibraryAddr = NativeMethods.GetRemoteProcAddress(process, kernel32Module, "LoadLibraryA");

        if (loadLibraryAddr == 0)
        {
            throw new Exception($"Failed to obtain remote function pointer to LoadLibraryA");
        }

        nint loadLibraryMemAddr = NativeMethods.VirtualAllocEx(openProcHandle, IntPtr.Zero, length, NativeMethods.MEM_COMMIT | NativeMethods.MEM_RESERVE, NativeMethods.PAGE_READWRITE);

        if (loadLibraryMemAddr == 0)
        {
            throw new Exception($"Failed to allocate memory for filePath");
        }

        Span<byte> buffer = stackalloc byte[filePath.Length];
        Encoding.UTF8.GetBytes(filePath, buffer);

        if (!NativeMethods.WriteProcessMemory(openProcHandle, loadLibraryMemAddr, buffer, length, out _))
        {
            throw new Exception($"Failed to write filePath to remote memory");
        }

        nint loadLibraryThread = NativeMethods.CreateRemoteThread(openProcHandle, IntPtr.Zero, 0, loadLibraryAddr, loadLibraryMemAddr, 0, IntPtr.Zero);

        if (loadLibraryThread == 0)
        {
            throw new Exception($"Failed to create LoadLibraryA thread in remote process");
        }

        try
        {
            int loadLibraryWaitResult = NativeMethods.WaitForSingleObject(loadLibraryThread);

            if (loadLibraryWaitResult != 0)
            {
                throw new Exception($"LoadLibaryA thread failed: {loadLibraryWaitResult:X}");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(loadLibraryThread);
        }
    }
}
