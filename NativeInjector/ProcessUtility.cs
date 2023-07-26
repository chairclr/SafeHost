using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NativeInjector;

internal static class ProcessUtility
{
    public static bool TryGetModuleByName(this Process process, string name, [NotNullWhen(returnValue: true)] out ProcessModule? module, bool ignoreCase = true)
    {
        module = null;

        for (int i = 0; i < process.Modules.Count; i++)
        {
            if (ignoreCase)
            {
                if (process.Modules[i].ModuleName.ToLower() == name.ToLower())
                {
                    module = process.Modules[i];
                    return true;
                }
            }
            else
            {
                if (process.Modules[i].ModuleName == name)
                {
                    module = process.Modules[i];
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryGetModuleByPath(this Process process, string path, [NotNullWhen(returnValue: true)] out ProcessModule? module)
    {
        module = null;

        for (int i = 0; i < process.Modules.Count; i++)
        {
            if (process.Modules[i].FileName == path)
            {
                module = process.Modules[i];
                return true;
            }
        }

        return false;
    }
}
