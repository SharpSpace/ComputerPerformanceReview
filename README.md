# ComputerPerformanceReview

En Windows-konsolapplikation som undersöker varför en dator blir seg, hänger sig eller fryser — trots att hårdvaran borde räcka. Programmet samlar in prestandadata via WMI, P/Invoke och .NET-diagnostik och presenterar resultat med färgkodad konsolutskrift på svenska.

## Funktioner

### ✨ Förbättrade prestandatips (Nytt!)

Programmet ger nu **konkreta, icke-tekniska åtgärdsförslag** för varje prestandaproblem:

- **DNS-latens?** → Steg-för-steg guide för att byta till 1.1.1.1 eller 8.8.8.8
- **GPU TDR-fel?** → Detaljerade instruktioner för drivrutinsuppdatering med DDU
- **Lågt diskutrymme?** → Konkreta rensningsåtgärder (cleanmgr, temp-filer, cache)
- **Energisparläge?** → Guide för att byta till Balanserad/Hög prestanda
- **Många webbläsarflikar?** → Tips om Tab Suspender och hur man hittar tunga flikar (Shift+Esc)
- **Bloatware?** → Identifierar OEM-verktyg, trial-antivirus och launcher-program

#### Nya analyser i Snabbanalys
- **Energischema** — Varnar om Power Saver på stationär/nätström
- **Installerade program** — Flaggar bloatware, flera antivirusprogram
- **Startprogram (förbättrad)** — Identifierar onödiga autostart-program

#### Nya analyser i Övervakning
- **Diskutrymme** — Kontinuerlig övervakning av ledigt utrymme per disk
- **Webbläsaranvändning** — Spårar antal processer och RAM-förbrukning
- **Energischema** — Upptäcker energisparläge under körning
- **Startprogram** — Periodisk kontroll av autostart-program

Alla tips inkluderar **ActionSteps** med svårighetsgrader (Lätt/Medel) och konkreta kommandon.

### Sysinternals-integration

Programmet integrerar automatiskt med **Microsoft Sysinternals Tools** för djupare diagnostik:

| Verktyg | När det körs | Syfte |
|---------|--------------|-------|
| **Handle.exe** | Var 30:e sekund på processer med >1000 handles | Identifierar typ av handles (File, Mutex, etc.) vid handle-läckor |
| **PoolMon.exe** | När kernel nonpaged pool >200 MB | Identifierar vilken drivrutin som läcker kernel-minne |
| **ProcDump.exe** | När process hänger sig | Skapar minnesdump för offline-analys |

**Verktyg laddas ner automatiskt från Microsoft vid behov.** De sparas i `%LocalAppData%\ComputerPerformanceReview\Sysinternals\`.

#### Handle-analys
När programmet upptäcker en handle-läcka (>1000 nya handles på 3 sek) körs Handle.exe automatiskt och ger en breakdown av handletyper:
```
Handle-läcka: chrome +2400 handles (nu: 8234) Topp-typer: File: 1850, Event: 320, Key: 180
```

#### Kernel Pool-analys
Vid hög kernel pool-användning (>200 MB nonpaged) körs PoolMon.exe för att identifiera läckande drivrutiner:
```
Kernel pool: Nonpaged pool är 245 MB Topp pool-taggar: NdiS (98 MB), Ntfs (42 MB), TcpE (31 MB)
```

#### ProcDump vid hängningar
När en process slutar svara skapas en memory dump automatiskt i `%LocalAppData%\ComputerPerformanceReview\Sysinternals\Dumps\` för vidare analys med WinDbg eller Visual Studio.

### Två lägen

| Läge | Beskrivning |
|------|-------------|
| **[1] Snabbanalys** | Engångsanalys (~30 sek) av CPU, RAM, disk, nätverk, GPU, startprogram och system. Jämför med upp till 10 tidigare körningar. |
| **[2] Övervakning** | Kontinuerlig realtidsövervakning (3 sekunders intervall) med live-dashboard som fångar spikar, hängningar och minnesexplosioner. |

### Övervakningsläge — Dashboard

Dashboarden visar i realtid:

```
╔══════════════════════════════════════════════════════════════════════╗
║            ÖVERVAKNINGSLÄGE (14:32 - 3/10 min)                     ║
╚══════════════════════════════════════════════════════════════════════╝

  CPU: ████████░░░░░░░░░░░░  38%    RAM: ████████████████░░░░  79%
  Disk: ░░░░░░░░░░░░░░░░░░░░ 0.2 kö  Net:  ░░░░░░░░░░░░░░░░░░░░ 1.2 Mbps

  ── SYSTEMHÄLSA ────────────────────────────────────────────────────
  Minnestryck:    [██████░░░░░░░░░░░░░░]   32/100  Friskt
  Systemlatens:   [██░░░░░░░░░░░░░░░░░░]    8/100  Responsivt

  Handles:  45231   PagesIn:    12/s   Commit: 8.2 GB/16.0 GB  PoolNP: 98 MB
  DPC:  0.3%  IRQ:  0.1%  CtxSw:  12340/s  ProcQueue: 0  DiskLat: R 0.2ms  W 0.1ms
  CPUClk: 3600/3600 MHz  GPU: 12%  DNS: 24 ms  StorageErr(15m): 0

  Topp CPU: chrome (12%), Teams (8%), explorer (3%)
  Topp RAM: chrome (2.1 GB), Teams (890 MB), explorer (210 MB)
  Topp I/O: System (1.2 GB), chrome (340 MB), svchost (120 MB)
  Värst disk: 0 C: D: (R 0.2ms, W 0.1ms, kö 0.1, busy 3%)
  Handle: chrome (4523 total, topp: File 2341)
  Pool:   NdiS (45 MB), Ntfs (23 MB), TcpE (18 MB)

  Hängande: Inga
