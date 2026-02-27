namespace ComputerPerformanceReview.Helpers;

public static class FormatHelper
{
    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int suffixIndex = 0;
        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }
        return $"{size:F1} {suffixes[suffixIndex]}";
    }

    public static string FormatBytes(double bytes) => FormatBytes((long)bytes);

    public static string FormatNumber(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000.0:F1}M",
            >= 1_000 => $"{value / 1_000.0:F1}K",
            _ => value.ToString()
        };
    }

    public static string FormatNumber(double value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000.0:F1}M",
            >= 1_000 => $"{value / 1_000.0:F1}K",
            _ => $"{value:F0}"
        };
    }
}
