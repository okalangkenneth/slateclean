# SlateClean — Project Rules

## Memory Architecture (3-Layer System)

| Layer | Location | Purpose | Auto-Loaded |
|-------|----------|---------|-------------|
| **CLAUDE.md** | Project root | Rules, workflow, conventions | ✅ Always |
| **MEMORY.md** | `~/.claude/projects/<project>/memory/` | Session learnings, patterns Claude discovers | ✅ First 200 lines |
| **claude-mem** | `~/.claude-mem/` | Deep searchable history, AI-compressed | ✅ Via MCP injection |

### Memory Commands

| Command | Purpose |
|---------|---------|
| `/memory` | View/toggle auto-memory, edit CLAUDE.md |
| `/remember` | Suggest patterns to save permanently |
| `/compact` | Instant (uses pre-written Session Memory) |
| `/dream` | Manually trigger memory consolidation |
| `Ctrl+O` | Expand "Recalled/Wrote memories" details |

---

## Project Overview

**SlateClean** — A lightweight Windows system tray app that monitors disk usage and
automatically clears cache files from DaVinci Resolve, Premiere Pro, and After Effects
when free space drops below a user-defined threshold.

**Target audience:** Video editors and content creators on Windows with limited disk space.

**Website:** https://slateclean.app

**Distribution:** Gumroad ($20 one-time). Free tier: manual clear only. Paid: auto-clean + scheduling.

---

## Tech Stack

- **Runtime**: .NET 9
- **UI Framework**: WPF (dashboard window)
- **System Tray**: NotifyIcon (WPF + Windows Forms interop)
- **Database**: SQLite via EF Core (cleanup history, settings)
- **Testing**: xUnit
- **Package Manager**: NuGet / dotnet CLI
- **Installer**: WiX Toolset (.msi)
- **Memory**: Native auto-memory + claude-mem for deep history

---

## Solution Structure

```
SlateClean/
├── SlateClean.Core/               # Business logic — no UI dependencies
│   ├── CacheLocations/            # Per-app cache path resolvers
│   │   ├── DaVinciCacheLocator.cs
│   │   ├── PremiereCacheLocator.cs
│   │   └── AfterEffectsCacheLocator.cs
│   ├── Services/
│   │   ├── DiskMonitorService.cs  # Polls free space every 60s
│   │   ├── CleanupService.cs      # Safe deletion logic + SQLite logging
│   │   ├── IFileDeleter.cs        # Seam for testing deletion (real vs fake)
│   │   └── StartupService.cs      # Windows "Run at startup" registry toggle
│   ├── Models/
│   │   ├── CacheLocation.cs
│   │   ├── CleanupLog.cs
│   │   └── AppSettings.cs
│   └── Data/
│       └── SlateCleanDbContext.cs  # EF Core context
│
├── SlateClean.App/                # WPF application
│   ├── Views/
│   │   ├── DashboardWindow.xaml
│   │   └── SettingsWindow.xaml
│   ├── ViewModels/
│   │   ├── DashboardViewModel.cs
│   │   └── SettingsViewModel.cs
│   ├── TrayIcon/
│   │   └── TrayIconManager.cs
│   ├── App.xaml
│   └── App.xaml.cs
│
└── SlateClean.Tests/              # xUnit test project
    ├── CacheLocationTests.cs
    └── CleanupServiceTests.cs
```

---

## Cache Paths (Windows)

These are the known cache directories per application. Always resolve using
`Environment.GetFolderPath` — never hardcode usernames.

```
DaVinci Resolve:
  %APPDATA%\Blackmagic Design\DaVinci Resolve\CacheClip

Premiere Pro:
  %APPDATA%\Adobe\Common\Media Cache Files
  %APPDATA%\Adobe\Common\Media Cache

After Effects:
  %APPDATA%\Adobe\Common\Media Cache Files   (shared with Premiere)
  %LOCALAPPDATA%\Adobe\After Effects\<version>\disk cache
```

> Note: Users can relocate cache folders in app settings. Phase 2 will add
> custom path detection by reading app preferences files.

---

## Build Progress (KEEP UPDATED)

**Claude Code: Update this section at the end of every session.**

