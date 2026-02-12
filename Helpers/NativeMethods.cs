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
}
