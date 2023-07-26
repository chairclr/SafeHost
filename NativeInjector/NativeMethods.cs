using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace NativeInjector;

public partial class NativeMethods
{
    public const int PROCESS_CREATE_THREAD = 0x0002;
    public const int PROCESS_QUERY_INFORMATION = 0x0400;
    public const int PROCESS_VM_OPERATION = 0x0008;
    public const int PROCESS_VM_WRITE = 0x0020;
    public const int PROCESS_VM_READ = 0x0010;

    public const uint MEM_COMMIT = 0x00001000;
    public const uint MEM_RESERVE = 0x00002000;
    public const uint PAGE_READWRITE = 4;

    [LibraryImport("kernel32.dll")]
    public static partial nint OpenProcess(int dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    public static partial nint GetModuleHandleA(string lpModuleName);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    public static partial nint GetProcAddress(nint hModule, string procName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint VirtualAllocEx(nint hProcess, nint lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualFreeEx(nint hProcess, nint lpAddress, uint dwSize, AllocationType dwFreeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, Span<byte> lpBuffer, uint nSize, out nuint lpNumberOfBytesWritten);

    [LibraryImport("kernel32.dll")]
    public static partial nint CreateRemoteThread(nint hProcess, nint lpThreadAttributes, uint dwStackSize, nint lpStartAddress, nint lpParameter, uint dwCreationFlags, nint lpThreadId);

    [LibraryImport("kernel32.dll")]
    public static partial int WaitForSingleObject(nint hHandle, int ms = Timeout.Infinite);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(nint hProcess, nint lpBaseAddress, Span<byte> lpBuffer, int dwSize, out nint lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [SuppressUnmanagedCodeSecurity]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    private unsafe static T ReadRemoteStruct<T>(nint process, nint address) where T : unmanaged
    {
        T ret = default;

        Span<byte> bytes = MemoryMarshal.AsBytes(new Span<T>(ref ret));

        ReadProcessMemory(process, address, bytes, bytes.Length, out _);

        return ret;
    }

    private unsafe static Span<T> ReadRemoteStructArray<T>(nint process, nint address, int count) where T : unmanaged
    {
        Span<T> buffer = new Span<T>(new T[count]);
        Span<byte> bytes = MemoryMarshal.AsBytes(buffer);

        ReadProcessMemory(process, address, bytes, bytes.Length, out _);

        return buffer;
    }

    public static nint GetRemoteProcAddress(Process process, ProcessModule processModule, string functionName)
    {
        nint hProcess = process.Handle;
        nint hModule = processModule.BaseAddress;

        IMAGE_DOS_HEADER dosHeader = ReadRemoteStruct<IMAGE_DOS_HEADER>(hProcess, hModule);
        IMAGE_FILE_HEADER fileHeader = ReadRemoteStruct<IMAGE_FILE_HEADER>(hProcess, (nint)(hModule + dosHeader.e_lfanew + sizeof(int)));

        nint exportTableAddress;

        if (fileHeader.SizeOfOptionalHeader == Unsafe.SizeOf<IMAGE_OPTIONAL_HEADER64>())
        {
            IMAGE_OPTIONAL_HEADER64 optionalHeader = ReadRemoteStruct<IMAGE_OPTIONAL_HEADER64>(hProcess, (nint)(hModule + dosHeader.e_lfanew + sizeof(int) + Unsafe.SizeOf<IMAGE_FILE_HEADER>()));
            exportTableAddress = hModule + optionalHeader.ExportTable.VirtualAddress;
        }
        else if (fileHeader.SizeOfOptionalHeader == Unsafe.SizeOf<IMAGE_OPTIONAL_HEADER32>())
        {
            IMAGE_OPTIONAL_HEADER32 optionalHeader = ReadRemoteStruct<IMAGE_OPTIONAL_HEADER32>(hProcess, (nint)(hModule + dosHeader.e_lfanew + sizeof(int) + Unsafe.SizeOf<IMAGE_FILE_HEADER>()));
            exportTableAddress = hModule + optionalHeader.ExportTable.VirtualAddress;
        }
        else
        {
            throw new Exception("Invalid optional header size.");
        }

        IMAGE_EXPORT_DIRECTORY exportDirectory = ReadRemoteStruct<IMAGE_EXPORT_DIRECTORY>(hProcess, exportTableAddress);

        nint functionTableAddress = (nint)(hModule + exportDirectory.AddressOfFunctions);
        nint nameTableAddress = (nint)(hModule + exportDirectory.AddressOfNames);

        Span<int> functionTable = ReadRemoteStructArray<int>(hProcess, functionTableAddress, (int)exportDirectory.NumberOfFunctions);
        Span<int> functionNameTable = ReadRemoteStructArray<int>(hProcess, nameTableAddress, (int)exportDirectory.NumberOfNames);

        // We know that the exported name will have the same length as `functionName`
        // We can just allocate a buffer of the same size as `functionName`
        // This simplifies the process by allowing us to not have to care about null termination or string lengths or anything like that
        byte[] functionNameBytes = new byte[functionName.Length];
        for (int i = 0; i < functionNameTable.Length; i++)
        {
            ReadProcessMemory(hProcess, hModule + functionNameTable[i], functionNameBytes, functionNameBytes.Length, out _);
            string exportedFunctionName = Encoding.ASCII.GetString(functionNameBytes);

            if (exportedFunctionName == functionName)
            {
                nint functionAddress = functionTable[i] + hModule;

                return functionAddress;
            }
        }

        return 0;
    }

    [Flags]
    public enum AllocationType
    {
        Commit = 0x1000,
        Reserve = 0x2000,
        Decommit = 0x4000,
        Release = 0x8000,
        Reset = 0x80000,
        Physical = 0x400000,
        TopDown = 0x100000,
        WriteWatch = 0x200000,
        LargePages = 0x20000000
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IMAGE_DOS_HEADER
    {
        public ushort e_magic;
        public ushort e_cblp;
        public ushort e_cp;
        public ushort e_crlc;
        public ushort e_cparhdr;
        public ushort e_minalloc;
        public ushort e_maxalloc;
        public ushort e_ss;
        public ushort e_sp;
        public ushort e_csum;
        public ushort e_ip;
        public ushort e_cs;
        public ushort e_lfarlc;
        public ushort e_ovno;
        public ushort e_res_0;
        public ushort e_res_1;
        public ushort e_res_2;
        public ushort e_res_3;
        public ushort e_oemid;
        public ushort e_oeminfo;
        public ushort e_res2_0;
        public ushort e_res2_1;
        public ushort e_res2_2;
        public ushort e_res2_3;
        public ushort e_res2_4;
        public ushort e_res2_5;
        public ushort e_res2_6;
        public ushort e_res2_7;
        public ushort e_res2_8;
        public ushort e_res2_9;
        public uint e_lfanew;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGE_DATA_DIRECTORY
    {
        public int VirtualAddress;
        public int Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGE_FILE_HEADER
    {
        public ushort Machine;
        public ushort NumberOfSections;
        public uint TimeDateStamp;
        public uint PointerToSymbolTable;
        public uint NumberOfSymbols;
        public ushort SizeOfOptionalHeader;
        public ushort Characteristics;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGE_OPTIONAL_HEADER64
    {
        public ushort Magic;
        public byte MajorLinkerVersion;
        public byte MinorLinkerVersion;
        public uint SizeOfCode;
        public uint SizeOfInitializedData;
        public uint SizeOfUninitializedData;
        public uint AddressOfEntryPoint;
        public uint BaseOfCode;
        public ulong ImageBase;
        public uint SectionAlignment;
        public uint FileAlignment;
        public ushort MajorOperatingSystemVersion;
        public ushort MinorOperatingSystemVersion;
        public ushort MajorImageVersion;
        public ushort MinorImageVersion;
        public ushort MajorSubsystemVersion;
        public ushort MinorSubsystemVersion;
        public uint Win32VersionValue;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public uint CheckSum;
        public ushort Subsystem;
        public ushort DllCharacteristics;
        public ulong SizeOfStackReserve;
        public ulong SizeOfStackCommit;
        public ulong SizeOfHeapReserve;
        public ulong SizeOfHeapCommit;
        public uint LoaderFlags;
        public uint NumberOfRvaAndSizes;

        public IMAGE_DATA_DIRECTORY ExportTable;
        public IMAGE_DATA_DIRECTORY ImportTable;
        public IMAGE_DATA_DIRECTORY ResourceTable;
        public IMAGE_DATA_DIRECTORY ExceptionTable;
        public IMAGE_DATA_DIRECTORY CertificateTable;
        public IMAGE_DATA_DIRECTORY BaseRelocationTable;
        public IMAGE_DATA_DIRECTORY Debug;
        public IMAGE_DATA_DIRECTORY Architecture;
        public IMAGE_DATA_DIRECTORY GlobalPtr;
        public IMAGE_DATA_DIRECTORY TLSTable;
        public IMAGE_DATA_DIRECTORY LoadConfigTable;
        public IMAGE_DATA_DIRECTORY BoundImport;
        public IMAGE_DATA_DIRECTORY IAT;
        public IMAGE_DATA_DIRECTORY DelayImportDescriptor;
        public IMAGE_DATA_DIRECTORY CLRRuntimeHeader;
        public IMAGE_DATA_DIRECTORY Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGE_OPTIONAL_HEADER32
    {
        public ushort Magic;
        public byte MajorLinkerVersion;
        public byte MinorLinkerVersion;
        public uint SizeOfCode;
        public uint SizeOfInitializedData;
        public uint SizeOfUninitializedData;
        public uint AddressOfEntryPoint;
        public uint BaseOfCode;
        public uint BaseOfData;
        public uint ImageBase;
        public uint SectionAlignment;
        public uint FileAlignment;
        public ushort MajorOperatingSystemVersion;
        public ushort MinorOperatingSystemVersion;
        public ushort MajorImageVersion;
        public ushort MinorImageVersion;
        public ushort MajorSubsystemVersion;
        public ushort MinorSubsystemVersion;
        public uint Win32VersionValue;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public uint CheckSum;
        public ushort Subsystem;
        public ushort DllCharacteristics;
        public uint SizeOfStackReserve;
        public uint SizeOfStackCommit;
        public uint SizeOfHeapReserve;
        public uint SizeOfHeapCommit;
        public uint LoaderFlags;
        public uint NumberOfRvaAndSizes;

        public IMAGE_DATA_DIRECTORY ExportTable;
        public IMAGE_DATA_DIRECTORY ImportTable;
        public IMAGE_DATA_DIRECTORY ResourceTable;
        public IMAGE_DATA_DIRECTORY ExceptionTable;
        public IMAGE_DATA_DIRECTORY CertificateTable;
        public IMAGE_DATA_DIRECTORY BaseRelocationTable;
        public IMAGE_DATA_DIRECTORY Debug;
        public IMAGE_DATA_DIRECTORY Architecture;
        public IMAGE_DATA_DIRECTORY GlobalPtr;
        public IMAGE_DATA_DIRECTORY TLSTable;
        public IMAGE_DATA_DIRECTORY LoadConfigTable;
        public IMAGE_DATA_DIRECTORY BoundImport;
        public IMAGE_DATA_DIRECTORY IAT;
        public IMAGE_DATA_DIRECTORY DelayImportDescriptor;
        public IMAGE_DATA_DIRECTORY CLRRuntimeHeader;
        public IMAGE_DATA_DIRECTORY Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGE_EXPORT_DIRECTORY
    {
        public uint Characteristics;
        public uint TimeDateStamp;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public uint Name;
        public uint Base;
        public uint NumberOfFunctions;
        public uint NumberOfNames;
        public uint AddressOfFunctions;
        public uint AddressOfNames;
        public uint AddressOfNameOrdinals;
    }
}