namespace ComputerPerformanceReview.Tests;

/// <summary>
/// Manual test harness for validating FreezeInvestigator functionality.
/// This can be run by creating a hanging process scenario.
/// </summary>
public static class FreezeInvestigatorTests
{
    /// <summary>
    /// Tests FreezeInvestigator by analyzing the current process.
    /// This is a basic smoke test to ensure the investigator can collect thread data.
    /// </summary>
    public static void TestCurrentProcess()
    {
        Console.WriteLine("=== FreezeInvestigator Smoke Test ===\n");
        
        var currentProcess = Process.GetCurrentProcess();
        
        // Create a mock MonitorSample with minimal data
        var mockSample = new MonitorSample(
            Timestamp: DateTime.Now,
            CpuPercent: 20,
            MemoryUsedPercent: 50,
            MemoryAvailableBytes: 8_000_000_000,
            DiskQueueLength: 0.5,
            NetworkMbps: 10,
            TotalSystemHandles: 10000,
            PageFaultsPerSec: 100,
            CommittedBytes: 8_000_000_000,
            TopCpuProcesses: new List<MonitorProcessInfo>
            {
                new(currentProcess.ProcessName, currentProcess.Id, 5.0, 
                    currentProcess.WorkingSet64, 100, 0, 0, currentProcess.Threads.Count)
            },
            TopMemoryProcesses: new List<MonitorProcessInfo>(),
            TopGdiProcesses: new List<MonitorProcessInfo>(),
            TopIoProcesses: new List<MonitorIoProcessInfo>(),
            TopFaultProcesses: new List<MonitorFaultProcessInfo>(),
            DiskInstances: new List<DiskInstanceStat>(),
            HangingProcesses: new List<HangingProcessInfo>()
        );
        
        Console.WriteLine($"Testing with process: {currentProcess.ProcessName} (PID: {currentProcess.Id})");
        Console.WriteLine($"Threads: {currentProcess.Threads.Count}\n");
        
        // Test FreezeInvestigator.Investigate
        var report = FreezeInvestigator.Investigate(
            currentProcess.ProcessName,
            currentProcess.Id,
            TimeSpan.FromSeconds(6), // Simulate 6 second freeze
            mockSample
        );
        
        if (report == null)
        {
            Console.WriteLine("❌ FreezeInvestigator.Investigate returned null");
            return;
        }
        
        Console.WriteLine("✓ FreezeInvestigator.Investigate succeeded\n");
        Console.WriteLine($"Process: {report.ProcessName} (PID: {report.ProcessId})");
        Console.WriteLine($"Freeze Duration: {report.FreezeDuration.TotalSeconds:F1}s");
        Console.WriteLine($"Total Threads: {report.TotalThreads}");
        Console.WriteLine($"Running Threads: {report.RunningThreads}");
        Console.WriteLine($"Waiting Threads: {report.TotalThreads - report.RunningThreads}");
        
        if (report.WaitReasonCounts.Count > 0)
        {
            Console.WriteLine("\nWait Reasons:");
            foreach (var kvp in report.WaitReasonCounts.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value} threads");
            }
        }
        
        if (report.DominantWaitReason != null)
        {
            Console.WriteLine($"\nDominant Wait Reason: {report.DominantWaitReason}");
        }
        
        Console.WriteLine($"\nLikely Root Cause: {report.LikelyRootCause}");
        
        if (report.MiniDumpPath != null)
        {
            Console.WriteLine($"\nMiniDump created: {report.MiniDumpPath}");
        }
        else
        {
            Console.WriteLine("\nNo MiniDump created (freeze duration < 15s)");
        }
        
        Console.WriteLine("\n=== Test Completed Successfully ===");
    }
}
