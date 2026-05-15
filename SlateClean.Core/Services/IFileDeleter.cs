namespace SlateClean.Core.Services;

public interface IFileDeleter
{
    void Delete(string path);
}

public class FileSystemDeleter : IFileDeleter
{
    public void Delete(string path) => File.Delete(path);
}
