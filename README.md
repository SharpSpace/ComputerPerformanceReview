# ComputerPerformanceReview

Ett WPF-verktyg för Windows som undersöker varför en dator blir seg, hänger sig eller fryser — trots att hårdvaran borde räcka. Presenterar resultat i ett modernt mörkt/ljust gränssnitt med realtidsgrafer och klickbara åtgärdskommandon.

## Skärmbild

> **Monitor** — Realtidsdashboard med rullande grafer (CPU, RAM, Diskkö, Nätverk)
> **Snapshot** — Engångsanalys med färgkodade resultatrader, recommendations och körbara kommandon

---

## Funktioner

### 📊 Monitor — Realtidsdashboard

Dashboarden uppdateras varje sekund och visar:

| Mätvärde | Detalj |
|----------|--------|
| **CPU** | Aktuell användning % |
| **RAM** | Använt / Totalt i GB |
| **Diskkö** | Avg. Disk Queue Length |
| **Nätverk** | Mbps (mottagning + sändning) |
| **Systemdetaljer** | OS-version, datornamn, uppstart |
| **Händelselogg** | Live-händelser med tidsstämpel (varningar, kritiska) |

Varje mätvärde har en **rullande 60-punktsgraf** (LiveCharts2) med distinkt färg.

### 🔍 Snapshot — Engångsanalys

Kör alla analyzers parallellt och visar resultat grupperade per kategori med:

- **Färgad svårhetsgradslinje** — grön (Ok), gul (Warning), röd (Critical)
- **Recommendation** — kort textförklaring när något är fel
- **Action Steps** — konkreta åtgärder med kommandoknappar (se nedan)
- **Filter** — dölj alla Ok-resultat och visa bara Warning/Critical

#### Copy & Run — köra kommandon direkt

Varje action step med ett kommando visar:

```
[Easy]  Open Windows Update settings
        start ms-settings:windowsupdate
                               [Copy]  [▶ Run]
```

- **[Copy]** — kopierar kommandot till urklipp
- **[▶ Run]** — öppnar `cmd.exe /k <kommando>` i ett nytt terminalfönster (stannar öppet)

#### Filter: Issues Only / Show All

Knapp i headern (synlig när analysen är klar) som växlar mellan att visa alla resultat eller bara Warning/Critical-rader. Tomma grupper döljs automatiskt.

---

## Analyzers och deras kommandon

### SYSTEM CHECKS (`SystemAnalyzer`)

| Check | Kommandon |
|-------|-----------|
| Pending restart | `shutdown.exe /r /t 60` · `shutdown.exe /a` |
| Latest Windows Update (om gammal) | `start ms-settings:windowsupdate` |

### DISK ANALYSIS (`DiskAnalyzer`)

| Check | Kommandon |
|-------|-----------|
| Diskutrymme (Warning/Critical) | `cleanmgr.exe /d C:` · `start ms-settings:storagesense` |
| Temporära filer (Warning/Critical) | `cleanmgr.exe` · `explorer.exe %TEMP%` |
| AppData\Local\pip | `pip cache purge` |
| AppData\Local\npm-cache | `npm cache clean --force` |
| AppData\Local\yarn | `yarn cache clean` |
| AppData\Local\NuGet | `dotnet nuget locals all --clear` |
| AppData\Local\conda | `conda clean --all` |
| AppData\Local\Docker | `docker system prune` |
| AppData\Local\Ollama | `ollama list` |

### POWER PLAN (`PowerPlanAnalyzer`)

| Check | Kommandon |
|-------|-----------|
| Fel energischema | `powercfg.exe /setactive SCHEME_MIN` · `powercfg.exe /setactive SCHEME_BALANCED` |

### Övriga analyzers (utan CLI-kommandon)

- **CPU** — Logisk/fysisk kärnräkning, klockfrekvens, usage
- **Memory** — RAM-användning, page file, handles, commit charge
- **Network** — Adapter, hastighet, DNS-svarstid, paketfel
- **Driver** — Drivrutiner utan digital signatur, gamla drivrutiner

---

## Arkitektur

