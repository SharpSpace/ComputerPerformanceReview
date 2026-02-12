namespace ComputerPerformanceReview.Analyzers.Health;

/// <summary>
/// Deep freeze diagnostic engine that collects detailed thread information 
/// when a process becomes unresponsive (freeze > 5 seconds).
/// Performance constraint: Analysis must complete within 200ms.
/// </summary>
public static class FreezeInvestigator
{
    private const double DominantWaitReasonThreshold = 0.6; // 60%
    private const int MinidumpThresholdSeconds = 15;

    /// <summary>
    /// Investigates a frozen process and returns a detailed freeze report.
    /// Collects thread states, wait reasons, and performs root cause analysis.
    /// </summary>
    /// <param name="processName">Name of the frozen process</param>
    /// <param name="processId">Process ID</param>
    /// <param name="freezeDuration">How long the process has been frozen</param>
    /// <param name="sample">Current system monitoring sample</param>
    /// <returns>Detailed freeze report with root cause analysis</returns>
    public static FreezeReport? Investigate(string processName, int processId, TimeSpan freezeDuration, MonitorSample sample)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            
            // Collect thread information
            var threads = process.Threads.Cast<ProcessThread>().ToList();
            int totalThreads = threads.Count;
            int runningThreads = 0;
            var waitReasonCounts = new Dictionary<string, int>();

            foreach (var thread in threads)
            {
                try
                {
                    var state = thread.ThreadState;
                    
                    if (state == System.Diagnostics.ThreadState.Running)
                    {
                        runningThreads++;
                    }
                    else if (state == System.Diagnostics.ThreadState.Wait)
                    {
                        // Get wait reason
                        string waitReason = thread.WaitReason.ToString();
                        
                        if (!waitReasonCounts.ContainsKey(waitReason))
                            waitReasonCounts[waitReason] = 0;
                        
                        waitReasonCounts[waitReason]++;
                    }
                }
                catch
                {
                    // Skip threads we can't access
                }
            }

            // Determine dominant wait reason (>60% of waiting threads)
            int waitingThreads = totalThreads - runningThreads;
            string? dominantWaitReason = null;
            
            if (waitingThreads > 0)
            {
                foreach (var kvp in waitReasonCounts)
                {
                    double percentage = (double)kvp.Value / waitingThreads;
                    if (percentage > DominantWaitReasonThreshold)
                    {
                        dominantWaitReason = kvp.Key;
                        break;
                    }
                }
            }

            // Analyze root cause
            string rootCause = AnalyzeRootCause(
                dominantWaitReason, 
                waitReasonCounts, 
                runningThreads, 
                totalThreads, 
                sample, 
                process);

            // Create minidump if freeze duration > 15 seconds
            string? miniDumpPath = null;
            MiniDumpAnalysis? miniDumpAnalysis = null;
            
            if (freezeDuration.TotalSeconds > MinidumpThresholdSeconds)
            {
                miniDumpPath = MiniDumpHelper.CreateMiniDump(process);
                if (miniDumpPath != null)
                {
                    miniDumpAnalysis = MiniDumpHelper.AnalyzeMiniDump(miniDumpPath);
                }
            }

            return new FreezeReport(
                ProcessName: processName,
                ProcessId: processId,
                FreezeDuration: freezeDuration,
                TotalThreads: totalThreads,
                RunningThreads: runningThreads,
                WaitReasonCounts: waitReasonCounts,
                DominantWaitReason: dominantWaitReason,
                LikelyRootCause: rootCause,
                MiniDumpPath: miniDumpPath,
                MiniDumpAnalysis: miniDumpAnalysis
            );
        }
        catch (Exception ex)
        {
            // If we can't investigate, return null
            Console.WriteLine($"Failed to investigate freeze for {processName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Applies heuristics to determine likely root cause of freeze.
    /// </summary>
    private static string AnalyzeRootCause(
        string? dominantWaitReason,
        Dictionary<string, int> waitReasonCounts,
        int runningThreads,
        int totalThreads,
        MonitorSample sample,
        Process process)
    {
        int waitingThreads = totalThreads - runningThreads;
        
        // Get process CPU usage
        double processCpu = GetProcessCpuPercent(process, sample);

        // Heuristic 1: Most threads waiting on Executive (>60%)
        if (dominantWaitReason == "Executive")
        {
            return "Lock contention or synchronization block";
        }

        // Heuristic 2: PageIn wait reason
        if (waitReasonCounts.ContainsKey("PageIn") && waitReasonCounts["PageIn"] > 0)
        {
            return "Memory or disk paging pressure";
        }

        // Heuristic 3: All threads waiting + low CPU
        if (runningThreads == 0 && processCpu < 20)
        {
            return "Deadlock or kernel object wait";
        }

        // Heuristic 4: High context switches + low CPU
        if (sample.ContextSwitchesPerSec > 30000 && sample.CpuPercent < 30)
        {
            return "Scheduler thrashing";
        }

        // Heuristic 5: High DPC time
        if (sample.DpcTimePercent > 15)
        {
            return "Driver or interrupt latency issue";
        }

        // Heuristic 6: Most threads waiting on UserRequest
        if (dominantWaitReason == "UserRequest")
        {
            return "Waiting for user input or I/O completion";
        }

        // Heuristic 7: FreePage wait reason
        if (waitReasonCounts.ContainsKey("FreePage") && waitReasonCounts["FreePage"] > 0)
        {
            return "Memory allocation pressure";
        }

        // Heuristic 8: VirtualMemory wait reason
        if (waitReasonCounts.ContainsKey("VirtualMemory") && waitReasonCounts["VirtualMemory"] > 0)
        {
            return "Virtual memory management delay";
        }

        // Heuristic 9: High number of threads with low activity
        if (totalThreads > 100 && runningThreads < 2)
        {
            return "Thread pool starvation or async deadlock";
        }

        // Default: mixed or unclear pattern
        if (waitingThreads > runningThreads * 2)
        {
            return "Mixed wait states - likely internal synchronization issue";
        }

        return "Undetermined - requires deeper investigation";
    }

    /// <summary>
    /// Gets the CPU percentage for a specific process from the monitoring sample.
    /// </summary>
    private static double GetProcessCpuPercent(Process process, MonitorSample sample)
    {
        // Look in TopCpuProcesses
        var procInfo = sample.TopCpuProcesses.FirstOrDefault(p => p.Pid == process.Id);
        if (procInfo != null)
            return procInfo.CpuPercent;

        // If not found, assume low CPU
        return 0;
    }
}
