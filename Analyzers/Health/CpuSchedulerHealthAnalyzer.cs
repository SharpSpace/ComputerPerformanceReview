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
                    ? $"Öppna Aktivitetshanteraren → högerklicka på {topProc.Name} → Avsluta aktivitet. Om det händer ofta, avinstallera/uppdatera programmet."
                    : "Öppna Aktivitetshanteraren (Ctrl+Shift+Esc) och sortera på CPU för att se vilken process som orsakar lasten.";

                events.Add(new MonitorEvent(
                    DateTime.Now, "CpuSpike",
                    $"CPU-spik: {current.CpuPercent:F0}% i {_consecutiveHighCpu * 3} sekunder{procInfo}",
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
                    $"DPC-storm: {current.DpcTimePercent:F1}% DPC-tid + {current.InterruptTimePercent:F1}% interrupt-tid",
                    isCritical ? "Critical" : "Warning",
                    "Hög DPC-tid indikerar ett drivrutinsproblem (vanligast: nätverk, GPU, USB, lagring). "
                    + "ÅTGÄRDER: 1) Uppdatera ALLA drivrutiner via tillverkarens hemsida. "
                    + "2) Kör 'LatencyMon' (gratis) för att identifiera exakt vilken drivrutin. "
                    + "3) Prova koppla bort USB-enheter en åt gången för att isolera problemet. "
                    + "4) Kontrollera att BIOS/firmware är uppdaterad."));
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
                    $"Scheduler-trängsel: CPU {current.CpuPercent:F0}% men processorkö = {current.ProcessorQueueLength} (borde vara < {2 * cores})",
                    isCritical ? "Critical" : "Warning",
                    $"Processorns kö är lång ({current.ProcessorQueueLength}) trots låg CPU ({current.CpuPercent:F0}%). "
                    + "Trådar väntar på CPU-tid. Trolig orsak: för många aktiva processer eller en process som blockerar schedulern. "
                    + "Kontrollera processprioriteringar i Aktivitetshanteraren och minska antalet aktiva program."));
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
                        $"CPU-throttling: {current.CpuClockMHz:F0}/{current.CpuMaxClockMHz:F0} MHz ({ratio * 100:F0}%) vid {current.CpuPercent:F0}% last",
                        ratio < 0.4 ? "Critical" : "Warning",
                        "Processorn går med låg frekvens under belastning. Kontrollera temperaturer, strömplan, BIOS-inställningar och kylning."));
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
            hint = "Drivrutinsproblem: hög DPC-tid indikerar en drivrutin som blockerar processorn";
        else if (current.CpuMaxClockMHz > 0 && current.CpuClockMHz / current.CpuMaxClockMHz < 0.6 && current.CpuPercent > 40)
            hint = "CPU-throttling: låg klockfrekvens under belastning";
        else if (healthScore < 50)
            hint = "CPU-mättnad eller scheduler-trängsel";

        return new HealthAssessment(
            new HealthScore(Domain, healthScore, confidence, hint),
            events);
    }
}
