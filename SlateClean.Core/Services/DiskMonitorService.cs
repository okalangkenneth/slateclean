using Microsoft.Extensions.Logging;

namespace SlateClean.Core.Services;

public class DiskMonitorService
{
    private readonly ILogger<DiskMonitorService> _logger;

    public DiskMonitorService(ILogger<DiskMonitorService> logger)
    {
        _logger = logger;
    }

    public long GetFreeBytes(string driveRoot)
    {
        var drive = new DriveInfo(driveRoot);
        return drive.AvailableFreeSpace;
    }
}
