# attaquer-framework

A [Zebar](https://github.com/glzr-io/zebar) widget pack for [Framework laptops](https://frame.work), based on the [attaquer](https://github.com/iAttaquer/.glzr) theme by [iAttaquer](https://github.com/iAttaquer).

Built with SolidJS + TypeScript + Vite.

## What's different from attaquer?

### Framework Control integration
Connects to [Framework Control](https://github.com/ozturkkl/framework-control)'s local API to display:
- **CPU temperature** (max across all sensors, color-coded)
- **Fan speed** as duty cycle % (using calibrated cubic spline interpolation from your fan's calibration data)
- Click either to open the Framework Control UI

### Automatic app icon extraction
Unknown apps in the taskbar automatically get their icon extracted from the running process via PowerShell + `System.Drawing`. No more maintaining a hardcoded icon map for every app you use. Icons are cached as base64 data URLs for the session.

### Other improvements
- **Battery guard** — fixes the `NaN%` bug in the original attaquer theme where the battery widget renders before data loads
- **Volume mute toggle** — clicking the speaker icon mutes/unmutes instead of opening the audio device selector
- **Weather tooltip** — hover shows condition + wind speed; click opens forecast
- **Time tooltip** — hover shows the full date
- **Dynamic script paths** — AHK/VBS scripts are resolved relative to the widget's install location, not hardcoded to a specific path

## Prerequisites

- [Zebar](https://github.com/glzr-io/zebar) v3.3.0+
- [GlazeWM](https://github.com/glzr-io/glazewm) (for workspace management features)
- [Framework Control](https://github.com/ozturkkl/framework-control) (for CPU temp, fan speed, battery via API)
- [AutoHotkey](https://www.autohotkey.com/) (for some button actions — search, start menu, focus window)

## Framework Control setup

The widget polls Framework Control's local API. You need to allow CORS from Zebar's webview origin.

1. Find the WinSW config file at `C:\Program Files\FrameworkControl\FrameworkControlService.xml`
2. Edit the `FRAMEWORK_CONTROL_ALLOWED_ORIGINS` env tag to include Zebar's origin:
   ```xml
   <env name="FRAMEWORK_CONTROL_ALLOWED_ORIGINS" value="...,http://127.0.0.1:6124" />
   ```
3. Restart the service: `Restart-Service FrameworkControlService`

**Note:** The port (`30912` by default) is baked into the Framework Control binary. Check `FrameworkControlService.xml` for the actual port, and update `BASE_URL` in `src/ThermalStatus/ThermalStatus.tsx` if different.

Fan speed % requires running the fan calibration wizard in Framework Control's web UI first. Without calibration, raw RPM is displayed instead.

## Install

```bash
# Clone into Zebar's widget pack directory
cd ~/.glzr/zebar
git clone https://github.com/BYK/attaquer-framework.git

# Install dependencies and build
cd attaquer-framework
npm install
npm run build
```

Then in Zebar's GUI, enable the `attaquer-framework` widget pack and select a preset (`1080p` for 26px bar, `1440p` for 32px bar).

## Development

```bash
npm install
npm run build
# Output goes to dist/ — Zebar serves from there
```

After changes, rebuild and reload the widget in Zebar (right-click tray icon).

## Credits

- Original [attaquer](https://github.com/iAttaquer/.glzr) theme by [iAttaquer](https://github.com/iAttaquer)
- [Framework Control](https://github.com/ozturkkl/framework-control) by [ozturkkl](https://github.com/ozturkkl)
- [Zebar](https://github.com/glzr-io/zebar) by [glzr-io](https://github.com/glzr-io)
- App icons extracted at runtime via Windows `System.Drawing` API
- Static app icons from [Icons8](https://icons8.com/)
- [Nerd Fonts](https://www.nerdfonts.com/) for glyph icons

## License

[MIT](LICENSE) — original attaquer theme by iAttaquer, Framework-specific additions by BYK.
