namespace SlateClean.Core.Models;

// Identifies what caused a cleanup pass — drives both UX (silent vs prompt)
// and the future TriggeredBy column on CleanupLog.
public enum BreachTier
{
    Manual,
    SoftThreshold,
    CriticalThreshold,
}

public sealed record PlannedFileDeletion(
    string Path,
    long SizeBytes,
    DateTime LastWriteUtc);

public sealed record PlannedAppCleanup(
    string AppName,
    IReadOnlyList<PlannedFileDeletion> Files)
{
    public long TotalBytes => Files.Sum(f => f.SizeBytes);
}

// Immutable preview of a cleanup pass. Produced by CleanupService.BuildPlanAsync
// using the same eligibility predicate the executor will use, so the dry-run
// surface shown to the user is exactly what gets deleted.
public sealed record CleanupPlan(
    Guid PlanId,
    DateTime BuiltAtUtc,
    BreachTier Tier,
    IReadOnlyList<PlannedAppCleanup> Apps)
{
    public long TotalBytes => Apps.Sum(a => a.TotalBytes);
    public int TotalFiles => Apps.Sum(a => a.Files.Count);
}
