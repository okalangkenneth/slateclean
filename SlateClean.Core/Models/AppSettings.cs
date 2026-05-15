namespace SlateClean.Core.Models;

public class AppSettings
{
    public int Id { get; set; }
    public int ThresholdGb { get; set; } = 20;
    public bool AutoCleanEnabled { get; set; } = false;
    public bool RunOnStartup { get; set; } = false;
    public DateTime? LastCleanedUtc { get; set; }
}
