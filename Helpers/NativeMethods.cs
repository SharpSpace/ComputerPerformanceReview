using System.Runtime.InteropServices;

namespace ComputerPerformanceReview.Helpers;

public static partial class NativeMethods
{
    private const uint GR_GDIOBJECTS = 0;
    private const uint GR_USEROBJECTS = 1;

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(IntPtr hProcess, uint uiFlags);

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
}
