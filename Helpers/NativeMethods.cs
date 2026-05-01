using System.Runtime.InteropServices;
using System.Text;

namespace ComputerPerformanceReview.Helpers;

public static partial class NativeMethods
{
    private const uint GR_GDIOBJECTS = 0;
    private const uint GR_USEROBJECTS = 1;

    // ── ProcessOriginHelper: NtQueryInformationProcess ──────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    // ── ProcessOriginHelper: ToolHelp32 ─────────────────────────────────────

    public const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const IntPtr INVALID_HANDLE_VALUE = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    // ── ProcessOriginHelper: Token / user lookup ─────────────────────────────

    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LookupAccountSid(
        string? lpSystemName,
        IntPtr sid,
        StringBuilder name,
        ref int cchName,
        StringBuilder referencedDomainName,
        ref int cchReferencedDomainName,
        out int peUse);

    public static bool TryOpenProcessToken(IntPtr processHandle, out IntPtr tokenHandle)
        => OpenProcessToken(processHandle, TOKEN_QUERY, out tokenHandle);

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
