using System.Runtime.InteropServices;
using System.Text;

namespace ComputerPerformanceReview.Helpers;

/// <summary>
/// Determines which process started a given PID and which user account owns it.
///
/// Three algorithms are attempted in order; the first to succeed wins:
///   1. NtQueryInformationProcess  – fastest, pure native API, no WMI overhead
///   2. WMI Win32_Process          – standard managed approach, also resolves user via GetOwner()
///   3. ToolHelp32 Snapshot        – kernel snapshot API, always available, user via WMI fallback
/// </summary>
public static class ProcessOriginHelper
{
    public static ProcessOriginInfo? GetOrigin(int pid)
    {
        return TryNtQuery(pid)
            ?? TryWmi(pid)
            ?? TryToolHelp32(pid);
    }

    // ── Algorithm 1: NtQueryInformationProcess ───────────────────────────────

    private static ProcessOriginInfo? TryNtQuery(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            var info = new NativeMethods.ProcessBasicInformation();
            int status = NativeMethods.NtQueryInformationProcess(
                process.Handle,
                processInformationClass: 0,   // ProcessBasicInformation
                ref info,
                Marshal.SizeOf(info),
                out _);

            if (status != 0)
                return null;

            int parentPid = info.InheritedFromUniqueProcessId.ToInt32();
            string? parentName = TryGetProcessName(parentPid);
            var (user, domain) = TryGetUserNative(process.Handle);

            return new ProcessOriginInfo(parentPid, parentName, user, domain, "NtQueryInformationProcess");
        }
        catch
        {
            return null;
        }
    }

    // ── Algorithm 2: WMI Win32_Process ──────────────────────────────────────

    private static ProcessOriginInfo? TryWmi(int pid)
    {
        try
        {
            // Parent PID is a plain property — use the existing WmiHelper
            var rows = WmiHelper.Query(
                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}");

            if (rows.Count == 0)
                return null;

            int parentPid = WmiHelper.GetValue<int>(rows[0], "ParentProcessId");
            string? parentName = TryGetProcessName(parentPid);

            // User requires InvokeMethod("GetOwner") — handle separately
            var (user, domain) = TryGetUserWmi(pid);

            return new ProcessOriginInfo(parentPid, parentName, user, domain, "WMI");
        }
        catch
        {
            return null;
        }
    }

    // ── Algorithm 3: ToolHelp32 ──────────────────────────────────────────────

    private static ProcessOriginInfo? TryToolHelp32(int pid)
    {
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return null;

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32W>()
            };

            if (!NativeMethods.Process32FirstW(snapshot, ref entry))
                return null;

            do
            {
                if (entry.th32ProcessID != (uint)pid)
                    continue;

                int parentPid = (int)entry.th32ParentProcessID;
                string? parentName = TryGetProcessName(parentPid);

                // ToolHelp32 has no user info — try WMI, else leave null
                var (user, domain) = TryGetUserWmi(pid);

                return new ProcessOriginInfo(parentPid, parentName, user, domain, "ToolHelp32");
            }
            while (NativeMethods.Process32NextW(snapshot, ref entry));

            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }

    // ── User resolution: native token ────────────────────────────────────────

    private static (string? user, string? domain) TryGetUserNative(IntPtr processHandle)
    {
        IntPtr tokenHandle = IntPtr.Zero;
        try
        {
            if (!NativeMethods.TryOpenProcessToken(processHandle, out tokenHandle))
                return (null, null);

            // First call: get required buffer size
            NativeMethods.GetTokenInformation(tokenHandle, 1 /* TokenUser */,
                IntPtr.Zero, 0, out int size);

            if (size == 0)
                return (null, null);

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!NativeMethods.GetTokenInformation(tokenHandle, 1,
                        buffer, size, out _))
                    return (null, null);

                // TOKEN_USER { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; } }
                // First IntPtr in the struct is the pointer to the SID
                IntPtr sid = Marshal.ReadIntPtr(buffer);

                var name = new StringBuilder(256);
                var domainSb = new StringBuilder(256);
                int nameLen = 256, domainLen = 256;

                if (!NativeMethods.LookupAccountSid(null, sid,
                        name, ref nameLen,
                        domainSb, ref domainLen,
                        out _))
                    return (null, null);

                return (name.ToString(), domainSb.ToString());
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                NativeMethods.CloseHandle(tokenHandle);
        }
    }

    // ── User resolution: WMI GetOwner() ─────────────────────────────────────

    private static (string? user, string? domain) TryGetUserWmi(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var outParams = obj.InvokeMethod("GetOwner", null, null);
                    if (outParams == null) continue;

                    string? user = outParams["User"]?.ToString();
                    string? domain = outParams["Domain"]?.ToString();
                    return (user, domain);
                }
            }
        }
        catch { }

        return (null, null);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
