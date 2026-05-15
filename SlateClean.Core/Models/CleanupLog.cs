namespace SlateClean.Core.Models;

public class CleanupLog
{
    public int Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public int FilesDeleted { get; set; }
    public long BytesFreed { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
