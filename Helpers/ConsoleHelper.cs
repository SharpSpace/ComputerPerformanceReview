using System.Text;

namespace ComputerPerformanceReview.Helpers;

public static class ConsoleHelper
{
    private const int BoxWidth = 70;

    public static void Init()
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.InputEncoding = Encoding.UTF8; } catch { }
    }

    public static void WriteHeader(string text)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        Console.WriteLine("╔" + new string('═', BoxWidth) + "╗");
        var padded = text.PadLeft((BoxWidth + text.Length) / 2).PadRight(BoxWidth);
        Console.WriteLine("║" + padded + "║");
        Console.WriteLine("╚" + new string('═', BoxWidth) + "╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void WriteSectionHeader(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("── ");
        Console.Write(text.ToUpperInvariant());
        Console.Write(" ");
        var remaining = BoxWidth - text.Length - 4;
        if (remaining > 0)
            Console.Write(new string('─', remaining));
        Console.WriteLine();
        Console.ResetColor();
        Console.WriteLine();
    }

    public static void WriteResult(AnalysisResult result)
    {
        var (label, color) = result.Severity switch
        {
            Severity.Ok => ("[OK]      ", ConsoleColor.Green),
            Severity.Warning => ("[VARNING] ", ConsoleColor.Yellow),
            Severity.Critical => ("[KRITISKT]", ConsoleColor.Red),
            _ => ("[?]       ", ConsoleColor.Gray)
        };

        Console.Write("  ");
        Console.ForegroundColor = color;
        Console.Write(label);
        Console.ResetColor();
        Console.Write("  ");
        Console.WriteLine(result.Description);

        if (result.Recommendation is not null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"             → {result.Recommendation}");
            Console.ResetColor();
        }

        // Display action steps if available
        if (result.ActionSteps is { Count: > 0 })
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"             ÅTGÄRDER:");
            Console.ResetColor();
            
            for (int i = 0; i < result.ActionSteps.Count; i++)
            {
                var step = result.ActionSteps[i];
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write($"               {i + 1}. ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(step.Title);
                
                if (!string.IsNullOrEmpty(step.Difficulty))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($" [{step.Difficulty}]");
                }
                
                Console.WriteLine();
                
                if (!string.IsNullOrEmpty(step.CommandHint))
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"                  Kommando: {step.CommandHint}");
                }
                
                Console.ResetColor();
            }
        }
    }

    public static void WriteReport(AnalysisReport report)
    {
        WriteSectionHeader(report.Title);
        foreach (var result in report.Results)
        {
            WriteResult(result);
        }
    }

    public static int CalculateScore(List<AnalysisReport> reports)
    {
        var allResults = reports.SelectMany(r => r.Results).ToList();
        int criticalCount = allResults.Count(r => r.Severity == Severity.Critical);
        int warningCount = allResults.Count(r => r.Severity == Severity.Warning);
        int score = 100 - (criticalCount * 15) - (warningCount * 5);
        return Math.Max(0, Math.Min(100, score));
    }

    public static void WriteSummary(List<AnalysisReport> reports)
    {
        var allResults = reports.SelectMany(r => r.Results).ToList();
        int criticalCount = allResults.Count(r => r.Severity == Severity.Critical);
        int warningCount = allResults.Count(r => r.Severity == Severity.Warning);
        int okCount = allResults.Count(r => r.Severity == Severity.Ok);

        int score = CalculateScore(reports);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("╔" + new string('═', BoxWidth) + "╗");
        var title = "SAMMANFATTNING";
        var titlePadded = title.PadLeft((BoxWidth + title.Length) / 2).PadRight(BoxWidth);
        Console.WriteLine("║" + titlePadded + "║");
        Console.WriteLine("╠" + new string('═', BoxWidth) + "╣");
        Console.ResetColor();

        // Health score bar
        var scoreColor = score >= 70 ? ConsoleColor.Green : score >= 40 ? ConsoleColor.Yellow : ConsoleColor.Red;
        int filledBars = score / 5;
        int emptyBars = 20 - filledBars;
        string barFilled = new('█', filledBars);
        string barEmpty = new('░', emptyBars);

        Console.Write("║  Systemhälsa: ");
        Console.ForegroundColor = scoreColor;
        Console.Write($"{score}/100");
        Console.ResetColor();
        Console.Write("  [");
        Console.ForegroundColor = scoreColor;
        Console.Write(barFilled);
        Console.ResetColor();
        Console.Write(barEmpty);
        Console.Write("]");
        var barLine = $"  Systemhälsa: {score}/100  [{barFilled}{barEmpty}]";
        var barPadding = BoxWidth - barLine.Length;
        if (barPadding > 0) Console.Write(new string(' ', barPadding));
        Console.WriteLine("║");

        WriteBoxLine("");

        // Critical issues
        if (criticalCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            WriteBoxLine($"  Kritiska problem ({criticalCount}):");
            Console.ResetColor();
            int idx = 1;
            foreach (var r in allResults.Where(r => r.Severity == Severity.Critical))
            {
                var line = $"    {idx}. {r.Description}";
                if (r.Recommendation is not null)
                    line += $" - {r.Recommendation}";
                if (line.Length > BoxWidth - 2)
                    line = line[..(BoxWidth - 5)] + "...";
                WriteBoxLine(line);
                idx++;
            }
            WriteBoxLine("");
        }

        // Warnings
        if (warningCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            WriteBoxLine($"  Varningar ({warningCount}):");
            Console.ResetColor();
            int idx = 1;
            foreach (var r in allResults.Where(r => r.Severity == Severity.Warning))
            {
                var line = $"    {idx}. {r.Description}";
                if (line.Length > BoxWidth - 2)
                    line = line[..(BoxWidth - 5)] + "...";
                WriteBoxLine(line);
                idx++;
            }
            WriteBoxLine("");
        }

        if (criticalCount == 0 && warningCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            WriteBoxLine("  Inga problem hittades! Ditt system ser bra ut.");
            Console.ResetColor();
            WriteBoxLine("");
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("╚" + new string('═', BoxWidth) + "╝");
        Console.ResetColor();

        // Recommendations
        var recommendations = allResults
            .Where(r => r.Severity != Severity.Ok && r.Recommendation is not null)
            .OrderByDescending(r => r.Severity)
            .Select(r => r.Recommendation!)
            .Distinct()
            .Take(10)
            .ToList();

        if (recommendations.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  REKOMMENDATIONER:");
            Console.ResetColor();
            for (int i = 0; i < recommendations.Count; i++)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"  {i + 1}. {recommendations[i]}");
                Console.ResetColor();
            }
        }
    }

    private static void WriteBoxLine(string text)
    {
        var padded = text.Length >= BoxWidth ? text[..BoxWidth] : text.PadRight(BoxWidth);
        Console.WriteLine("║" + padded + "║");
    }

    public static void WriteProgress(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"\r  ⏳ {message}");
        Console.ResetColor();
    }

    public static void ClearProgress()
    {
        Console.Write("\r" + new string(' ', 80) + "\r");
    }

    public static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  ⚠ {message}");
        Console.ResetColor();
    }

    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ {message}");
        Console.ResetColor();
    }

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int suffixIndex = 0;
        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }
        return $"{size:F1} {suffixes[suffixIndex]}";
    }

    public static string FormatBytes(double bytes)
    {
        return FormatBytes((long)bytes);
    }

    public static void WriteComparison(RunLog current, List<RunLog> history)
    {
        if (history.Count == 0) return;

        WriteSectionHeader("HISTORISK JÄMFÖRELSE");

        // Table header
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("  {0,-20} {1,-10} {2,-10} {3,-10} {4}",
            "Körning", "Hälsa", "Kritiska", "Varningar", "Trend");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('─', 65));
        Console.ResetColor();

        // Current run first
        WriteComparisonRow(current, history.Count > 0 ? history[^1] : null, isCurrent: true);

        // Previous runs (newest first)
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var prev = i > 0 ? history[i - 1] : null;
            WriteComparisonRow(history[i], prev, isCurrent: false);
        }

        // Detail comparison against the most recent previous run
        var lastRun = history[^1];
        WriteDetailComparison(current, lastRun);
    }

    private static void WriteComparisonRow(RunLog run, RunLog? previousRun, bool isCurrent)
    {
        string timestamp = isCurrent
            ? $"→ {run.Timestamp:yyyy-MM-dd HH:mm}"
            : $"  {run.Timestamp:yyyy-MM-dd HH:mm}";

        string scoreStr = $"{run.HealthScore}/100";

        // Trend calculation
        string trend = "";
        ConsoleColor trendColor = ConsoleColor.Gray;
        if (previousRun is not null)
        {
            int diff = run.HealthScore - previousRun.HealthScore;
            if (diff > 0)
            {
                trend = $"▲ +{diff}";
                trendColor = ConsoleColor.Green;
            }
            else if (diff < 0)
            {
                trend = $"▼ {diff}";
                trendColor = ConsoleColor.Red;
            }
            else
            {
                trend = "━  0";
                trendColor = ConsoleColor.Gray;
            }
        }

        // Score color
        var scoreColor = run.HealthScore >= 70 ? ConsoleColor.Green
            : run.HealthScore >= 40 ? ConsoleColor.Yellow
            : ConsoleColor.Red;

        if (isCurrent)
            Console.ForegroundColor = ConsoleColor.White;
        else
            Console.ForegroundColor = ConsoleColor.Gray;

        Console.Write($"  {timestamp,-20} ");

        Console.ForegroundColor = scoreColor;
        Console.Write($"{scoreStr,-10} ");

        Console.ForegroundColor = run.CriticalCount > 0 ? ConsoleColor.Red : ConsoleColor.Gray;
        Console.Write($"{run.CriticalCount,-10} ");

        Console.ForegroundColor = run.WarningCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Gray;
        Console.Write($"{run.WarningCount,-10} ");

        Console.ForegroundColor = trendColor;
        Console.Write(trend);

        Console.ResetColor();
        Console.WriteLine();
    }

    private static void WriteDetailComparison(RunLog current, RunLog previous)
    {
        // Build sets of issues keyed by Category+CheckName
        var currentIssues = current.Reports
            .SelectMany(r => r.Results)
            .Where(r => r.Severity != "Ok")
            .Select(r => (Key: $"{r.Category}|{r.CheckName}", r))
            .ToList();

        var previousIssues = previous.Reports
            .SelectMany(r => r.Results)
            .Where(r => r.Severity != "Ok")
            .Select(r => (Key: $"{r.Category}|{r.CheckName}", r))
            .ToList();

        var currentKeys = currentIssues.Select(i => i.Key).ToHashSet();
        var previousKeys = previousIssues.Select(i => i.Key).ToHashSet();

        var newIssues = currentIssues.Where(i => !previousKeys.Contains(i.Key)).ToList();
        var resolvedIssues = previousIssues.Where(i => !currentKeys.Contains(i.Key)).ToList();

        if (newIssues.Count == 0 && resolvedIssues.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine();
            Console.WriteLine("  Inga förändringar sedan förra körningen.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine();

        if (newIssues.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Nya problem sedan förra körningen:");
            Console.ResetColor();
            foreach (var issue in newIssues)
            {
                var label = issue.r.Severity == "Critical" ? "KRITISKT" : "VARNING";
                Console.ForegroundColor = issue.r.Severity == "Critical" ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.Write($"    + [{label}] ");
                Console.ResetColor();
                Console.WriteLine(Truncate(issue.r.Description, 55));
            }
            Console.WriteLine();
        }

        if (resolvedIssues.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Lösta problem sedan förra körningen:");
            Console.ResetColor();
            foreach (var issue in resolvedIssues)
            {
                var label = issue.r.Severity == "Critical" ? "KRITISKT" : "VARNING";
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"    - [{label}] ");
                Console.ResetColor();
                Console.WriteLine(Truncate(issue.r.Description, 55));
            }
        }
    }

    private static string Truncate(string text, int maxLen)
    {
        return text.Length > maxLen ? text[..(maxLen - 3)] + "..." : text;
    }
}
