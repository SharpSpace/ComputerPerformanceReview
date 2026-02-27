namespace ComputerPerformanceReview.Analyzers.Health;

public sealed class CpuSchedulerHealthAnalyzer : IHealthSubAnalyzer
{
    public string Domain => "CPU";

    // Thresholds
    private const double CpuSpikeThreshold = 80;
    private const int CpuSpikeConsecutive = 2;
    private const double DpcStormThreshold = 15;
    private const int DpcStormConsecutive = 2;
    private const double DpcCriticalThreshold = 25;

    // State
    private int _consecutiveHighCpu;
    private int _consecutiveHighDpc;
    private int _consecutiveSchedulerContention;
    private int _consecutiveClockThrottle;

    public void Collect(MonitorSampleBuilder builder)
    {
        // CPU load percentage
        try
        {
            var cpuData = WmiHelper.Query("SELECT LoadPercentage FROM Win32_Processor");
            if (cpuData.Count > 0)
                builder.CpuPercent = cpuData.Average(d => WmiHelper.GetValue<double>(d, "LoadPercentage"));
        }
        catch { }

        // DPC and Interrupt time
        try
        {
            var procData = WmiHelper.Query(
                "SELECT PercentDPCTime, PercentInterruptTime " +
                "FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
            if (procData.Count > 0)
            {
                builder.DpcTimePercent = WmiHelper.GetValue<double>(procData[0], "PercentDPCTime");
                builder.InterruptTimePercent = WmiHelper.GetValue<double>(procData[0], "PercentInterruptTime");
            }
        }
        catch { }


        // CPU clock throttling signals
        try
        {
            var cpuClock = WmiHelper.Query("SELECT CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor");
            if (cpuClock.Count > 0)
            {
                builder.CpuClockMHz = cpuClock.Average(c => WmiHelper.GetValue<double>(c, "CurrentClockSpeed"));
                builder.CpuMaxClockMHz = cpuClock.Average(c => WmiHelper.GetValue<double>(c, "MaxClockSpeed"));
            }
        }
        catch { }

        // Context switches and processor queue length
        try
        {
            var sysData = WmiHelper.Query(
                "SELECT ContextSwitchesPersec, ProcessorQueueLength " +
                "FROM Win32_PerfFormattedData_PerfOS_System");
            if (sysData.Count > 0)
            {
                builder.ContextSwitchesPerSec = WmiHelper.GetValue<double>(sysData[0], "ContextSwitchesPersec");
                builder.ProcessorQueueLength = (int)WmiHelper.GetValue<long>(sysData[0], "ProcessorQueueLength");
            }
        }
        catch { }
    }

    public HealthAssessment Analyze(MonitorSample current, IReadOnlyList<MonitorSample> history)
    {
        var events = new List<MonitorEvent>();
        int healthScore = 100;

        // 1. CPU spike detection
        if (current.CpuPercent > CpuSpikeThreshold)
        {
            _consecutiveHighCpu++;
            if (_consecutiveHighCpu == CpuSpikeConsecutive)
            {
                var topProc = current.TopCpuProcesses.FirstOrDefault();
                string procInfo = topProc is not null ? $" ({topProc.Name} {topProc.CpuPercent:F0}%)" : "";
                bool isCritical = current.CpuPercent > 95;
                healthScore -= isCritical ? 25 : 10;

                string tip = topProc is not null
                    ? $"Open Task Manager → right-click {topProc.Name} → End task. If this happens frequently, uninstall/update the program."
                    : "Open Task Manager (Ctrl+Shift+Esc) and sort by CPU to identify which process is causing the load.";

                events.Add(new MonitorEvent(
                    DateTime.Now, "CpuSpike",
                    $"CPU spike: {current.CpuPercent:F0}% for {_consecutiveHighCpu * 3} seconds{procInfo}",
                    isCritical ? "Critical" : "Warning", tip));
            }
        }
        else
        {
            _consecutiveHighCpu = 0;
        }

        // 2. DPC/Interrupt storm
        if (current.DpcTimePercent > DpcStormThreshold)
        {
            _consecutiveHighDpc++;
            if (_consecutiveHighDpc == DpcStormConsecutive)
            {
                bool isCritical = current.DpcTimePercent > DpcCriticalThreshold;
                healthScore -= isCritical ? 30 : 15;
                events.Add(new MonitorEvent(
                    DateTime.Now, "DpcStorm",
                    $"DPC storm: {current.DpcTimePercent:F1}% DPC time + {current.InterruptTimePercent:F1}% interrupt time",
                    isCritical ? "Critical" : "Warning",
                    "High DPC time indicates a driver problem (most common: network, GPU, USB, storage). "
                    + "ACTIONS: 1) Update ALL drivers via the manufacturer's website. "
                    + "2) Run 'LatencyMon' (free) to identify exactly which driver. "
                    + "3) Try disconnecting USB devices one at a time to isolate the problem. "
                    + "4) Check that BIOS/firmware is up to date."));
            }
        }
        else
        {
            _consecutiveHighDpc = 0;
        }

        // 3. Scheduler contention (CPU low but queue high)
        int cores = Environment.ProcessorCount;
        if (current.CpuPercent < 50 && current.ProcessorQueueLength > 2 * cores)
        {
            _consecutiveSchedulerContention++;
            if (_consecutiveSchedulerContention == 2)
            {
                bool isCritical = current.ProcessorQueueLength > 4 * cores;
                healthScore -= isCritical ? 20 : 10;
                events.Add(new MonitorEvent(
                    DateTime.Now, "SchedulerContention",
                    $"Scheduler contention: CPU {current.CpuPercent:F0}% but processor queue = {current.ProcessorQueueLength} (should be < {2 * cores})",
                    isCritical ? "Critical" : "Warning",
                    $"Processor queue is long ({current.ProcessorQueueLength}) despite low CPU ({current.CpuPercent:F0}%). "
                    + "Threads are waiting for CPU time. Likely cause: too many active processes or a process blocking the scheduler. "
                    + "Check process priorities in Task Manager and reduce active programs."));
            }
        }
        else
        {
            _consecutiveSchedulerContention = 0;
        }


        // 4. Thermal/power throttling (low clocks under load)
        if (current.CpuMaxClockMHz > 0)
        {
            double ratio = current.CpuClockMHz / current.CpuMaxClockMHz;
            if (current.CpuPercent > 40 && ratio < 0.6)
            {
                _consecutiveClockThrottle++;
                if (_consecutiveClockThrottle == 2)
                {
                    healthScore -= 15;
                    events.Add(new MonitorEvent(
                        DateTime.Now, "CpuThrottle",
                        $"CPU throttling: {current.CpuClockMHz:F0}/{current.CpuMaxClockMHz:F0} MHz ({ratio * 100:F0}%) at {current.CpuPercent:F0}% load",
                        ratio < 0.4 ? "Critical" : "Warning",
                        "CPU running at low frequency under load. Check temperatures, power plan, BIOS settings, and cooling."));
                }
            }
            else
            {
                _consecutiveClockThrottle = 0;
            }
        }

        healthScore = Math.Clamp(healthScore, 0, 100);
        double confidence = history.Count >= 3 ? 1.0 : history.Count / 3.0;

        string? hint = null;
        if (current.DpcTimePercent > DpcStormThreshold)
            hint = "Driver problem: high DPC time indicates a driver blocking the processor";
        else if (current.CpuMaxClockMHz > 0 && current.CpuClockMHz / current.CpuMaxClockMHz < 0.6 && current.CpuPercent > 40)
            hint = "CPU throttling: low clock frequency under load";
        else if (healthScore < 50)
            hint = "CPU saturation or scheduler contention";

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
