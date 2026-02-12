using System.Runtime.InteropServices;

namespace ComputerPerformanceReview.Helpers;

public static partial class NativeMethods
{
    private const uint GR_GDIOBJECTS = 0;
    private const uint GR_USEROBJECTS = 1;

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessIoCounters(IntPtr hProcess, out IoCounters counters);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out ProcessMemoryCountersEx counters, uint size);

    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessMemoryCountersEx
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    public static (int GdiObjects, int UserObjects) GetGuiResourceCounts(IntPtr processHandle)
    {
        try
        {
            uint gdi = GetGuiResources(processHandle, GR_GDIOBJECTS);
            uint user = GetGuiResources(processHandle, GR_USEROBJECTS);
            return ((int)gdi, (int)user);
        }
        catch
        {
            return (0, 0);
        }
    }

    public static bool TryGetIoCounters(IntPtr processHandle, out IoCounters counters)
    {
        try
        {
            return GetProcessIoCounters(processHandle, out counters);
        }
        catch
        {
            counters = default;
            return false;
        }
    }

    public static bool TryGetPageFaultCount(IntPtr processHandle, out long pageFaults)
    {
        pageFaults = 0;

        try
        {
            var size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>();
            var counters = new ProcessMemoryCountersEx();
            if (GetProcessMemoryInfo(processHandle, out counters, size))
            {
                pageFaults = counters.PageFaultCount;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }
}
