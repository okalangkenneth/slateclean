using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Data;
using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

// Read/write access to the single AppSettings row, with audit logging when
// security-relevant settings transition. Behaviour wiring (which threshold
// triggers prompt vs silent cleanup) lives in services that *consume* this;
// the repository only persists what the user chose.
public class SettingsRepository
{
    public const string AuditEventCriticalThresholdEnabled = "CriticalThresholdEnabled";
    public const string AuditEventCriticalFire = "CriticalFire";

    private readonly SlateCleanDbContext _db;

    public SettingsRepository(SlateCleanDbContext db)
    {
        _db = db;
    }

    // Returns the single settings row, creating it from defaults on first run.
    public AppSettings Load()
    {
        var s = _db.Settings.FirstOrDefault();
        if (s is null)
        {
            s = new AppSettings();
            _db.Settings.Add(s);
            _db.SaveChanges();
        }
        return s;
    }

    // Persists user-editable fields onto the existing row. Soft-only fields
    // (LastCleanedUtc, AutoCleanEnabled, RunOnStartup) are not touched here.
    // Writes a SettingsAuditLog row when CriticalThresholdEnabled transitions
    // from false to true — the consent event itself is the audit record.
    public void Save(AppSettings updated)
    {
        var existing = _db.Settings.FirstOrDefault();
        if (existing is null)
        {
            _db.Settings.Add(updated);
            if (updated.CriticalThresholdEnabled)
            {
                WriteCriticalOptIn(updated.CriticalThresholdGb);
            }
            _db.SaveChanges();
            return;
        }

        bool wasCriticalEnabled = existing.CriticalThresholdEnabled;

        existing.ThresholdGb = updated.ThresholdGb;
        existing.SendToRecycleBin = updated.SendToRecycleBin;
        existing.CriticalThresholdEnabled = updated.CriticalThresholdEnabled;
        existing.CriticalThresholdGb = updated.CriticalThresholdGb;

        if (!wasCriticalEnabled && updated.CriticalThresholdEnabled)
        {
            WriteCriticalOptIn(updated.CriticalThresholdGb);
        }

        _db.SaveChanges();
    }

    private void WriteCriticalOptIn(int criticalThresholdGb)
    {
        _db.SettingsAuditLogs.Add(new SettingsAuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            EventType = AuditEventCriticalThresholdEnabled,
            Details = $"CriticalThresholdGb={criticalThresholdGb}",
        });
    }

    // Records a critical-tier silent execution. The "I never enabled that"
    // defense: every silent deletion leaves an explicit audit row that names
    // the trigger conditions and the plan it consumed.
    public void WriteCriticalFireAudit(
        DateTime timestampUtc,
        long freeBytesAtTrigger,
        int criticalThresholdGb,
        Guid planId,
        int filesDeleted,
        long bytesFreed)
    {
        _db.SettingsAuditLogs.Add(new SettingsAuditLog
        {
            TimestampUtc = timestampUtc,
            EventType = AuditEventCriticalFire,
            FreeBytesAtTrigger = freeBytesAtTrigger,
            CriticalThresholdGb = criticalThresholdGb,
            PlanId = planId.ToString(),
            FilesDeleted = filesDeleted,
            BytesFreed = bytesFreed,
        });
        _db.SaveChanges();
    }
}