```

### Händelser och tips

När programmet upptäcker problem skapas en händelse med:
- **Tidsstämpel** och allvarlighetsgrad (`KRITISKT` / `VARNING`)
- **Beskrivning** med processinformation (vilken process orsakar problemet)
- **Konkreta tips** på svenska om vad du kan göra åt det

Exempel:
```
  14:33:15  [KRITISKT]  CPU-spik: 92% i 6 sekunder (chrome 45%)
           → TIPS: Öppna Aktivitetshanteraren → högerklicka på chrome
             → Avsluta aktivitet. Om det händer ofta,
             avinstallera/uppdatera programmet.
```

### Freeze Detector

När en process slutar svara analyserar programmet systemets tillstånd och klassificerar den troliga orsaken:

| Klassificering | Indikation |
|----------------|------------|
| **Lagringsfel** | Disk-controller-fel i systemloggen (Event ID 7/51/129/153) |
| **Diskbunden** | Hög disklatens (>100ms) eller lång diskkö (>4) |
| **GPU-mättnad** | GPU >95%, UI-rendering blockeras |
| **Drivrutinsproblem** | DPC-tid >15%, en drivrutin blockerar processorn |
| **Nätverkslatens** | DNS-latens >300ms |
| **CPU-mättnad** | Processorn är fullt belastad (>80%) |
| **Minnestryck** | Memory Pressure Index >70, intensiv paging |

När CPU och disk är normala men en process ändå hänger, görs en **detaljerad sub-klassificering** baserat på per-process-data:

| Sub-klassificering | Signaler |
|--------------------|----------|
| **Tråd-pool starvation** | >100 trådar + <5% CPU — alla trådar blockerade (async deadlock) |
| **Nätverksväntan (blockerad UI-tråd)** | Aktiv I/O + nätverksaktivitet + låg CPU — synkront nätverksanrop |
| **Resurssvält (handles)** | >5000 handles + låg CPU — väntar på Windows-resurs (mutex, pipe, etc) |
| **Lås-konkurrens (contention)** | >30000 context switches/s + låg CPU — processer konkurrerar om lås |
| **Paging-väntan** | >100 page faults/s + låg CPU — minne laddas från sidfilen |
| **Drivrutinslatens (förhöjd DPC)** | DPC >5% men <15% — mikrostutter från drivrutin |
| **Scheduler-trängsel** | Processorkö > antal kärnor + låg CPU — priority inversion |
| **UI-tråd blockerad av beräkning** | 10–30% CPU — tung beräkning på UI-tråden |
| **Intern blockering (deadlock/väntan)** | ~0% CPU, allt normalt — intern deadlock, COM/RPC/shell extension |

Varje klassificering inkluderar **bevisrader** som visar exakt vilka mätvärden som ledde till slutsatsen, samt konkreta åtgärdsförslag.

### Composite Scores

| Score | Formel | Syfte |
|-------|--------|-------|
| **Minnestryck-index** (0–100) | 40% commit-ratio + 35% pages-input + 25% RAM-användning | Kombinerad minnesbild istället för enskild PageFaults-tröskel |
| **Systemlatens-score** (0–100) | 30% DPC/IRQ-tid + 35% disklatens + 35% processorkö | Indikerar om systemet "hänger" trots att Task Manager ser lugnt ut |

### Alla upptäckbara händelsetyper

| Händelse | Trigger |
|----------|---------|
| CPU-spik | >80% i 6+ sek |
| CPU-throttling | Klockfrekvens <60% av max under last |
| DPC-storm | DPC-tid >15% |
| Scheduler-trängsel | Processorkö hög trots låg CPU |
| UI-hängning | Process svarar inte (med live-duration och orsaksklassificering) |
| Minnesökning | >500 MB tappat på 30 sek (med processinfo, max 1/min) |
| Hård paging-storm | PagesInput >300/s (verklig disk-paging) |
| Commit-gräns | >90% av commit limit använt |
| Kernel pool-uttömning | Nonpaged pool >200 MB |
| Disk I/O-flaskhals | Diskkö >2 i 6+ sek |
| Disk-latens | >50ms per läs/skriv |
| Lagringsfel | Event ID 7/51/129/153 i systemloggen |
| Nätverksspik | >100 Mbps i 6+ sek |
| Handle-läcka | +1000 handles per sample |
| GDI-läcka | >5000 GDI-objekt (kritiskt vid >8000) |
| Tråd-explosion | +200 trådar per sample |
| GPU-mättnad | >95% utilization |
| GPU TDR | TDR-events i systemloggen |
| DNS-latens | >300ms |
| **Lågt diskutrymme** | <15% ledigt (varning), <8% ledigt (kritiskt) |
| **Många webbläsarprocesser** | >15 browserprocesser eller >4GB RAM |
| **Energisparläge** | Power saver aktivt på stationär/nätström |
| **Många startprogram** | >15 autostart-program (varning), >25 (kritiskt) |

### Konkreta åtgärdsförslag

Alla prestandaproblem inkluderar nu **detaljerade, konkreta åtgärdsförslag** på svenska:

#### Exempel: DNS-latens
```
ÅTGÄRDER:
1) Byt DNS-server till snabbare alternativ:
   - Öppna Nätverksinställningar → Ändra adapterkonfiguration
   - Högerklicka på nätverket → Egenskaper
   - Internet Protocol Version 4 → Använd följande DNS-servrar:
     Primär: 1.1.1.1 (Cloudflare) eller 8.8.8.8 (Google)
     Sekundär: 1.0.0.1 eller 8.8.4.4
