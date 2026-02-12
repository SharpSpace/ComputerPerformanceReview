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
}
