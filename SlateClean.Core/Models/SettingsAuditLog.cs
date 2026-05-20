namespace SlateClean.Core.Models;

// Audit trail for events whose consequences are not file-deletions on their
// own (those go in CleanupLog). Two event types live here today:
//
//   "CriticalThresholdEnabled" — the user opted in to silent critical-tier
//       cleanup. Recorded by SettingsRepository.Save on the false→true
//       transition. Details holds "CriticalThresholdGb=<n>".
//
//   "CriticalFire" — a critical-tier breach actually executed a plan
//       silently (no balloon, no Review window). Populates the numeric
//       columns below. This is the "I never enabled that" defense.
//
// Nullable numeric columns let one table serve both event types without
// per-row sentinels — opt-in rows leave the CriticalFire-specific columns
// NULL, fire rows populate them all.
public class SettingsAuditLog
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Details { get; set; }

    public long? FreeBytesAtTrigger { get; set; }
    public int? CriticalThresholdGb { get; set; }
    public string? PlanId { get; set; }
    public int? FilesDeleted { get; set; }
    public long? BytesFreed { get; set; }
}
