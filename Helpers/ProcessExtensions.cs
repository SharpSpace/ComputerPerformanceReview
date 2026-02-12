namespace ComputerPerformanceReview.Helpers;

public static class ProcessExtensions
{
    public static bool TryGetIoCounters(this Process process, out NativeMethods.IoCounters counters)
    {
        counters = default;

        try
        {
            if (process.HasExited)
                return false;

            return NativeMethods.TryGetIoCounters(process.Handle, out counters);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetPageFaultCount(this Process process, out long pageFaults)
    {
        pageFaults = 0;

        try
        {
            if (process.HasExited)
                return false;

            return NativeMethods.TryGetPageFaultCount(process.Handle, out pageFaults);
        }
        catch
        {
            return false;
        }
    }
}
