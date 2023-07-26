using System.ComponentModel;
using System.Diagnostics;

namespace NativeInjector;

internal class Program
{
    static void Main(string[] args)
    {
        string processNameOrId;
        string path;
        string entryPoint = "NativeEntry";

        if (args.Length == 2)
        {
            processNameOrId = args[0];
            path = args[1];
        }
        else if (args.Length == 3)
        {
            processNameOrId = args[0];
            path = args[1];
            entryPoint = args[2];
        }
        else
        {
            Console.WriteLine("Invalid Arguments");
            Console.WriteLine("Usage:");
            Console.WriteLine("./NativeInjector targetProcessNameOrId /path/to/dll/file ?optionalEntryPointName");

            return;
        }

        Process? process;

        if (int.TryParse(processNameOrId, out int processId))
        {
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"No process with id '{processId}' is running");
                Console.ResetColor();
                return;
            }
        }
        else
        {
            Process[] processes = Process.GetProcessesByName(processNameOrId);

            if (processes.Length < 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"No process named '{processNameOrId}'");
                Console.ResetColor();
                return;
            }
            else if (processes.Length > 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Mulitple processes named '{processNameOrId}'");
                Console.ResetColor();
                Console.WriteLine($"{"PID",6} {"Name",24} Path");
                foreach (Process proc in processes)
                {
                    string? processPath = null;

                    try
                    {
                        processPath = proc.MainModule?.FileName;
                    }
                    catch (Win32Exception)
                    {
                        processPath = "<Access Denied>";
                    }

                    Console.WriteLine($"{proc.Id,6} {proc.ProcessName,24} {processPath ?? "Unknown"}");
                }
                return;
            }
            else
            {
                process = processes.Single();
            }
        }

        try
        {
            Injector.InjectIntoProcess(process, path, entryPoint);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.ToString());
            Console.ResetColor();
        }
    }
}
