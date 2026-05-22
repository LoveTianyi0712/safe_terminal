using Microsoft.Win32;

namespace EndpointComplianceChecker.Helpers;

public static class RegistryHelper
{
    public static string? GetLocalMachineValue(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName)?.ToString();
        }
        catch { return null; }
    }

    public static string? GetCurrentUserValue(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName)?.ToString();
        }
        catch { return null; }
    }

    public static IEnumerable<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        try
        {
            var root = hive switch
            {
                RegistryHive.LocalMachine => Registry.LocalMachine,
                RegistryHive.CurrentUser => Registry.CurrentUser,
                _ => Registry.LocalMachine
            };
            using var key = root.OpenSubKey(keyPath);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    public static Dictionary<string, string?> GetAllValues(RegistryHive hive, string keyPath)
    {
        var result = new Dictionary<string, string?>();
        try
        {
            var root = hive switch
            {
                RegistryHive.LocalMachine => Registry.LocalMachine,
                RegistryHive.CurrentUser => Registry.CurrentUser,
                _ => Registry.LocalMachine
            };
            using var key = root.OpenSubKey(keyPath);
            if (key == null) return result;
            foreach (var name in key.GetValueNames())
                result[name] = key.GetValue(name)?.ToString();
        }
        catch { }
        return result;
    }

    public static RegistryKey? OpenSubKey(RegistryHive hive, string keyPath)
    {
        try
        {
            var root = hive switch
            {
                RegistryHive.LocalMachine => Registry.LocalMachine,
                RegistryHive.CurrentUser => Registry.CurrentUser,
                _ => Registry.LocalMachine
            };
            return root.OpenSubKey(keyPath);
        }
        catch { return null; }
    }
}
