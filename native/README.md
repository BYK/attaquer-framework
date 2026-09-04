# Attaquer Taskbar

A native Windows 11 taskbar companion for Framework laptops. It combines:

- CPU temperature and calibrated fan duty from Framework Control
- system-wide now-playing metadata and previous/play/next controls
- automatic adaptation to both the standard and new compact taskbar layouts

It keeps the Microsoft taskbar and hosts a WinUI 3 surface inside it using
[Deskband11Lib](https://github.com/airtaxi/Deskband11Lib). The media integration
is derived from [BarPlay](https://github.com/airtaxi/BarPlay).

## Requirements

- Windows 11
- [Framework Control](https://github.com/ozturkkl/framework-control), listening
  on its default local port `30912`
- fan calibration completed in Framework Control if you want percentage rather
  than raw RPM

Unlike the Zebar widget, this native process calls Framework Control directly,
so no CORS allow-list entry is required.

## Install a CI build

1. Open the latest successful **Native taskbar companion** workflow run.
2. Download the `attaquer-taskbar-win-x64` artifact.
3. Extract it to a permanent directory and run `AttaquerTaskbar.exe`.
4. Right-click the widget and enable **Run at startup**.

Do not enable startup while running from a temporary download directory; the
startup entry records the executable's current absolute path.

## Compact taskbar behavior

Deskband11Lib clamps the host to the taskbar's actual height. The control then
switches layout at 40 DIPs:

- standard taskbar: 32-DIP artwork and two metadata lines
- compact taskbar: 24-DIP artwork, one metadata line, 24-DIP controls and tighter
  margins
- narrow horizontal space: previous/next collapse before the play/pause button

This avoids BarPlay's fixed 36-DIP artwork plus outer margin overflowing a
roughly 32-DIP compact taskbar.

## Build

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet publish native/AttaquerTaskbar/AttaquerTaskbar.csproj `
  -c Release -r win-x64 --self-contained true `
  -o native-publish
```

The build uses NativeAOT and bundles the Windows App SDK runtime. Keep the
published directory together; WinUI applications are not single-file binaries.

## Controls

- Click the CPU/fan values to open Framework Control.
- Use the inline previous, play/pause and next buttons for the current Windows
  media session.
- Right-click anywhere on the widget for startup, Framework Control and exit
  actions.

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
- A final `Waiting for Deskband11Lib...` line means the process cannot measure
  or attach to this Windows taskbar layout.
- A `WinUI launch failed` or `Unhandled ... exception` line contains the startup
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