```
ComputerPerformanceReview/
├── App.xaml                        ← Tema-bootstrap (Dark/Light baserat på OS)
├── MainWindow.xaml                 ← Shell med ContentControl för navigering
│
├── Views/
│   ├── StartupView.xaml            ← Startskärm med Monitor- och Snapshot-knappar
│   ├── MonitorView.xaml            ← Realtidsdashboard med grafer
│   └── SnapshotView.xaml           ← Analysresultat med filter + action steps
│
├── ViewModels/
│   ├── MainViewModel.cs            ← Navigering mellan vyer
│   ├── MonitorViewModel.cs         ← Live-metrics, 60p historik, LiveCharts-instanser
│   └── SnapshotViewModel.cs        ← Analysresultat, FilteredGroups, ToggleFilter
│                                      SnapshotActionItem (Copy/Run-kommandon)
│
├── Analyzers/
│   ├── IAnalyzer.cs                ← Interface: AnalyzeAsync() → AnalysisReport
│   ├── SystemAnalyzer.cs           ← Uptime, page file, pending restart, Windows Update
│   ├── CpuAnalyzer.cs              ← Kärnor, klockfrekvens, CPU-load
│   ├── MemoryAnalyzer.cs           ← RAM, page file, commit, handles
│   ├── DiskAnalyzer.cs             ← Diskutrymme, temp-filer, AppData, latens, SMART
│   ├── NetworkAnalyzer.cs          ← Adapters, hastighet, DNS-latens
│   ├── PowerPlanAnalyzer.cs        ← Energischema, rekommendation
│   └── DriverAnalyzer.cs           ← Drivrutinsvalidering
│
├── Models/
│   ├── AnalysisResult.cs           ← record: Category, CheckName, Description,
│   │                                   Severity, Recommendation, List<ActionStep>?
│   ├── AnalysisReport.cs           ← Title + List<AnalysisResult>
│   ├── ActionStep.cs               ← record: Title, CommandHint?, Difficulty?
│   └── Severity.cs                 ← enum Ok / Warning / Critical
│
├── Converters/
│   ├── PercentToColorConverter.cs  ← SeverityToColorConverter
│   │                                  BoolToVisibilityConverter (invert/empty)
│   │                                  StringNotEmptyToVisibilityConverter
│   │                                  DifficultyToColorConverter (Easy/Medium/Hard)
│   └── (övriga konverterare)
│
├── Themes/
│   ├── DarkTheme.xaml              ← GitHub Dark-inspirerat (standard)
│   ├── LightTheme.xaml             ← Ljust tema
│   └── SharedStyles.xaml           ← CardStyle, PrimaryButton, SecondaryButton m.m.
│
└── Helpers/
    ├── WmiHelper.cs                ← WMI-queries med GetValue<T>
    ├── ConsoleHelper.cs            ← FormatBytes, FormatMbps
    └── FormatHelper.cs             ← Ytterligare formateringshjälp
```

---

## Teknisk stack

| Komponent | Version |
|-----------|---------|
| **.NET** | 10.0 (`net10.0-windows10.0.19041`) |
| **WPF** | .NET 10 inbyggt |
| **CommunityToolkit.Mvvm** | 8.4.0 — `[ObservableProperty]`, `[RelayCommand]` |
| **LiveChartsCore.SkiaSharpView.WPF** | 2.0.0-rc6.1 — realtidsgrafer |
| **System.Management** | 10.0.2 — WMI-queries |
| **System.Diagnostics.PerformanceCounter** | 9.0.4 — diskmätning |

### Notering om LiveCharts och TFM

`TargetFramework` måste vara `net10.0-windows10.0.19041` (ej bara `net10.0-windows`). Med bara `windows` väljer NuGet `net462`-bygget av LiveCharts vilket orsakar CS0012-fel i WPF:s `_wpftmp.csproj`. Alla `CartesianChart`-instanser skapas i `MonitorViewModel` och exponeras som `object`-properties bundna via `ContentControl.Content` — LiveCharts-typer refereras aldrig i XAML.

---

## Krav

- **Windows 10 (19041) / Windows 11**
- **.NET 10 Runtime** (eller SDK för att bygga)
- **Administratörsbehörighet** rekommenderas — krävs för vissa WMI-queries (SMART, Event Log, pool)

## Bygga och köra

```bash
# Klona och bygg
git clone ...
cd ComputerPerformanceReview
dotnet build -c Release

# Kör
dotnet run -c Release
# eller
bin\Release\net10.0-windows10.0.19041\ComputerPerformanceReview.exe
```