### ✅ COMPLETED (Phase 1)
- Solution scaffold — `SlateClean.Core`, `SlateClean.App`, `SlateClean.Tests`
- CleanupLog model — `SlateClean.Core/Models/CleanupLog.cs`
- AppSettings model — `SlateClean.Core/Models/AppSettings.cs`
- SlateCleanDbContext with SQLite — `SlateClean.Core/Data/SlateCleanDbContext.cs`
- Cache locators (DaVinci, Premiere, AE) + base class — `SlateClean.Core/CacheLocations/`
- CacheLocator aggregator with size calculation — `SlateClean.Core/Services/CacheLocator.cs`
- DiskMonitorService (60s polling, 5-min throttle) — `SlateClean.Core/Services/DiskMonitorService.cs`
- CleanupService with safe deletion + SQLite logging — `SlateClean.Core/Services/CleanupService.cs`
- IFileDeleter seam for testable deletion — `SlateClean.Core/Services/IFileDeleter.cs`
- StartupService (Windows registry "run at startup") — `SlateClean.Core/Services/StartupService.cs`
- TrayIconManager with right-click menu, Clear Now (aggregate), double-click → dashboard, startup toggle — `SlateClean.App/TrayIcon/TrayIconManager.cs`
- 24 xUnit tests — all passing

### 🔨 IN PROGRESS
- Slice 6 — Auto-clean trigger. 6a + 6b + observability fix landed (full dry-run
  pipeline runs end-to-end without deletion). 6c–6f remaining: Review window +
  soft-path execution, critical-path silent execution, Snooze 1h, TriggeredBy
  column on CleanupLog.

### ✅ COMPLETED (Phase 2)
- Slice 1 — DI host + singleton DashboardWindow/VM, tray double-click, Hide-on-close — `ab9d2e8`
- Slice 2 — Disk usage card bound to DiskMonitorService, "Last updated" timestamp — `52141ac`, `cda1234`, `2f864c0`
- Slice 3 — Per-app cache sizes with async refresh — `1c2a8fd`, fix in `c8bb96e`
- Slice 4 — XAML smoke test (`421e625`) + Settings UI / SQLite persistence for soft threshold, Recycle Bin toggle (default off), critical-threshold opt-in with explicit consent text, SettingsAuditLog row on critical opt-in — `5bcf7df`
- Slice 5 — Cleanup history card with auto-refresh on Clear Now via CleanupService.CleanupCompleted event — `2c0d593`
- Slice 6a — `CleanupPlan` record + `CleanupService.BuildPlanAsync`; shared `EnumerateEligibleFiles` predicate so dry-run = execution — `e6d20a7`
- Slice 6b — Tier-aware DiskThresholdBreached (Soft/Critical), per-tier edge-triggered hysteresis + 5-min throttle, settings-driven via `Func<AppSettings>`, AutoCleanCoordinator builds plans and logs only (no execution) — `68d10db`
- Observability — FileLoggerProvider writes Information+ to `%LocalAppData%\SlateClean\logs\slateclean.log` so manual verification can tail with `Get-Content -Wait` — `109d2a2`

### ❌ REMAINING

**Phase 1 — Core Engine** (all done — keep for history)
- [x] Solution scaffold (3 projects)
- [x] CacheLocator per app (DaVinci, Premiere, AE)
- [x] Cache size calculation
- [x] DiskMonitorService (60s polling)
- [x] CleanupService with safe deletion
- [x] SQLite logging via EF Core
- [x] System tray icon + right-click menu
- [x] Manual "Clear Now" from tray menu (aggregate — per-app variant deferred)
- [x] Windows startup toggle

**Phase 2 — Dashboard + Auto-Clean** (sliced, one commit per slice)
- [x] Slice 1 — Plumbing: CommunityToolkit.Mvvm + DI host, singleton DashboardWindow + DashboardViewModel, tray double-click + bolded "Open Dashboard" menu item, Hide-on-close pattern
- [x] Slice 2 — Disk usage bar bound read-only to DiskMonitorService
- [x] Slice 3 — Cache sizes per app (live, refresh on demand)
- [x] Slice 4 — Settings UI + SQLite persistence for threshold, Recycle Bin toggle, critical-threshold opt-in
- [x] Slice 5 — Cleanup history view (read-only query against CleanupLog)
- [ ] Slice 6 — Auto-clean trigger (in progress)
  - [x] 6a — CleanupPlan record + BuildPlanAsync (shared predicate)
  - [x] 6b — Subscribe to ThresholdBreached, build plan, log only (dry-run)
  - [ ] 6c — CleanupReviewWindow + soft-path execution (balloon → review → Clean now)
  - [ ] 6d — Critical-path silent execution (when CriticalThresholdEnabled && free < critical)
  - [ ] 6e — Snooze 1h respected by breach handler (adds SnoozedUntilUtc to AppSettings)
  - [ ] 6f — TriggeredBy column on CleanupLog (Manual / SoftThreshold / CriticalThreshold)
