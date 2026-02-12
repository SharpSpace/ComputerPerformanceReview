using System.Diagnostics;
using ProcessThread = System.Diagnostics.ProcessThread;

namespace ComputerPerformanceReview.Analyzers.Health;

public enum DeepFreezeCategory
{
    SuspendedProcess,
    DriverStall,
    ExternalDependencyStall,
    LockContention,
    Deadlock,
    PagingPressure,
    MemoryPressure,
    SchedulerThrash,
    ThreadPoolStarvation,
    Unknown
}

public sealed record DeepFreezeDiagnostic(
    DeepFreezeCategory Category,
    string Description,
    double ConfidenceScore
);

public static class FreezeInvestigator
{
    private const double DominantThreshold = 0.6;
    private const int MinidumpThresholdSeconds = 15;

    public static FreezeReport? Investigate(
        string processName,
        int processId,
        TimeSpan freezeDuration,
        MonitorSample sample)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            var threads = process.Threads.Cast<ProcessThread>().ToList();

            int totalThreads = threads.Count;
            int runningThreads = 0;
            int suspendedThreads = 0;

            var waitReasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var thread in threads)
            {
                try
                {
                    switch (thread.ThreadState)
                    {
                        case System.Diagnostics.ThreadState.Running:
                            runningThreads++;
                            break;

                        case System.Diagnostics.ThreadState.Wait:
                            var reason = thread.WaitReason.ToString();

                            if (thread.WaitReason == ThreadWaitReason.Suspended)
                            {
                                suspendedThreads++;
                            }

                            waitReasonCounts.TryAdd(reason, 0);
                            waitReasonCounts[reason]++;
                            break;
                    }
                }
                catch
                {
                    // ignore inaccessible threads
                }
            }

            int waitingThreads = totalThreads - runningThreads;

            string? dominantWaitReason = null;
            if (waitingThreads > 0)
            {
                foreach (var kvp in waitReasonCounts)
                {
                    if ((double)kvp.Value / waitingThreads >= DominantThreshold)
                    {
                        dominantWaitReason = kvp.Key;
                        break;
                    }
                }
            }

            string? dumpPath = null;
            MiniDumpAnalysis? dumpAnalysis = null;

            if (freezeDuration.TotalSeconds >= MinidumpThresholdSeconds)
            {
                dumpPath = MiniDumpHelper.CreateMiniDump(process);
                if (dumpPath != null)
                    dumpAnalysis = MiniDumpHelper.AnalyzeMiniDump(dumpPath);
            }

            var diagnostic = AnalyzeRootCause(
                dominantWaitReason,
                waitReasonCounts,
                runningThreads,
                suspendedThreads,
                totalThreads,
                sample,
                process,
                dumpAnalysis);

            return new FreezeReport(
                ProcessName: processName,
                ProcessId: processId,
                FreezeDuration: freezeDuration,
                TotalThreads: totalThreads,
                RunningThreads: runningThreads,
                WaitReasonCounts: waitReasonCounts,
                DominantWaitReason: dominantWaitReason,
                LikelyRootCause: diagnostic.Description,
                MiniDumpPath: dumpPath,
                MiniDumpAnalysis: dumpAnalysis
            );
        }
        catch
        {
            return null;
        }
    }

    private static DeepFreezeDiagnostic AnalyzeRootCause(
        string? dominantWaitReason,
        Dictionary<string, int> waitReasonCounts,
        int runningThreads,
        int suspendedThreads,
        int totalThreads,
        MonitorSample sample,
        Process process,
        MiniDumpAnalysis? dump)
    {
        double processCpu = GetProcessCpuPercent(process, sample);
        int waitingThreads = totalThreads - runningThreads;

        // 1️⃣ All suspended
        if (suspendedThreads == totalThreads && totalThreads > 0)
        {
            return new(
                DeepFreezeCategory.SuspendedProcess,
                "All threads suspended – likely OS lifecycle pause or debugger intervention.",
                0.95
            );
        }

        // 2️⃣ Driver stall via dump analysis
        if (dump != null)
        {
            // Check faulting module
            if (!string.IsNullOrWhiteSpace(dump.FaultingModule) &&
                IsDriverModule(dump.FaultingModule))
            {
                return new(
                    DeepFreezeCategory.DriverStall,
                    $"Faulting module is driver-related ({dump.FaultingModule}).",
                    0.9
                );
            }

            // Check flagged modules
            if (dump.FlaggedModules.Any(IsDriverModule))
            {
                var module = dump.FlaggedModules.First(IsDriverModule);
                return new(
                    DeepFreezeCategory.DriverStall,
                    $"Driver module flagged in dump ({module}).",
                    0.9
                );
            }

            // Scan stack traces for driver modules
            if (dump.StackTraces.Any(s => ContainsDriverModule(s)))
            {
                return new(
                    DeepFreezeCategory.DriverStall,
                    "Stack trace contains GPU/driver modules.",
                    0.85
                );
            }
        }

        // 3️⃣ Lock contention
        if (dominantWaitReason == "Executive" && runningThreads > 0)
        {
            return new(
                DeepFreezeCategory.LockContention,
                "Majority waiting on Executive → synchronization contention.",
                0.75
            );
        }

        // 4️⃣ Deadlock
        if (dominantWaitReason == "Executive" &&
            runningThreads == 0 &&
            processCpu < 20)
        {
            return new(
                DeepFreezeCategory.Deadlock,
                "No running threads + Executive waits dominate → probable deadlock.",
                0.8
            );
        }

        // 5️⃣ Paging pressure
        if (waitReasonCounts.ContainsKey("PageIn") ||
            sample.PagesInputPerSec > 2000)
        {
            return new(
                DeepFreezeCategory.PagingPressure,
                "Hard paging detected – memory stall.",
                0.8
            );
        }

        // 6️⃣ Scheduler thrash
        if (sample.ContextSwitchesPerSec > 80000 &&
            sample.CpuPercent < 30)
        {
            return new(
                DeepFreezeCategory.SchedulerThrash,
                "Extremely high context switching with low CPU.",
                0.7
            );
        }

        // 7️⃣ Memory pressure
        if (waitReasonCounts.ContainsKey("FreePage") ||
            sample.MemoryPressureIndex > 80)
        {
            return new(
                DeepFreezeCategory.MemoryPressure,
                "High memory pressure detected.",
                0.7
            );
        }

        // 8️⃣ External dependency
        if (dominantWaitReason == "UserRequest" && runningThreads == 0)
        {
            return new(
                DeepFreezeCategory.ExternalDependencyStall,
                "Likely waiting on RPC/COM/I/O dependency.",
                0.65
            );
        }

        // 9️⃣ Thread starvation
        if (totalThreads > 100 && runningThreads < 2)
        {
            return new(
                DeepFreezeCategory.ThreadPoolStarvation,
                "High thread count with minimal execution.",
                0.6
            );
        }

        return new(
            DeepFreezeCategory.Unknown,
            "Cause undetermined – deeper stack analysis required.",
            0.3
        );
    }

    private static bool IsDriverModule(string module)
    {
        var m = module.ToLowerInvariant();

        return m.Contains("nvwgf2umx") ||
               m.Contains("dxgi") ||
               m.Contains("d3d") ||
               m.Contains("win32k") ||
               m.Contains("wlanapi") ||
               m.Contains("bluetooth") ||
               m.Contains("rpcrt4") ||
               m.Contains("twinapi");
    }

    private static bool ContainsDriverModule(string stack)
    {
        return IsDriverModule(stack);
    }

    private static double GetProcessCpuPercent(Process process, MonitorSample sample)
    {
        var info = sample.TopCpuProcesses.FirstOrDefault(p => p.Pid == process.Id);
        return info?.CpuPercent ?? 0;
    }
}
