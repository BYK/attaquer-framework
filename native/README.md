# Attaquer Taskbar

A native Windows 11 taskbar companion for Framework laptops. It combines:

- a native GlazeWM workspace switcher
- CPU temperature and calibrated fan duty from Framework Control
- system-wide now-playing metadata and previous/play/next controls
- automatic adaptation to both the standard and new compact taskbar layouts
- an expanded workspace/media/thermal flyout with timeline seeking and persistent settings
- optional automatic GlazeWM insertion direction for wide and tall windows

It keeps the Microsoft taskbar and hosts a WPF surface inside it using
[Deskband11Lib](https://github.com/airtaxi/Deskband11Lib). The media integration
is derived from [BarPlay](https://github.com/airtaxi/BarPlay).

## Requirements

- Windows 11
- [Framework Control](https://github.com/ozturkkl/framework-control), listening
  on its default local port `30912`
- fan calibration completed in Framework Control if you want percentage rather
  than raw RPM
- [GlazeWM](https://github.com/glzr-io/glazewm) with IPC enabled if you want the
  workspace switcher or automatic tiling direction

Unlike the Zebar widget, this native process calls Framework Control directly,
so no CORS allow-list entry is required.

For the GlazeWM features, enable IPC in `%USERPROFILE%\.glzr\glazewm\config.yaml`:

```yaml
ipc:
  enabled: true
```

Attaquer Taskbar connects only to GlazeWM's local default endpoint at
`ws://127.0.0.1:6123`. It reconnects automatically when GlazeWM starts or
reloads.

## Install a CI build

1. Open the latest successful **Native taskbar companion** workflow run.
2. Download the `attaquer-taskbar-win-x64` artifact.
3. Extract it anywhere and run `install.cmd`.
4. Accept the UAC prompt. The installer trusts the build's ephemeral signing
   certificate in the machine's **Trusted People** store, installs the MSIX and
   launches Attaquer Taskbar.
5. Right-click the widget and enable **Run at startup**.

## Compact taskbar behavior

Deskband11Lib clamps the host to the taskbar's actual height. The control then
switches layout at 40 DIPs:

- standard taskbar: 32-DIP artwork and two metadata lines
- compact taskbar: 22-DIP workspace buttons, 24-DIP artwork and controls, one
  metadata line, and tighter margins
- narrow horizontal space: previous/next collapse before the play/pause button

This avoids BarPlay's fixed 36-DIP artwork plus outer margin overflowing a
roughly 32-DIP compact taskbar.

Metric labels default to **Auto**, which uses the original Attaquer-style fan
and thermometer icons on the compact taskbar and `CPU` / `FAN` text on the
standard taskbar. The flyout settings can force either style and can replace
the current values with two-minute sparklines.

## Workspaces and automatic tiling

The workspace buttons use each GlazeWM workspace's `display_name` when set,
otherwise its `name`. The focused workspace is highlighted; clicking another
button focuses it through GlazeWM IPC. If IPC is unavailable, a small `WM`
button opens setup details without affecting the thermal or media modules.

Automatic tiling direction is enabled by default and can be toggled in
Settings. Focus, movement, workspace activation and window-management events
are evaluated immediately from their GlazeWM payloads. Resize-driven workspace
updates settle for 160 ms before evaluation.

Wide windows select horizontal insertion and tall windows select vertical
insertion. A 10% aspect-ratio deadband preserves the current direction for
near-square windows, avoiding direction chatter while resizing. Workspace-list
refreshes remain separately debounced and do not delay tiling commands. All
queries, commands and events share one multiplexed IPC connection, with
exponential reconnect backoff.

The policy is our own implementation informed by the observable behavior of
[GlazeWM_AutoTile](https://github.com/aka-phrankie/GlazeWM_AutoTile) and the
reliability ideas demonstrated by
[GlazeTiler](https://github.com/Dutch-Raptor/glazetiler). No source from either
project is incorporated; GlazeTiler is GPL-3.0-only and GlazeWM_AutoTile
publishes no license.

## Modules and settings

Hover over the thermal strip for a compact two-graph preview. Click the thermal
strip, artwork or media title to open the complete flyout. It contains
workspaces and GlazeWM status, current thermal values and history, larger
now-playing artwork, timeline seeking, media controls, and a settings button.
Settings open as a separate flyout page so the panel stays compact without a
nested scrollbar.

The compact and expanded views are provided by built-in `ITaskbarModule`
implementations. Workspaces, Thermal and Media use the same lifecycle, layout
and theme contract. Additional built-ins can implement it without changing the
host. External DLL discovery is intentionally not enabled yet so the plugin API
can evolve without loading untrusted code into the taskbar process.

Settings are saved to
`%LOCALAPPDATA%\AttaquerTaskbar\settings.json` and currently include:

- automatic, icon, or text metric labels
- numeric values or compact sparklines
- Workspaces, Thermal and Media module visibility
- automatic GlazeWM tiling direction
- run at startup

## Build

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet publish native/AttaquerTaskbar/AttaquerTaskbar.csproj `
  -c Release -r win-x64 --self-contained true `
  -o native-publish
```

The build is a self-contained .NET desktop app inside a signed MSIX. It uses WPF
instead of WinUI so it also works on Insider builds where WinUI's optional
limited-access feature activation is unavailable.

## Controls

- Click a workspace button to focus that GlazeWM workspace.
- Click the CPU/fan values, artwork or media title to open the expanded flyout.
- Use the inline previous, play/pause and next buttons for the current Windows
  media session.
- Use the flyout gear or right-click menu for settings.
- Open Framework Control from the thermal flyout or right-click menu.

## Troubleshooting a silent launch

The first taskbar attachment can take up to about 15 seconds while Explorer's
layout is measured. If no widget appears after that, check whether the process
is still running:

```powershell
Get-Process AttaquerTaskbar -ErrorAction SilentlyContinue |
  Select-Object Id, StartTime, Path
```

Then inspect the startup trace:

```powershell
Get-Content "$env:LOCALAPPDATA\AttaquerTaskbar\attaquer-taskbar.log" -Tail 100
```

- `Another instance is already registered` means a previous invisible copy is
  running. End it in Task Manager, or run
  `Stop-Process -Name AttaquerTaskbar -Force`, then start the app once.
- A `Windows.ApplicationModel.LimitedAccessFeatures` error identifies an older
  WinUI build. Install the current WPF-based MSIX artifact with `install.cmd`.
- A final `Waiting for Deskband11Lib...` line with no later message means the
  process cannot measure or attach to this Windows taskbar layout.
- `GlazeWM IPC connection unavailable` does not prevent startup; it means the
  workspace switcher is waiting for GlazeWM or for `ipc.enabled: true`.
- A `WPF launch failed` or `Unhandled ... exception` line contains the startup
  failure and stack trace. Include those lines plus the Windows build from
  `winver` in a bug report.

If the process exits without recording an exception, Windows may have logged a
native crash. This command shows recent matching Application log entries:

```powershell
Get-WinEvent -FilterHashtable `
  @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-10)} |
  Where-Object Message -Match 'AttaquerTaskbar' |
  Select-Object TimeCreated, Id, ProviderName, Message |
  Format-List
```

## Compatibility boundary

Taskbar hosting is not an official Windows extensibility API. Deskband11Lib uses
a child HWND plus UI Automation measurements, so a future Windows update may
require a library update. The app contains no Explorer process injection.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for upstream attribution.
