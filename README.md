# PCAN CAN Monitor

Windows desktop monitor for PEAK-System PCAN-USB hardware.

The project uses PEAK's `Peak.PCANBasic.NET` NuGet package (`5.1.0.1194`) for all hardware access. The package is the managed API wrapper; PEAK's native PCAN-Basic runtime must still be installed on Windows.

Intended use is for project developments, especially on vehicles using MoTeC Data Acquisition Loggers.

## Features

- Discovers PCAN-USB channels automatically; no channel configuration is required.
- Probes common CAN bitrates by listening only, then remains in receive-only mode.
- Displays frames as raw hexadecimal data.
- Retains the most recent 5,000 frames in memory.
- Names observed identifiers from the **Identifiers** menu.
- Edits message declarations and byte-level signals from the **Identifiers** menu.
- Keeps multiple named identifier lists in one human-editable `%LOCALAPPDATA%\PcanCanMonitor\identifiers.txt` file. The original unnamed list is migrated to **SRE-7b**.
- The same file stores car-specific message and signal decoding rows; SRE-7b mappings are bootstrapped from `canManager.c` and can be edited per list.
- Exports and imports tab-separated `.pcanlog` files for offline analysis.
- Writes startup, PCAN discovery, capture, and crash diagnostics to `%LOCALAPPDATA%\PcanCanMonitor\pcan-monitor.log`.

## Build and run

```powershell
dotnet restore --configfile .\NuGet.Config
dotnet build -c Release
dotnet run -c Release
```

The attached `Peak.PCANBasic.NET` package is included in the `packages` folder. The project-local `NuGet.Config` uses that package first and retains nuget.org for its standard `NETStandard.Library` dependency.

Install the PEAK-System PCAN driver package separately so `PCANBasic.dll` is available to the application. If the DLL is not present, the application still opens and supports offline capture import, filtering, inspection, and export.

If the application closes unexpectedly, inspect `%LOCALAPPDATA%\PcanCanMonitor\pcan-monitor.log`. Logging is initialized before the WinForms UI, and the application records unhandled UI, application-domain, and background-task exceptions. If that location is not writable, the logger falls back to the application directory and then the system temporary directory; the selected path is recorded in the first log entries.

The monitor never calls a PCAN transmit API. The bitrate probe initializes a candidate receive channel, listens for traffic for a short interval, and uninitializes it before trying the next common bitrate. If no traffic is present, the interface is opened at 500 kbit/s and the UI reports that the bitrate is awaiting confirmation.

## Deliberate scope

Transmission, DBC/symbol-file decoding, CANopen, UDS, ISO-TP, CAN simulation, and other higher-layer protocol stacks are not included.
