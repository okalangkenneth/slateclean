using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace SlateClean.Core.Services;

[SupportedOSPlatform("windows")]
public class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SlateClean";

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService> logger)
    {
        _logger = logger;
    }

    public void EnableRunOnStartup(string appPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        var quoted = $"\"{appPath}\"";
        key.SetValue(ValueName, quoted, RegistryValueKind.String);
        _logger.LogInformation("[Startup] Enabled: {Path}", appPath);
    }

    public void DisableRunOnStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        if (key.GetValue(ValueName) is null) return;
        key.DeleteValue(ValueName, throwOnMissingValue: false);
        _logger.LogInformation("[Startup] Disabled");
    }

    public bool IsRunOnStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }
}
