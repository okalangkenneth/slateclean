using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class CleanupHistoryServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SlateCleanDbContext _ctx;
    private readonly CleanupHistoryService _svc;

    public CleanupHistoryServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<SlateCleanDbContext>()
            .UseSqlite(_conn)
            .Options;
        _ctx = new SlateCleanDbContext(opts);
        _ctx.Database.EnsureCreated();
        _svc = new CleanupHistoryService(_ctx);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task GetRecent_returns_empty_when_no_logs_exist()
    {
        var rows = await _svc.GetRecentAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetRecent_orders_by_timestamp_descending()
    {
        var t0 = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        _ctx.CleanupLogs.AddRange(
            new CleanupLog { AppName = "A", TimestampUtc = t0, Success = true },
            new CleanupLog { AppName = "B", TimestampUtc = t0.AddHours(2), Success = true },
            new CleanupLog { AppName = "C", TimestampUtc = t0.AddHours(1), Success = true });
        _ctx.SaveChanges();

        var rows = await _svc.GetRecentAsync();

        Assert.Equal(new[] { "B", "C", "A" }, rows.Select(r => r.AppName).ToArray());
    }

    [Fact]
    public async Task GetRecent_caps_at_limit()
    {
        for (int i = 0; i < 60; i++)
        {
            _ctx.CleanupLogs.Add(new CleanupLog
            {
                AppName = $"App{i}",
                TimestampUtc = new DateTime(2026, 5, 1).AddMinutes(i),
                Success = true,
            });
        }
        _ctx.SaveChanges();

        var rows = await _svc.GetRecentAsync(limit: 50);

        Assert.Equal(50, rows.Count);
    }

    [Fact]
    public async Task GetRecent_returns_empty_when_limit_is_zero_or_negative()
    {
        _ctx.CleanupLogs.Add(new CleanupLog { AppName = "A", TimestampUtc = DateTime.UtcNow });
        _ctx.SaveChanges();

        Assert.Empty(await _svc.GetRecentAsync(limit: 0));
        Assert.Empty(await _svc.GetRecentAsync(limit: -5));
    }
}
