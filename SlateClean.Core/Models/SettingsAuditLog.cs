namespace SlateClean.Core.Models;

// Audit trail for settings changes whose consequences are not file-deletions
// (those go in CleanupLog). Currently records the moment the user opts in to
// the critical-threshold silent-cleanup mode.
public class SettingsAuditLog
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Details { get; set; }
}
