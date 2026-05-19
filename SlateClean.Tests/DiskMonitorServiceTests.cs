using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class DiskMonitorServiceTests
{
    private sealed class StubMonitor : DiskMonitorService
    {
        public long FreeBytesValue { get; set; }
        public long TotalBytesValue { get; set; } = 500L * 1024 * 1024 * 1024;

        public StubMonitor(Func<DateTime> now, Func<AppSettings> getSettings)
            : base(
                Array.Empty<ICacheLocator>(),
                getSettings,
                NullLogger<DiskMonitorService>.Instance,
                now)
        { }

        protected override long GetFreeBytes(string driveRoot) => FreeBytesValue;
        protected override long GetTotalBytes(string driveRoot) => TotalBytesValue;
    }

    private static Func<AppSettings> Settings(
        int softGb,
        bool criticalEnabled = false,
        int criticalGb = 0)
    {
        var s = new AppSettings
        {
            ThresholdGb = softGb,
            CriticalThresholdEnabled = criticalEnabled,
            CriticalThresholdGb = criticalGb,
        };
        return () => s;
    }

    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void Poll_suppresses_soft_breach_while_snoozed()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings
        {
            ThresholdGb = 20,
            SnoozedUntilUtc = now.AddMinutes(30),    // active snooze
        };
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 5 * Gb,
        };
        var raised = false;
        monitor.DiskThresholdBreached += (_, _) => raised = true;

        monitor.Poll();

        Assert.False(raised);
    }

    [Fact]
    public void Poll_emits_soft_breach_after_snooze_expires()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings
        {
            ThresholdGb = 20,
            SnoozedUntilUtc = now.AddMinutes(-1),    // expired
        };
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 5 * Gb,
        };
        DiskThresholdBreachedEventArgs? received = null;
        monitor.DiskThresholdBreached += (_, e) => received = e;

        monitor.Poll();

        Assert.NotNull(received);
        Assert.Equal(BreachTier.SoftThreshold, received!.Tier);
    }

    [Fact]
    public void Poll_emits_critical_breach_even_while_snoozed()
    {
        // Snooze is a UX-interruption defer for the soft-tier prompt path.
        // Critical-tier breaches must fire regardless: the silent-execution
        // path is the dangerous one, and the user's [Snooze 1h] click on a
        // prior soft prompt does not override their critical opt-in.
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings
        {
            ThresholdGb = 20,
            CriticalThresholdEnabled = true,
            CriticalThresholdGb = 5,
            SnoozedUntilUtc = now.AddMinutes(30),    // active snooze
        };
        var monitor = new StubMonitor(() => now, () => settings)
        {
            FreeBytesValue = 3 * Gb,                 // below critical
        };
        DiskThresholdBreachedEventArgs? received = null;
        monitor.DiskThresholdBreached += (_, e) => received = e;

        monitor.Poll();

        Assert.NotNull(received);
        Assert.Equal(BreachTier.CriticalThreshold, received!.Tier);
    }

    [Fact]
    public void Poll_raises_soft_breach_when_below_soft_threshold()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now, Settings(softGb: 20))
        {
            FreeBytesValue = 5 * Gb,
        };

        DiskThresholdBreachedEventArgs? received = null;
        monitor.DiskThresholdBreached += (_, e) => received = e;

        monitor.Poll();

        Assert.NotNull(received);
        Assert.Equal(BreachTier.SoftThreshold, received!.Tier);
        Assert.Equal(20 * Gb, received.ThresholdBytes);
        Assert.Equal(5 * Gb, received.FreeBytes);
    }

    [Fact]
    public void Poll_does_not_raise_when_above_soft_threshold()
    {
        var monitor = new StubMonitor(() => DateTime.UtcNow, Settings(softGb: 20))
        {
            FreeBytesValue = 100 * Gb,
        };
        var raised = false;
        monitor.DiskThresholdBreached += (_, _) => raised = true;

        monitor.Poll();

        Assert.False(raised);
    }

    [Fact]
    public void Poll_raises_critical_breach_when_enabled_and_below_critical()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(
            () => now,
            Settings(softGb: 20, criticalEnabled: true, criticalGb: 5))
        {
            FreeBytesValue = 3 * Gb,
        };
        DiskThresholdBreachedEventArgs? received = null;
        monitor.DiskThresholdBreached += (_, e) => received = e;

        monitor.Poll();

        Assert.NotNull(received);
        Assert.Equal(BreachTier.CriticalThreshold, received!.Tier);
        Assert.Equal(5 * Gb, received.ThresholdBytes);
    }

    [Fact]
    public void Poll_ignores_critical_threshold_when_opt_in_is_off()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(
            () => now,
            Settings(softGb: 20, criticalEnabled: false, criticalGb: 5))
        {
            FreeBytesValue = 3 * Gb,
        };
        DiskThresholdBreachedEventArgs? received = null;
        monitor.DiskThresholdBreached += (_, e) => received = e;

        monitor.Poll();

        // Below both thresholds, but critical opt-in is off → fire as soft.
        Assert.NotNull(received);
        Assert.Equal(BreachTier.SoftThreshold, received!.Tier);
    }

    [Fact]
    public void Poll_hysteresis_suppresses_repeat_below_without_recovery()
    {
        var time = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => time, Settings(softGb: 20))
        {
            FreeBytesValue = 5 * Gb,
        };
        var count = 0;
        monitor.DiskThresholdBreached += (_, _) => count++;

        monitor.Poll();
        // Advance well past the 5-min throttle. Hysteresis must still suppress
        // because free space never recovered above the threshold.
        time = time.AddMinutes(10);
        monitor.Poll();
        time = time.AddMinutes(10);
        monitor.Poll();

        Assert.Equal(1, count);
    }

    [Fact]
    public void Poll_refires_after_recovery_above_threshold_and_drop_back_below()
    {
        var time = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => time, Settings(softGb: 20))
        {
            FreeBytesValue = 5 * Gb,
        };
        var count = 0;
        monitor.DiskThresholdBreached += (_, _) => count++;

        monitor.Poll();                              // fires (first below)
        time = time.AddMinutes(10);
        monitor.FreeBytesValue = 50 * Gb;
        monitor.Poll();                              // recovery (re-arms hysteresis)
        time = time.AddMinutes(10);
        monitor.FreeBytesValue = 5 * Gb;
        monitor.Poll();                              // fires again (transition)

        Assert.Equal(2, count);
    }

    [Fact]
    public void Poll_throttle_suppresses_rapid_recovery_and_re_breach()
    {
        var time = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => time, Settings(softGb: 20))
        {
            FreeBytesValue = 5 * Gb,
        };
        var count = 0;
        monitor.DiskThresholdBreached += (_, _) => count++;

        monitor.Poll();                              // fires
        time = time.AddMinutes(1);
        monitor.FreeBytesValue = 50 * Gb;
        monitor.Poll();                              // recovery
        time = time.AddMinutes(1);
        monitor.FreeBytesValue = 5 * Gb;
        monitor.Poll();                              // would fire (transition) but throttled

        Assert.Equal(1, count);
    }

    [Fact]
    public void Poll_always_raises_disk_space_updated_regardless_of_threshold()
    {
        var now = new DateTime(2026, 5, 18, 9, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now, Settings(softGb: 20))
        {
            FreeBytesValue = 100 * Gb,
            TotalBytesValue = 500 * Gb,
            DriveRoot = "D:\\",
        };
        DiskSpaceUpdatedEventArgs? received = null;
        monitor.DiskSpaceUpdated += (_, e) => received = e;

        monitor.Poll();

        Assert.NotNull(received);
        Assert.Equal("D:\\", received!.DriveRoot);
        Assert.Equal(100 * Gb, received.FreeBytes);
        Assert.Equal(500 * Gb, received.TotalBytes);
        Assert.Equal(now, received.ObservedAtUtc);
    }

    [Fact]
    public void Poll_raises_disk_space_updated_before_threshold_event()
    {
        var now = new DateTime(2026, 5, 18, 9, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now, Settings(softGb: 20))
        {
            FreeBytesValue = 5 * Gb,
            TotalBytesValue = 500 * Gb,
        };
        var order = new List<string>();
        monitor.DiskSpaceUpdated += (_, _) => order.Add("space");
        monitor.DiskThresholdBreached += (_, _) => order.Add("breach");

        monitor.Poll();

        Assert.Equal(new[] { "space", "breach" }, order);
    }

    [Fact]
    public void Poll_critical_wins_when_both_tiers_transition_in_same_poll()
    {
        var time = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(
            () => time,
            Settings(softGb: 20, criticalEnabled: true, criticalGb: 5))
        {
            FreeBytesValue = 100 * Gb,
        };
        var received = new List<BreachTier>();
        monitor.DiskThresholdBreached += (_, e) => received.Add(e.Tier);

        monitor.Poll();                              // above both
        monitor.FreeBytesValue = 3 * Gb;             // below both at once
        time = time.AddMinutes(10);
        monitor.Poll();

        Assert.Single(received);
        Assert.Equal(BreachTier.CriticalThreshold, received[0]);
    }
}