2) Kontrollera routern: Starta om, uppdatera firmware
3) Rensa DNS-cache: Kör 'ipconfig /flushdns' som admin
4) Inaktivera VPN tillfälligt för att testa
```

#### Exempel: GPU TDR (Driver Timeout)
```
ÅTGÄRDER:
1) Uppdatera GPU-drivrutinen:
   - NVIDIA: geforce.com/drivers
   - AMD: amd.com/support
   - Intel: intel.com/support
2) Använd DDU (Display Driver Uninstaller):
   - Boota i felsäkert läge
   - Kör DDU → Avinstallera drivrutin
   - Starta om → Installera ny drivrutin
3) Om nyligen uppdaterad: Rulla tillbaka via Enhetshanteraren
4) Kontrollera GPU-temperatur
5) Om överklockat: Återställ till fabriksinställningar
```

#### Exempel: Disklatens
```
ÅTGÄRDER:
1) Uppgradera till SSD för systemdisken (C:) om HDD
2) Kör diskkontroll: 'chkdsk C: /F /R' som admin
3) Kontrollera SMART-status med CrystalDiskInfo
4) Avaktivera Windows Search-indexering temporärt
5) Flytta stora spel/program till separat SSD
```

## Rapport och loggning

### Under övervakning
Vid avslut visas en fullständig rapport med:
- Genomsnitt och peak för alla mätvärden
- Detaljerade toppar (DPC, IRQ, disklatens, kernel pool, GPU m.m.)
- Alla händelser med tidsstämplar
- **REKOMMENDATIONER** — grupperade tips per händelsetyp, sorterade efter allvarlighet

### JSON-loggning
Varje körning sparas som en individuell JSON-fil:

```
Logs/
├── monitor_2025-01-15_14-30-00.json    (övervakningsrapport)
├── run_2025-01-15_14-00-00.json        (snabbanalys)
└── ...
```

Snabbanalysen jämför automatiskt med upp till 10 tidigare körningar och visar trender (bättre/sämre).

## Arkitektur

```
Program.cs                      ← Startmeny, lägesval
│
├── Snabbanalys (IAnalyzer)
│   ├── CpuAnalyzer.cs
│   ├── MemoryAnalyzer.cs
│   ├── DiskAnalyzer.cs
│   ├── NetworkAnalyzer.cs
│   ├── GpuAnalyzer.cs
│   ├── StartupAnalyzer.cs          ← Bloatware-detektion
│   ├── SystemAnalyzer.cs
│   ├── PowerPlanAnalyzer.cs        ← **NY: Energischema-analys**
│   ├── InstalledProgramsAnalyzer.cs ← **NY: Bloatware & multipla AV**
│   └── DriverAnalyzer.cs
│
├── Övervakning
│   ├── MonitorAnalyzer.cs          ← Tunnlager: loop + JSON-sparning
│   └── SystemHealthEngine.cs       ← Orkestrerar allt nedan
│       ├── MemoryHealthAnalyzer     ← WMI: minne, paging, commit, pool
│       ├── CpuSchedulerHealthAnalyzer ← WMI: CPU, DPC, IRQ, scheduler
│       ├── DiskHealthAnalyzer       ← WMI: diskkö, latens, per-disk, event log
│       ├── GpuHealthAnalyzer        ← WMI: GPU-utilization, VRAM, TDR
│       ├── NetworkLatencyHealthAnalyzer ← DNS-latens med konkreta tips
│       ├── ProcessHealthAnalyzer    ← Per-process: CPU, RAM, handles, GDI, trådar, I/O, page faults
│       ├── FreezeDetector           ← Klassificerar orsak till hängningar
│       ├── DiskSpaceHealthSubAnalyzer ← **NY: Diskutrymme & cleanup-tips**
│       ├── BrowserHealthSubAnalyzer  ← **NY: Webbläsarprocesser & RAM**
│       ├── PowerPlanHealthSubAnalyzer ← **NY: Energischema i realtid**
│       └── StartupHealthSubAnalyzer  ← **NY: Startprogram-övervakning**
│
├── Helpers/
│   ├── MonitorDisplay.cs           ← Dashboard + slutrapport (färgkodad)
│   ├── ConsoleHelper.cs            ← Formatering, poängberäkning
│   ├── LogHelper.cs                ← JSON-sparning och historik
│   ├── WmiHelper.cs                ← WMI-queries
│   ├── NativeMethods.cs            ← P/Invoke (GetGuiResources)
│   ├── ProcessExtensions.cs        ← I/O counters, page faults via NtQueryInformationProcess
│   └── SysinternalsHelper.cs       ← Laddar ner och kör Sysinternals-verktyg
│
├── Interfaces/
│   ├── IAnalyzer.cs                ← Snabbanalys-interface
│   └── IHealthSubAnalyzer.cs       ← Collect() + Analyze() för övervaknings-sub-analyzers
│
└── Models/
    ├── MonitorSample.cs            ← ~50-fälts realtidsdata + MonitorReport + JSON-kontext
    ├── MonitorSampleBuilder.cs     ← Mutabel builder → immutabelt MonitorSample
    ├── HealthScore.cs              ← Hälsopoäng per domän
    ├── HealthAssessment.cs         ← Score + events från sub-analyzer
    ├── CompositeScores.cs          ← FreezeClassification
    ├── AnalysisReport.cs           ← Snabbanalys-rapport
    ├── AnalysisResult.cs           ← Resultatrad med ActionSteps
    ├── ActionStep.cs               ← **NY: Konkret åtgärd med kommando & svårighetsgrad**
    ├── DiskSpaceInfo.cs            ← **NY: Diskutrymme per volym**
    ├── RunLog.cs                   ← Historisk körningslogg
    ├── ProcessInfo.cs              ← Processinfo (snabbanalys)
    ├── ProcessMemorySnapshot.cs    ← Minnesdetail (snabbanalys)
    └── Severity.cs                 ← Ok/Warning/Critical enum
```

## Krav

- **Windows** (WMI och P/Invoke)
- **.NET 10.0** (`net10.0-windows`)
- **System.Management** NuGet-paket (10.0.2)
- **Körs som administratör** rekommenderas (krävs för vissa WMI-queries och Event Log-läsning)

## Användning

```bash
# Startmeny
dotnet run

# Direkt snabbanalys
dotnet run -- --snapshot

# Direkt övervakning (15 minuter)
dotnet run -- --monitor 15
```

Tryck **Q** under övervakning för att avsluta och se slutrapporten.