- [ ] Slice 7 — Windows toast notifications (richer Review / Clean now / Snooze 1h actions; Phase 1 balloon-tip is the placeholder)

### 📋 Backlog (out of slice scope, captured for later)
- Dashboard banner ("Cleanup recommended — disk at N%, click to review") so users with Focus Assist suppressing toasts still see the breach state
- Persistent banner after critical-mode silent auto-clean if disk stays below threshold ~30 min ("cleanup completed but disk still below threshold; free other files manually")
- Settings changes don't currently re-propagate to running services without restart (DiskMonitorService now reads fresh via `Func<AppSettings>`; other services may not)
- Dead `ICacheLocator[] _locators` field in DiskMonitorService is unused — cleanup candidate

**Phase 3 — Distribution**
- [ ] WiX installer (.msi)
- [ ] Code signing certificate
- [ ] Auto-updater
- [ ] Gumroad license key validation
- [ ] Free vs paid tier enforcement

**Phase 4 — Landing Page**
- [ ] slateclean.app one-page site (Vite + React)
- [ ] Beta email capture → Supabase
- [ ] Pricing section

---

## Build State (UPDATE EVERY SESSION)

| Field | Value |
|-------|-------|
| Last known clean build | 2026-05-18 — Phase 2 through Slice 6b + observability |
| Build command | `dotnet build` |
| Test command | `dotnet test` |
| Last run by Claude | 2026-05-18 — 53/53 tests passing |
| Log tail | `Get-Content -Wait $env:LOCALAPPDATA\SlateClean\logs\slateclean.log` |

### Current Build Errors
```
None
```

### Current Warnings
```
None
```

---

## Workflow

```
1. Make only the explicitly requested change
2. Build:     dotnet build
3. Test:      dotnet test
4. Verify behavior manually if UI-related
5. Commit:    conventional commits (feat:, fix:, chore:)
6. Push:      git push
7. Update Build Progress section
```

---

## Git Conventions

- **Branching**: `main` = production. Feature: `git checkout -b feat/name`
- **Commits**: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`
- **Before commit**: `dotnet build && dotnet test`
- **Never commit**: `.env`, API keys, secrets, connection strings, `*.user` files

### First-time setup
```bash
git init
git add .
git commit -m "feat: initial solution scaffold"
git remote add origin https://github.com/okalangkenneth/slateclean.git
git branch -M main
git push -u origin main
```

### After every phase
```bash
git add .
git commit -m "feat: Phase X — <short description>"
git push
```

---

## Critical Safety Rules (NON-NEGOTIABLE)

These rules protect users from data loss. Never bypass them.

```
NEVER delete files outside the known cache directory paths listed above.
NEVER delete files modified within the last 24 hours.
NEVER delete files if free space is already above threshold (no-op).
ALWAYS log the full file path and size to SQLite BEFORE executing deletion.
ALWAYS verify the target directory exists and matches a known cache path before deleting.
ALWAYS present a dry-run summary in the UI before any bulk auto-clean.
IF ANY DOUBT — skip the file and log a warning. Never guess.
```

### Safe Deletion Pattern (always follow this)
```csharp
// 1. Verify path is within an allowed cache directory
// 2. Check file was not modified in the last 24 hours
// 3. Log to SQLite (path, size, timestamp, app name, planId) — LogPendingDeletion
// 4. Delete via IFileDeleter (real or Recycle Bin, per setting)
// 5. Verify deletion succeeded
// 6. Update log entry with result — UpdateDeletionResult
```

> Logging lives inside `CleanupService` deliberately — splitting it into a
> separate `CleanupLogger` would allow the deletion step to be called without
> the log step, which the safety rules forbid. The `IFileDeleter` seam already
> provides the testability needed (assert log row exists in SQLite at the
> moment `IFileDeleter.Delete` is called).

---

## Deletion Policy

**Default:** permanent delete. Media cache routinely hits 50–500 GB; Recycle
Bin defaults defeat the entire app. The SQLite `CleanupLog` table is the audit
trail, not the Recycle Bin.

**Opt-in:** Settings toggle "Send to Recycle Bin instead". Implemented as a
second `IFileDeleter` implementation (`RecycleBinFileDeleter`) using
`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile` with the recycle option.
The deleter implementation is resolved per cleanup operation from
`AppSettings.UseRecycleBin`.

---

## Auto-Clean Tier Model

Three tiers gate when deletion actually fires. The free/paid split is enforced
in `CleanupService` and the dashboard hides paid controls for free users.

| Tier | Trigger | Free | Paid (default) |
|------|---------|------|----------------|
| Manual clear | User clicks Clear Now from tray or dashboard | ✅ | ✅ |
| Soft threshold | Free space drops below user-set threshold → toast prompt | ❌ | ✅ ON |
| Critical threshold | Free space drops below critical % → silent auto-clean | ❌ | ❌ OFF (opt-in) |

**Soft-threshold flow:** breach event → `CleanupService.BuildPlan()` →
toast "Disk at 91%. 4.2 GB ready to clean." with Review / Clean now / Snooze 1h
actions. Review opens dashboard with the plan pre-selected. Snooze writes
`AppSettings.SnoozedUntil`.

**Critical-threshold flow:** same `BuildPlan()`, no toast gate, deletion runs
immediately. Toast fires AFTER reporting what was freed.

**Critical toggle audit:** When the user enables critical-threshold silent
auto-clean, log the setting change itself to SQLite as a `SettingChanged` row
(or equivalent). Required for the "I never enabled that!" complaint that will
arrive eventually.

---

## CleanupPlan — Shared Audit Unit

Both the soft-threshold toast review and the silent critical path consume the
same `CleanupPlan` produced by `CleanupService.BuildPlan()`. This guarantees
identical safety rules (24-hour exclusion, known-paths-only) regardless of
entry point.

```csharp
public record CleanupPlan(
    Guid PlanId,
    IReadOnlyList<PlannedDeletion> Files,
    long TotalBytes,
    BreachReason Reason,         // ManualClear, SoftThreshold, CriticalThreshold
    DateTimeOffset CreatedAt,
    int ThresholdTierPercent);   // The % that triggered it (e.g. 90, 98)
