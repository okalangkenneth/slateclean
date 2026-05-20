using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SlateClean.App.Services;
using SlateClean.App.ViewModels;
using SlateClean.Core.Data;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class SettingsViewModelTests : IDisposable
{
    private const long Gb = 1024L * 1024L * 1024L;

    private sealed class FakeConfirmation : IConfirmationService
    {
        public bool ReturnValue { get; set; }
        public int Calls { get; private set; }
        public int? LastCriticalGb { get; private set; }
        public double? LastFreeGb { get; private set; }

        public bool ConfirmCriticalThresholdAboveFreeSpace(int criticalThresholdGb, double currentFreeGb)
        {
            Calls++;
            LastCriticalGb = criticalThresholdGb;
            LastFreeGb = currentFreeGb;
            return ReturnValue;
        }
    }

    private readonly SqliteConnection _conn;
    private readonly SlateCleanDbContext _ctx;
    private readonly SettingsRepository _repo;

    public SettingsViewModelTests()
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

    private SettingsViewModel BuildVm(IConfirmationService confirmation, long freeBytes) =>
        new(_repo, confirmation, () => freeBytes);

    [Fact]
    public void Save_with_critical_below_free_space_does_not_prompt()
    {
        var confirm = new FakeConfirmation { ReturnValue = false };
        var vm = BuildVm(confirm, freeBytes: 100 * Gb);

        vm.CriticalThresholdEnabled = true;
        vm.CriticalThresholdGb = 50;            // 50 < 100 → safe

        vm.SaveCommand.Execute(null);

        Assert.Equal(0, confirm.Calls);
        Assert.Equal(50, _repo.Load().CriticalThresholdGb);
        Assert.True(_repo.Load().CriticalThresholdEnabled);
    }

    [Fact]
    public void Save_with_critical_disabled_does_not_prompt_even_when_value_exceeds_free()
    {
        // The dangerous combination is enabled + above-free; if the user hasn't
        // opted in, the value is dormant and there's nothing to warn about.
        var confirm = new FakeConfirmation { ReturnValue = false };
        var vm = BuildVm(confirm, freeBytes: 50 * Gb);

        vm.CriticalThresholdEnabled = false;
        vm.CriticalThresholdGb = 200;           // above free, but disabled

        vm.SaveCommand.Execute(null);

        Assert.Equal(0, confirm.Calls);
        Assert.Equal(200, _repo.Load().CriticalThresholdGb);
        Assert.False(_repo.Load().CriticalThresholdEnabled);
    }

    [Fact]
    public void Save_with_dangerous_settings_invokes_confirmation()
    {
        var confirm = new FakeConfirmation { ReturnValue = true };
        var vm = BuildVm(confirm, freeBytes: 50 * Gb);

        vm.CriticalThresholdEnabled = true;
        vm.CriticalThresholdGb = 200;           // 200 > 50 → dangerous

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, confirm.Calls);
        Assert.Equal(200, confirm.LastCriticalGb);
        Assert.Equal(50.0, confirm.LastFreeGb!.Value, precision: 1);
    }

    [Fact]
    public void Save_with_dangerous_settings_when_user_confirms_persists()
    {
        var confirm = new FakeConfirmation { ReturnValue = true };
        var vm = BuildVm(confirm, freeBytes: 50 * Gb);
        var closeRaised = false;
        vm.RequestClose += (_, _) => closeRaised = true;

        vm.CriticalThresholdEnabled = true;
        vm.CriticalThresholdGb = 200;

        vm.SaveCommand.Execute(null);

        var persisted = _repo.Load();
        Assert.True(persisted.CriticalThresholdEnabled);
        Assert.Equal(200, persisted.CriticalThresholdGb);
        Assert.True(closeRaised);
    }

    [Fact]
    public void Save_with_dangerous_settings_when_user_cancels_does_not_persist()
    {
        var confirm = new FakeConfirmation { ReturnValue = false };
        var vm = BuildVm(confirm, freeBytes: 50 * Gb);
        var closeRaised = false;
        vm.RequestClose += (_, _) => closeRaised = true;

        // Establish a known persisted baseline (the defaults written when the
        // VM construction triggered Load) before the edit.
        var baseline = _repo.Load();
        var baselineCriticalGb = baseline.CriticalThresholdGb;
        Assert.False(baseline.CriticalThresholdEnabled);

        // User edits the VM but cancels at the confirmation dialog.
        vm.CriticalThresholdEnabled = true;
        vm.CriticalThresholdGb = 200;

        vm.SaveCommand.Execute(null);

        // Repo unchanged from baseline — settings NOT persisted.
        var afterCancel = _repo.Load();
        Assert.False(afterCancel.CriticalThresholdEnabled);
        Assert.Equal(baselineCriticalGb, afterCancel.CriticalThresholdGb);

        // VM state preserved so the user can adjust without re-typing.
        Assert.True(vm.CriticalThresholdEnabled);
        Assert.Equal(200, vm.CriticalThresholdGb);

        // Window stays open (no RequestClose).
        Assert.False(closeRaised);
    }
}
