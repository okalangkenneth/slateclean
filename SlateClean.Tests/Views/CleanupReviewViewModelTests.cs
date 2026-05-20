using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.App.ViewModels;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Models;
using SlateClean.Core.Services;

namespace SlateClean.Tests.Views;

// Covers the production code path the smoke test misses: real VM + real plan,
// verifying that command CanExecute updates correctly after Load. Without
// CleanNowCommand.NotifyCanExecuteChanged in Load the button stayed disabled
// at runtime even though the plan had eligible files.
public class CleanupReviewViewModelTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SlateCleanDbContext _db;

    public CleanupReviewViewModelTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<SlateCleanDbContext>().UseSqlite(_conn).Options;
        _db = new SlateCleanDbContext(opts);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private CleanupReviewViewModel NewVm(Func<DateTime>? utcNow = null) =>
        new(
            new CleanupService(Array.Empty<ICacheLocator>(), _db, NullLogger<CleanupService>.Instance),
            new SettingsRepository(_db),
            NullLogger<CleanupReviewViewModel>.Instance,
            utcNow);

    [Fact]
    public void CleanNow_is_disabled_before_Load()
    {
        var vm = NewVm();
        Assert.False(vm.CleanNowCommand.CanExecute(null));
    }

    [Fact]
    public void CleanNow_becomes_enabled_after_Load_with_eligible_files()
    {
        var vm = NewVm();
        var plan = new CleanupPlan(
            PlanId: Guid.NewGuid(),
            BuiltAtUtc: DateTime.UtcNow,
            Tier: BreachTier.SoftThreshold,
            Apps: new[]
            {
                new PlannedAppCleanup("DaVinci Resolve", new[]
                {
                    new PlannedFileDeletion(@"C:\fake\a.cache", 1024, DateTime.UtcNow.AddDays(-2)),
                }),
            });

        vm.Load(plan);

        Assert.True(vm.CleanNowCommand.CanExecute(null),
            "After Load with eligible files, CleanNow must be enabled — otherwise the button text " +
            "renders dimmed/invisible and the production review window is non-actionable.");
    }

    [Fact]
    public void Cancel_is_always_enabled()
    {
        var vm = NewVm();
        Assert.True(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void SnoozeAsync_persists_one_hour_defer_and_closes_window()
    {
        var fixedNow = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var vm = NewVm(() => fixedNow);
        var plan = new CleanupPlan(
            PlanId: Guid.NewGuid(),
            BuiltAtUtc: fixedNow,
            Tier: BreachTier.SoftThreshold,
            Apps: new[]
            {
                new PlannedAppCleanup("A", new[]
                {
                    new PlannedFileDeletion(@"C:\fake\a.cache", 1024, fixedNow.AddDays(-2)),
                }),
            });
        vm.Load(plan);

        var closed = false;
        vm.RequestClose += (_, _) => closed = true;

        // CanExecute states before invoking: Cancel always on, CleanNow on
        // (plan has files), Snooze on (same condition).
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.True(vm.CleanNowCommand.CanExecute(null));
        Assert.True(vm.Snooze1hCommand.CanExecute(null));

        vm.Snooze1hCommand.Execute(null);

        Assert.True(closed);

        var saved = new SettingsRepository(_db).Load();
        Assert.NotNull(saved.SnoozedUntilUtc);
        Assert.Equal(fixedNow.AddHours(1), saved.SnoozedUntilUtc);

        // Cancel CanExecute unchanged; CleanNow unchanged (plan still
        // present and IsExecuting unchanged).
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.True(vm.CleanNowCommand.CanExecute(null));
    }

    [Fact]
    public void SnoozedUntilUtc_round_trips_through_SettingsRepository()
    {
        var repo = new SettingsRepository(_db);
        var snoozeUntil = new DateTime(2026, 6, 1, 9, 30, 0, DateTimeKind.Utc);

        var settings = repo.Load();
        settings.SnoozedUntilUtc = snoozeUntil;
        repo.Save(settings);

        // Fresh context to defeat any in-memory tracking, simulating the
        // app-restart path that 6e promises.
        using var fresh = new SlateCleanDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SlateCleanDbContext>()
                .UseSqlite(_conn).Options);
        var reloaded = new SettingsRepository(fresh).Load();

        Assert.Equal(snoozeUntil, reloaded.SnoozedUntilUtc);
    }

    [Fact]
    public void Load_exposes_filename_separately_from_full_path_for_legible_display()
    {
        var vm = NewVm();
        var fullPath = @"C:\Users\Ken\AppData\Roaming\Blackmagic Design\DaVinci Resolve\CacheClip\test_1.cache";
        var plan = new CleanupPlan(
            PlanId: Guid.NewGuid(),
            BuiltAtUtc: DateTime.UtcNow,
            Tier: BreachTier.SoftThreshold,
            Apps: new[]
            {
                new PlannedAppCleanup("DaVinci Resolve", new[]
                {
                    new PlannedFileDeletion(fullPath, 1024, DateTime.UtcNow.AddDays(-2)),
                }),
            });

        vm.Load(plan);

        var row = Assert.Single(vm.FileRows);
        Assert.Equal("test_1.cache", row.FileName);
        Assert.Equal(fullPath, row.Path);
    }
}
