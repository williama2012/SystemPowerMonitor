# SystemPowerMonitor

A WinUI 3 desktop app that **displays and logs CPU and GPU load and power consumption** in real time. It samples the hardware at 1 Hz, renders a live CPU panel plus one panel per GPU, and can stream every sample into a MongoDB collection — **including while the window is minimized**.

Built on **.NET 10**, **WinUI 3** (Windows App SDK), and [**LibreHardwareMonitorLib**](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6.

## Features

- **CPU** — package power (Intel **RAPL**) + total load, via LibreHardwareMonitor.
- **Multi-GPU** — per-GPU core load, board (package) power, and VRAM usage, from NVIDIA **NVML** (with a D3D fallback for VRAM). Scales to any number of GPUs (verified on a dual-GeForce box: RTX 4080 SUPER + RTX 2080 SUPER).
- **Live UI** — a CPU panel and one panel per GPU, refreshed at 1 Hz.
- **Optional MongoDB logging** — every sample written as a flat document; logging keeps running **while the window is minimized**.
- **Settings** — the three MongoDB values persisted to the Windows registry (`HKCU\Software\BigDesignSolutions\SysPwrMon`).
- **Minimized-UI suspension** — while minimized, sampling + logging keep ticking but the UI render is suspended (WinUI 3's `INotifyPropertyChanged` is UI-thread-only, so this is a deliberate gate, not a pause).

## Prerequisites

- **Windows 10 (1809 / build 17763) or Windows 11**, x64 (RID `win-x64`).
- **.NET 10 SDK** — the project targets `net10.0-windows10.0.26100.0`.
- **Administrator rights** — CPU package power (RAPL) is read through the **PawIO** kernel driver, whose device node is admin-only. Without elevation the RAPL power sensors read **0** (LibreHardwareMonitor does *not* throw — it just returns zeroed MSRs). This is the one non-optional requirement.
- A **MongoDB** instance — only if you want logging; display-only needs nothing.

> **Why the app runs unpackaged + elevated.** It is deliberately built **unpacked** (`WindowsPackageType=None`) and grants itself `requireAdministrator` through the Win32 `app.manifest`. A packaged/Store app **cannot** request elevation — the OS vetoes it with *"The request is not supported."* — so a local, admin-only monitor runs as a loose EXE. See the csproj comment block for the full rationale.

## Build & Run

```bash
# Build the solution (or: dotnet build SysPwrMon/SysPwrMon.csproj)
dotnet build

# Run — launch from an elevated terminal/VS so the requireAdministrator
# manifest doesn't UAC-prompt (or accept the prompt)
dotnet run --project SysPwrMon/SysPwrMon.csproj
```

- There is no deploy step — because the app is unpackaged, `dotnet run` just launches the loose EXE.
- The CPU "package" value is **Intel RAPL** and only appears on Intel hardware; on AMD it stays blank and `total_power` sums the GPUs only.
- You can also open `SystemPowerMonitor.slnx` in Visual Studio and run (as administrator).

## Configuration

Settings live in the Windows registry under **`HKCU\Software\BigDesignSolutions\SysPwrMon`** and are edited on the **Settings** page:

| Field          | Registry value | Notes                                                |
|----------------|----------------|------------------------------------------------------|
| Mongo URI      | `MongoUri`     | e.g. `mongodb://localhost:27017` — treated as a secret |
| Database name  | `DatabaseName` | the Mongo database                                  |
| Collection name| `CollectionName`| the collection logs are written to                  |

All three must be present for logging to activate; leave any blank to turn logging off (the app still displays). There is no config file and no in-app default — an absent value is meaningful ("not configured"), and the registry values are never invented.

## MongoDB Document

Each sample is one document in the configured collection (driver: `MongoDB.Driver` 3.14.0):

```json
{
  "timestamp":   ISODate("2026-11-17T14:22:03Z"),
  "total_power": 423.2,
  "cpu_load":    73.0,
  "cpu_power":   95.2,
  "gpus": [
    { "name": "NVIDIA GeForce RTX 4080 SUPER",
      "core_load": 41.0, "core_power": 240.0,
      "vram_used": 9216, "vram_load": 56.2 },
    { "name": "NVIDIA GeForce RTX 2080 SUPER",
      "core_load": 12.0, "core_power": 88.0,
      "vram_used": 3072, "vram_load": 37.5 }
  ]
}
```

- `total_power` (watts) = `cpu_power` + the sum of every `core_power`. **Null terms are dropped**; if *no* power sensor is available at all, `total_power` is `null` — not `0` (a `0 W` reading would be a false claim).
- `vram_used` is in **mebibytes**; `core_load` / `vram_load` are percentages.
- Any nullable field with no sensor (e.g. VRAM load on a vendor that lacks the counter) is stored as BSON **null**, so a reader can tell "unknown" from "zero".

## Architecture

Hexagonal / clean layout — no business logic in the XAML, every boundary behind an interface so each piece is testable in isolation:

```
Models/        immutable DTOs + the Mongo document types (the contract)
Pages/         WinUI XAML: Home (live panels), Settings, About
Services/      the boundaries, each behind an interface:
               ITelemetrySource   -> LibreHardwareMonitorSource (1 Hz sampling)
               IPowerSink         -> MongoPowerSink (channel + writer task)
               ISettingsStore     -> RegistrySettingsStore (HKCU)
App.xaml.cs    the composition root (manual composition, no DI framework)
```

**The sampling pipeline:**

```mermaid
flowchart LR
  subgraph sample["sampling thread (1 Hz)"]
    SRC[LibreHardwareMonitorSource<br/>Refresh + Read] -->|TelemetrySample| SVC[TelemetryService]
  end
  SVC -->|Enqueue| SINK[IPowerSink<br/>unbounded channel]
  SINK --> WR[writer task<br/>InsertOne] --> MG[[(MongoDB)]]
  SVC -.->|SnapshotUpdated| UI[Home<br/>UI thread]
  UI -.->|copy sample to INPC models| BIND[CpuTelemetry / GpuTelemetry]
```

- **Two-phase source** — `ITelemetrySource.Refresh()` asks the hardware to recompute its sensor cache; `Read()` copies the values into a fresh immutable `TelemetrySample`. The split exists because WinUI 3 marshals `INotifyPropertyChanged` through WinRT, so only the **UI thread** may mutate the bindable models.
- **Plain data crosses threads** — the sampling thread only ever produces immutable DTOs; the UI thread copies them into INPC-bound models via `DispatcherQueue.TryEnqueue`.
- **Logging never blocks sampling** — `MongoPowerSink` is an unbounded channel + one writer task. Sampling enqueues (non-blocking); a slow/offline Mongo makes the queue grow (a log never drops an entry) rather than stall the 1 Hz tick. A bad *config* fails fast (deactivates + raises `WriteFailed`); a *runtime* outage is surfaced per-sample via `WriteFailed` and the app keeps running.
- **Minimized UI suspension** — `MainWindow` detects minimized via Win32 `GetWindowPlacement` (WinUI 3 has no managed `IsMinimized`) and tells `TelemetryService` to suspend **UI publication only** — sampling and logging keep ticking.

## Hardware Notes & Limitations

- **CPU package power is Intel RAPL** (MSRs via the PawIO driver) — the "whole compute" watts for the CPU. AMD platforms won't report this sensor, so `cpu_power` is blank there and `total_power` sums the GPUs only.
- **GPU package power** is the board total from NVML (NVIDIA), read from the `GPU Package` (Power) sensor — *not* the `GPU Power` **load** sensor (a % of the power limit).
- **VRAM** comes from `GPU Memory Used/Free/Total` (or the D3D dedicated-memory fallback when NVML isn't present), in **mebibytes**.
- Sensor names are resolved against **LibreHardwareMonitor 0.9.6** (verified live on an Intel 13th-gen + NVIDIA NVML box). If LHM is upgraded, `LibreHardwareMonitorSource` must be re-verified against the new tag — master-branch sensor naming has drifted from the pinned version.

## Project Structure

```
SysPwrMon/
├─ App.xaml(.cs)                       composition root (App + AppWindow + NavView)
├─ MainWindow.xaml(.cs)                root window; Win32 minimized detection
├─ Pages/
│  ├─ HomePage.xaml(.cs)               live CPU + per-GPU panels
│  ├─ SettingsPage.xaml(.cs)           Mongo settings (registry)
│  └─ AboutPage.xaml(.cs)              about
├─ Models/
│  ├─ TelemetrySample.cs               cross-thread DTO (record)
│  ├─ PowerSample.cs                   Mongo document (cpu + gpus)
│  ├─ GpuSample.cs                     Mongo per-GPU document
│  ├─ CpuTelemetry.cs                  UI-bound INPC model
│  └─ GpuTelemetry.cs                  UI-bound INPC model
├─ Services/
│  ├─ ITelemetrySource.cs              Refresh()/Read() abstraction
│  ├─ LibreHardwareMonitorSource.cs    LHM source (CPU RAPL + NVML)
│  ├─ TelemetryService.cs              1 Hz tick; UI-vs-logging gate
│  ├─ IPowerSink.cs                    sink abstraction
│  ├─ MongoPowerSink.cs                channel + writer task
│  ├─ ISettingsStore.cs               persistence abstraction
│  ├─ RegistrySettingsStore.cs         HKCU settings
│  └─ AppSettings.cs                  the three settings (record)
├─ Converters/                          XAML value converters
└─ app.manifest                        requireAdministrator (RAPL elevation)
```

## Design Notes

- **Elevation + packaging are coupled.** RAPL needs admin; a packaged app can't ask for admin; therefore this is an unpackaged EXE with `requireAdministrator`. That's intentional and documented in the csproj — don't repackage without revisiting the assumption.
- **Null vs. 0 is a contract.** A missing sensor is `null`, never `0`, so a reader can't mistake "no sensor" for "drawing 0 W".
- **The registry is per the requirement** — settings live per-user in the OS, survive a reinstall, and are inspectable without digging through app data.
