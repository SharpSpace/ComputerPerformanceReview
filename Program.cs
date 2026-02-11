ConsoleHelper.Init();

// Parse command line arguments
var mode = ParseArgs(args, out int monitorMinutes);

if (mode == AppMode.Menu)
{
    mode = ShowMenu(out monitorMinutes);
}

if (mode == AppMode.Monitor)
{
    await RunMonitorMode(monitorMinutes);
}
else
{
    await RunSnapshotMode();
}

return;

// --- Mode selection ---

static AppMode ParseArgs(string[] args, out int monitorMinutes)
{
    monitorMinutes = 10;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("--snapshot", StringComparison.OrdinalIgnoreCase))
            return AppMode.Snapshot;

        if (args[i].Equals("--monitor", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out int min) && min > 0)
                monitorMinutes = min;
            return AppMode.Monitor;
        }
    }

    return AppMode.Menu;
}

static AppMode ShowMenu(out int monitorMinutes)
{
    monitorMinutes = 10;

    ConsoleHelper.WriteHeader("DATORPRESTANDAANALYS");

    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("  Välj läge:");
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("  [1] ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("Snabbanalys");
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.WriteLine("      - Engångsanalys av systemet (~30 sek)");

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("  [2] ");
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write("Övervakning");
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.WriteLine("      - Kontinuerlig övervakning som fångar");
    Console.WriteLine("                         spikar och hängningar i realtid");
    Console.ResetColor();
    Console.WriteLine();

    Console.Write("  Välj (1/2): ");
    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.KeyChar == '1')
        {
            Console.WriteLine("1");
            return AppMode.Snapshot;
        }
        if (key.KeyChar == '2')
        {
            Console.WriteLine("2");
            Console.WriteLine();
            Console.Write("  Hur länge vill du övervaka? (minuter, standard 10): ");
            var input = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int min) && min > 0)
                monitorMinutes = min;
            return AppMode.Monitor;
        }
    }
}

// --- Snapshot mode (existing) ---

static async Task RunSnapshotMode()
{
    ConsoleHelper.WriteHeader("SNABBANALYS");
    ConsoleHelper.WriteInfo("Analyserar ditt system... Detta kan ta upp till 30 sekunder.\n");

    var analyzers = new List<IAnalyzer>
    {
        new DiskAnalyzer(),
        new SystemAnalyzer(),
        new StartupAnalyzer(),
        new MemoryAnalyzer(),
        new CpuAnalyzer(),
        new NetworkAnalyzer(),
        new GpuAnalyzer()
    };

    var reports = new List<AnalysisReport>();

    foreach (var analyzer in analyzers)
    {
        ConsoleHelper.WriteProgress($"Kör {analyzer.Name}...");
        try
        {
            var report = await analyzer.AnalyzeAsync();
            ConsoleHelper.ClearProgress();
            reports.Add(report);
            ConsoleHelper.WriteReport(report);
        }
        catch (Exception ex)
        {
            ConsoleHelper.ClearProgress();
            ConsoleHelper.WriteError($"Fel vid {analyzer.Name}: {ex.Message}");
        }
    }

    ConsoleHelper.WriteSummary(reports);

    // JSON logging and historical comparison
    int score = ConsoleHelper.CalculateScore(reports);
    var history = await LogHelper.LoadHistoryAsync();
    var currentRun = LogHelper.CreateRunLog(reports, score);

    if (history.Count > 0)
    {
        ConsoleHelper.WriteComparison(currentRun, history);
    }
    else
    {
        Console.WriteLine();
        ConsoleHelper.WriteInfo("  Första körningen - historik sparas för framtida jämförelser.");
    }

    try
    {
        var savedPath = await LogHelper.SaveRunAsync(reports, score);
        ConsoleHelper.WriteInfo($"\n  Resultat sparat till: {Path.GetFullPath(savedPath)}");
    }
    catch (Exception ex)
    {
        ConsoleHelper.WriteWarning($"Kunde inte spara logg: {ex.Message}");
    }

    Console.WriteLine();
    ConsoleHelper.WriteInfo("Tryck på valfri tangent för att avsluta...");
    Console.ReadKey(true);
}

// --- Monitor mode ---

static async Task RunMonitorMode(int durationMinutes)
{
    var monitor = new MonitorAnalyzer();
    await monitor.RunAsync(durationMinutes);

    Console.WriteLine();
    ConsoleHelper.WriteInfo("Tryck på valfri tangent för att avsluta...");
    Console.ReadKey(true);
}

// --- Enum ---

enum AppMode { Menu, Snapshot, Monitor }
