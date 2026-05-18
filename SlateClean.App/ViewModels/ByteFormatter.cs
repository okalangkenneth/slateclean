namespace SlateClean.App.ViewModels;

internal static class ByteFormatter
{
    private const double BytesPerMb = 1024d * 1024d;
    private const double BytesPerGb = BytesPerMb * 1024d;
    private const double BytesPerTb = BytesPerGb * 1024d;

    public static string Format(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        double tb = bytes / BytesPerTb;
        if (tb >= 1.0) return $"{tb:F2} TB";
        double gb = bytes / BytesPerGb;
        if (gb >= 1.0) return $"{gb:F1} GB";
        double mb = bytes / BytesPerMb;
        return $"{mb:F0} MB";
    }
}
