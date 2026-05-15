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

- **Runtime**: .NET 8
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
│   │   ├── CleanupService.cs      # Safe deletion logic
│   │   └── CleanupLogger.cs       # Writes to SQLite before deleting
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

### ✅ COMPLETED
<!-- Move completed items here with file paths -->

### 🔨 IN PROGRESS
<!-- Current work -->

### ❌ REMAINING

**Phase 1 — Core Engine**
- [ ] Solution scaffold (3 projects)
- [ ] CacheLocator per app (DaVinci, Premiere, AE)
- [ ] Cache size calculation
- [ ] DiskMonitorService (60s polling)
- [ ] CleanupService with safe deletion
- [ ] SQLite logging via EF Core
- [ ] System tray icon + right-click menu
- [ ] Manual "Clear Now" per app from tray menu
- [ ] Windows startup toggle

**Phase 2 — Dashboard + Auto-Clean**
- [ ] WPF dashboard window (cache sizes per app, disk usage bar)
- [ ] Threshold setting (auto-clean when free space below X GB)
- [ ] Auto-clean trigger on threshold breach
- [ ] Windows toast notifications
- [ ] Settings persistence (SQLite)
- [ ] Cleanup history view

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
| Last known clean build | — |
| Build command | `dotnet build` |
| Test command | `dotnet test` |
| Last run by Claude | — |

### Current Build Errors
```
None — greenfield project
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
// 3. Log to SQLite (path, size, timestamp, app name)
// 4. Delete
// 5. Verify deletion succeeded
// 6. Update log entry with result
```

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