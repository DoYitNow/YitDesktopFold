using Microsoft.Win32;
using System.Security;

namespace YitDesktopFold.Native.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "YitDesktopFold";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string value &&
                   TryGetExecutablePath(value, out var registeredPath) &&
                   Environment.ProcessPath is { } processPath &&
                   string.Equals(
                       Path.GetFullPath(registeredPath),
                       Path.GetFullPath(processPath),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is
            UnauthorizedAccessException or
            SecurityException or
            IOException or
            ArgumentException or
            NotSupportedException)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定应用程序路径。");
        executablePath = Path.GetFullPath(executablePath);
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException("无法打开当前用户的启动项设置。");
        key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
        if (!IsEnabled())
        {
            throw new IOException("Windows 未能保存当前应用的启动项。");
        }
    }

    private static bool TryGetExecutablePath(string command, out string executablePath)
    {
        executablePath = string.Empty;
        command = command.Trim();
        if (command.Length == 0)
        {
            return false;
        }

        if (command[0] == '"')
        {
            var closingQuote = command.IndexOf('"', 1);
            if (closingQuote <= 1)
            {
                return false;
            }

            executablePath = command[1..closingQuote];
            return true;
        }

        var separator = command.IndexOfAny([' ', '\t']);
        executablePath = separator < 0 ? command : command[..separator];
        return executablePath.Length > 0;
    }
}
