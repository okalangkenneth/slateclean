namespace SlateClean.Core.Models;

public class AppSettings
{
    public int Id { get; set; }
    public long FreeSpaceThresholdBytes { get; set; }
    public bool AutoCleanEnabled { get; set; }
    public bool LaunchOnStartup { get; set; }
    public string MonitoredDrive { get; set; } = "C:";
}
