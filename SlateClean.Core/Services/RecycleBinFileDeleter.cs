using Microsoft.VisualBasic.FileIO;

namespace SlateClean.Core.Services;

// Opt-in alternative to FileSystemDeleter (permanent delete). Sends each
// file to the user's Windows Recycle Bin so it can be restored from the
// shell. Used when AppSettings.SendToRecycleBin is true — selection lives
// in SettingsAwareFileDeleter, not here.
public class RecycleBinFileDeleter : IFileDeleter
{
    public void Delete(string path) =>
        FileSystem.DeleteFile(
            path,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin,
            UICancelOption.ThrowException);
}
