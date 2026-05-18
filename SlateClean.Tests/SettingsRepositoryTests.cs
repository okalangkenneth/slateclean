using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class SettingsRepositoryTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SlateCleanDbContext _ctx;
    private readonly SettingsRepository _repo;

    public SettingsRepositoryTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<SlateCleanDbContext>()
            .UseSqlite(_conn)
            .Options;
        _ctx = new SlateCleanDbContext(opts);
        _ctx.Database.EnsureCreated();
        _repo = new SettingsRepository(_ctx);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public void Load_returns_defaults_when_no_row_exists()
    {
        var s = _repo.Load();

        Assert.Equal(20, s.ThresholdGb);
        Assert.False(s.SendToRecycleBin);
        Assert.False(s.CriticalThresholdEnabled);
        Assert.Equal(5, s.CriticalThresholdGb);
    }

    [Fact]
    public void Load_after_first_call_persists_the_default_row()
    {
        _repo.Load();
        var count = _ctx.Settings.Count();
        Assert.Equal(1, count);
    }

    [Fact]
    public void Save_persists_user_editable_fields()
    {
        _repo.Load();
        _repo.Save(new AppSettings
        {
            ThresholdGb = 50,
            SendToRecycleBin = true,
            CriticalThresholdEnabled = true,
            CriticalThresholdGb = 8,
        });

        var loaded = _repo.Load();
        Assert.Equal(50, loaded.ThresholdGb);
        Assert.True(loaded.SendToRecycleBin);
        Assert.True(loaded.CriticalThresholdEnabled);
        Assert.Equal(8, loaded.CriticalThresholdGb);
    }

    [Fact]
    public void Save_writes_audit_row_on_critical_opt_in_transition()
    {
        _repo.Load();
        _repo.Save(new AppSettings
        {
            ThresholdGb = 20,
            SendToRecycleBin = false,
            CriticalThresholdEnabled = true,
            CriticalThresholdGb = 5,
        });

        var audit = _ctx.SettingsAuditLogs.Single();
        Assert.Equal(SettingsRepository.AuditEventCriticalThresholdEnabled, audit.EventType);
        Assert.Contains("CriticalThresholdGb=5", audit.Details);
        Assert.True(audit.TimestampUtc <= DateTime.UtcNow);
        Assert.True(audit.TimestampUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Save_does_not_write_audit_row_when_already_enabled()
    {
        _repo.Load();
        // first opt-in
        _repo.Save(new AppSettings { CriticalThresholdEnabled = true, CriticalThresholdGb = 5 });
        // second save with critical still on
        _repo.Save(new AppSettings { CriticalThresholdEnabled = true, CriticalThresholdGb = 10 });

        Assert.Equal(1, _ctx.SettingsAuditLogs.Count());
    }

    [Fact]
    public void Save_does_not_write_audit_row_when_critical_stays_off()
    {
        _repo.Load();
        _repo.Save(new AppSettings { CriticalThresholdEnabled = false });

        Assert.Empty(_ctx.SettingsAuditLogs);
    }

    [Fact]
    public void Save_writes_new_audit_row_on_re_opt_in_after_disabling()
    {
        _repo.Load();
        _repo.Save(new AppSettings { CriticalThresholdEnabled = true, CriticalThresholdGb = 5 });
        _repo.Save(new AppSettings { CriticalThresholdEnabled = false });
        _repo.Save(new AppSettings { CriticalThresholdEnabled = true, CriticalThresholdGb = 7 });

        Assert.Equal(2, _ctx.SettingsAuditLogs.Count());
    }
}
