using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class DiskMonitorServiceTests
{
    private sealed class StubMonitor : DiskMonitorService
    {
        public long FreeBytesValue { get; set; }
        public long TotalBytesValue { get; set; } = 500L * 1024 * 1024 * 1024;

        public StubMonitor(Func<DateTime> now)
            : base(Array.Empty<ICacheLocator>(), NullLogger<DiskMonitorService>.Instance, now)
        { }

        protected override long GetFreeBytes(string driveRoot) => FreeBytesValue;
        protected override long GetTotalBytes(string driveRoot) => TotalBytesValue;
    }

    [Fact]
    public void Poll_RaisesEvent_WhenFreeSpaceBelowThreshold()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now)
        {
            ThresholdGb = 20,
            FreeBytesValue = 5L * 1024 * 1024 * 1024,
        };

        DiskThresholdBreachedEventArgs? received = null;
        monitor.DiskThresholdBreached += (_, e) => received = e;

        monitor.Poll();

        Assert.NotNull(received);
        Assert.Equal(20L * 1024 * 1024 * 1024, received!.ThresholdBytes);
        Assert.Equal(5L * 1024 * 1024 * 1024, received.FreeBytes);
    }

    [Fact]
    public void Poll_DoesNotRaiseEvent_WhenFreeSpaceAboveThreshold()
    {
        var monitor = new StubMonitor(() => DateTime.UtcNow)
        {
            ThresholdGb = 20,
            FreeBytesValue = 100L * 1024 * 1024 * 1024,
        };

        var raised = false;
        monitor.DiskThresholdBreached += (_, _) => raised = true;

        monitor.Poll();

        Assert.False(raised);
    }

    [Fact]
    public void Poll_SuppressesSecondEvent_WithinFiveMinutes()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now)
        {
            ThresholdGb = 20,
            FreeBytesValue = 5L * 1024 * 1024 * 1024,
        };

        var count = 0;
        monitor.DiskThresholdBreached += (_, _) => count++;

        monitor.Poll();
        now = now.AddMinutes(2);
        monitor.Poll();
        now = now.AddMinutes(2);
        monitor.Poll();

        Assert.Equal(1, count);
    }

    [Fact]
    public void Poll_AlwaysRaisesDiskSpaceUpdated_RegardlessOfThreshold()
    {
        var now = new DateTime(2026, 5, 18, 9, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now)
        {
            ThresholdGb = 20,
            FreeBytesValue = 100L * 1024 * 1024 * 1024,
            TotalBytesValue = 500L * 1024 * 1024 * 1024,
            DriveRoot = "D:\\",
        };

        DiskSpaceUpdatedEventArgs? received = null;
        monitor.DiskSpaceUpdated += (_, e) => received = e;

        monitor.Poll();

        Assert.NotNull(received);
        Assert.Equal("D:\\", received!.DriveRoot);
        Assert.Equal(100L * 1024 * 1024 * 1024, received.FreeBytes);
        Assert.Equal(500L * 1024 * 1024 * 1024, received.TotalBytes);
        Assert.Equal(now, received.ObservedAtUtc);
    }

    [Fact]
    public void Poll_RaisesDiskSpaceUpdated_BeforeThresholdEvent_OnBreach()
    {
        var now = new DateTime(2026, 5, 18, 9, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now)
        {
            ThresholdGb = 20,
            FreeBytesValue = 5L * 1024 * 1024 * 1024,
            TotalBytesValue = 500L * 1024 * 1024 * 1024,
        };

        var order = new List<string>();
        monitor.DiskSpaceUpdated += (_, _) => order.Add("space");
        monitor.DiskThresholdBreached += (_, _) => order.Add("breach");

        monitor.Poll();

        Assert.Equal(new[] { "space", "breach" }, order);
    }

    [Fact]
    public void Poll_RaisesAgain_AfterFiveMinutes()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new StubMonitor(() => now)
        {
            ThresholdGb = 20,
            FreeBytesValue = 5L * 1024 * 1024 * 1024,
        };

        var count = 0;
        monitor.DiskThresholdBreached += (_, _) => count++;

        monitor.Poll();
        now = now.AddMinutes(5).AddSeconds(1);
        monitor.Poll();

        Assert.Equal(2, count);
    }
}
