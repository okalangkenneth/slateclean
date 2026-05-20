using SlateClean.Core.Models;

namespace SlateClean.Core.Services;

// Dispatcher IFileDeleter — reads AppSettings.SendToRecycleBin fresh on
// every Delete call and forwards to the matching concrete implementation.
// Lives at the DI layer so CleanupService doesn't need to know that the
// deletion mechanism is settings-driven: it just calls Delete and trusts
// the registered IFileDeleter to do the right thing.
//
// Settings are read per-call so a toggle in the Settings UI applies on the
// next deletion without restarting the app — same pattern as
// DiskMonitorService's Func<AppSettings>.
public class SettingsAwareFileDeleter : IFileDeleter
{
    private readonly IFileDeleter _permanent;
    private readonly IFileDeleter _recycleBin;
    private readonly Func<AppSettings> _getSettings;

    public SettingsAwareFileDeleter(
        IFileDeleter permanent,
        IFileDeleter recycleBin,
        Func<AppSettings> getSettings)
    {
        _permanent = permanent;
        _recycleBin = recycleBin;
        _getSettings = getSettings;
    }

    public void Delete(string path)
    {
        var deleter = _getSettings().SendToRecycleBin ? _recycleBin : _permanent;
        deleter.Delete(path);
    }
}
