namespace SlateClean.Core.Models;

public class CleanupLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }
}
