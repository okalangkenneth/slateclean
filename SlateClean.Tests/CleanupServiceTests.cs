using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SlateClean.Core.CacheLocations;
using SlateClean.Core.Data;
using SlateClean.Core.Services;
using Xunit;

namespace SlateClean.Tests;

public class CleanupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<SlateCleanDbContext> _opts;

    public CleanupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SlateCleanTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);

        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<SlateCleanDbContext>().UseSqlite(_conn).Options;
        using var ctx = new SlateCleanDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _conn.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SlateCleanDbContext NewContext() => new(_opts);

    private sealed class FakeLocator : ICacheLocator
    {
        public string AppName { get; init; } = "Fake";
        public required DirectoryInfo Directory { get; init; }
        public IEnumerable<DirectoryInfo> GetCacheDirectories() { yield return Directory; }
        public long GetCacheSizeBytes() => 0;
    }

    private sealed class RecordingDeleter : IFileDeleter
    {
        public List<string> Deleted { get; } = new();
        public Action<string>? Hook { get; set; }
        public void Delete(string path)
        {
            Hook?.Invoke(path);
            Deleted.Add(path);
            File.Delete(path);
        }
    }

    private sealed class ThrowingDeleter : IFileDeleter
    {
        public void Delete(string path) =>
            throw new IOException("simulated lock");
    }

    private string WriteFile(string name, DateTime lastWriteUtc, int sizeBytes = 16)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public async Task CleanAsync_SkipsFilesModifiedWithinLast24Hours()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var recent = WriteFile("recent.dat", now.AddHours(-1));
        var locator = new FakeLocator { Directory = new DirectoryInfo(_root) };
        var deleter = new RecordingDeleter();

        using var db = NewContext();
        var svc = new CleanupService(
            new ICacheLocator[] { locator }, db,
            NullLogger<CleanupService>.Instance, deleter, () => now);

        var log = await svc.CleanAsync(locator);

        Assert.True(log.Success);
        Assert.Equal(0, log.FilesDeleted);
        Assert.Empty(deleter.Deleted);
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public async Task CleanAsync_DeletesFilesOlderThan24Hours()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var old1 = WriteFile("old1.dat", now.AddHours(-25), sizeBytes: 100);
        var old2 = WriteFile("old2.dat", now.AddDays(-3), sizeBytes: 200);
        var recent = WriteFile("recent.dat", now.AddHours(-1), sizeBytes: 50);

        var locator = new FakeLocator { Directory = new DirectoryInfo(_root) };
        var deleter = new RecordingDeleter();

        using var db = NewContext();
        var svc = new CleanupService(
            new ICacheLocator[] { locator }, db,
            NullLogger<CleanupService>.Instance, deleter, () => now);

        var log = await svc.CleanAsync(locator);

        Assert.True(log.Success);
        Assert.Equal(2, log.FilesDeleted);
        Assert.Equal(300, log.BytesFreed);
        Assert.Contains(old1, deleter.Deleted);
        Assert.Contains(old2, deleter.Deleted);
        Assert.True(File.Exists(recent));
        Assert.False(File.Exists(old1));
        Assert.False(File.Exists(old2));
    }

    [Fact]
    public async Task CleanAsync_WritesCleanupLogToDb_BeforeFirstDeletion()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        WriteFile("old.dat", now.AddHours(-25));

        var locator = new FakeLocator { AppName = "TestApp", Directory = new DirectoryInfo(_root) };

        bool logExistedWhenDeleteCalled = false;
        var deleter = new RecordingDeleter
        {
            Hook = _ =>
            {
                using var ctx = NewContext();
                logExistedWhenDeleteCalled =
                    ctx.CleanupLogs.Any(l => l.AppName == "TestApp");
            }
        };

        using var db = NewContext();
        var svc = new CleanupService(
            new ICacheLocator[] { locator }, db,
            NullLogger<CleanupService>.Instance, deleter, () => now);

        var log = await svc.CleanAsync(locator);

        Assert.True(logExistedWhenDeleteCalled);
        Assert.Single(deleter.Deleted);
        Assert.True(log.Success);

        using var verify = NewContext();
        var saved = verify.CleanupLogs.Single();
        Assert.Equal("TestApp", saved.AppName);
        Assert.True(saved.Success);
        Assert.Equal(1, saved.FilesDeleted);
    }

    [Fact]
    public async Task CleanAsync_OnDeleterException_SetsSuccessFalseAndDoesNotThrow()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        WriteFile("old.dat", now.AddHours(-25));

        var locator = new FakeLocator { Directory = new DirectoryInfo(_root) };

        using var db = NewContext();
        var svc = new CleanupService(
            new ICacheLocator[] { locator }, db,
            NullLogger<CleanupService>.Instance, new ThrowingDeleter(), () => now);

        var log = await svc.CleanAsync(locator);

        Assert.False(log.Success);
        Assert.NotNull(log.ErrorMessage);
        Assert.Contains("simulated", log.ErrorMessage!);

        using var verify = NewContext();
        var saved = verify.CleanupLogs.Single();
        Assert.False(saved.Success);
        Assert.Equal(0, saved.FilesDeleted);
    }

    [Fact]
    public async Task CleanAllAsync_RunsAllLocators()
    {
        var now = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);
        var dirA = Directory.CreateDirectory(Path.Combine(_root, "A"));
        var dirB = Directory.CreateDirectory(Path.Combine(_root, "B"));
        File.WriteAllBytes(Path.Combine(dirA.FullName, "a.dat"), new byte[10]);
        File.SetLastWriteTimeUtc(Path.Combine(dirA.FullName, "a.dat"), now.AddDays(-2));
        File.WriteAllBytes(Path.Combine(dirB.FullName, "b.dat"), new byte[20]);
        File.SetLastWriteTimeUtc(Path.Combine(dirB.FullName, "b.dat"), now.AddDays(-2));

        var locators = new ICacheLocator[]
        {
            new FakeLocator { AppName = "A", Directory = dirA },
            new FakeLocator { AppName = "B", Directory = dirB },
        };

        using var db = NewContext();
        var svc = new CleanupService(locators, db,
            NullLogger<CleanupService>.Instance, new RecordingDeleter(), () => now);

        var logs = (await svc.CleanAllAsync()).ToList();

        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.True(l.Success));
        Assert.Contains(logs, l => l.AppName == "A" && l.FilesDeleted == 1);
        Assert.Contains(logs, l => l.AppName == "B" && l.FilesDeleted == 1);
    }
}