```

Every `CleanupLog` row stores the `PlanId` it belongs to. From a single plan
ID you can reconstruct exactly which breach event caused which deletions —
the plan IS the audit unit.

---

## Code Quality Rules

- NO placeholders (`YOUR_API_KEY`, `TODO`, `FIXME`) in committed code
- Environment variables or SQLite settings for all user configuration
- Remove unused `using` statements
- Add logging for all file system operations and errors
- Use `ILogger<T>` throughout — no `Console.WriteLine` in production code
- All public methods on Core services must have xUnit tests

---

## Anti-Hallucination Protocol

```
.NET API question?         → Check docs.microsoft.com FIRST
EF Core / SQLite question? → Use context7 to pull live docs
File system API?           → Verify against .NET 8 docs before using
Uncertain about anything?  → Say "I need to verify" and check

NEVER:
  - Invent method signatures
  - Assume API behavior without verification
  - Use deprecated APIs (e.g. old NotifyIcon patterns)
```

### Mandatory Confidence Levels

| Level | Meaning | Required Action |
|-------|---------|----------------|
| **HIGH** | Verified against docs | Can proceed |
| **MEDIUM** | Based on training, not verified | Flag it; verify before use |
| **LOW** | Uncertain, educated guess | Must verify before any use |
| **UNKNOWN** | Cannot determine | State explicitly, do not guess |

---

## Custom Subagents

```
.claude/agents/
├── cache-path-validator.md   # Verifies cache paths exist on a test machine
├── test-writer.md            # Generates xUnit tests for a given service class
└── wix-builder.md            # Handles WiX installer config (isolated — complex)
```

---

## Session Management

### Starting
1. CLAUDE.md and MEMORY.md auto-load
2. Run `dotnet build` — record any errors in Build State
3. Check "Recalled X memories" for Session Memory
4. Trust the loaded context — don't re-explore files unnecessarily

### During /compact
Always preserve:
- Modified files with paths
- Current git branch and uncommitted changes
- Pending tasks from Build Progress
- Key architecture decisions made this session

Compact proactively:
```
/compact focus on <current task>, drop <stale debugging/exploration>
```

### Rewind Strategy
Prefer rewind over repeated correction. If Claude goes wrong:
1. `Esc Esc` to rewind to just after useful file reads
2. Re-prompt with what you learned

Two failed corrections rule: if corrected twice on the same problem,
run `/clear` and rewrite the prompt from scratch.

### Ending
```
Update the Build Progress and Build State sections in CLAUDE.md, then stop.
```

---

## GitHub Repository

**Repo**: https://github.com/okalangkenneth/slateclean

### Rules
- Create the GitHub repo at the very start — not at the end
- Commit and push at the end of every phase
- Keep commit messages descriptive — they appear on the public portfolio
- Never push `.env`, API keys, or `*.user` files

---

## Corrections Log

| Date | Mistake | Rule |
|------|---------|------|
| | | |