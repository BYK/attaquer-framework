# Ideas

## Feature Ideas

1. **GPU/iGPU metrics** — Framework laptops with discrete GPUs (16" AMD) could show GPU usage/temp alongside CPU. You could poll this from Framework Control or a secondary source.

2. **Power draw widget** — Show real-time system wattage (package power / battery discharge rate). Framework Control likely exposes this. Great for seeing how your tweaks affect battery life in real-time.

3. **Keyboard backlight indicator/toggle** — A clickable widget to cycle Framework's keyboard backlight levels without reaching for the Fn key.

4. **Animated fan icon** — CSS-rotate the fan icon proportional to RPM/duty%. A spinning fan at 80% duty looks way cooler than a static icon.

5. **Mini sparkline graphs** — Tiny inline sparklines (last ~30 seconds) for CPU temp, fan speed, or memory. SolidJS makes reactive SVG path updates trivial.

6. **"Now Playing" album art** — The `media` provider already gives you track info. You could extract album art (if available) and show a tiny thumbnail next to the track title.

7. **Notification center badge** — Show a count badge for unread Windows notifications, click to open Action Center.

8. **Theme auto-switching** — Detect ambient light or time-of-day and auto-switch between light/dark CSS variables. Or tie it to the Windows system theme.

9. **Pomodoro/focus timer** — A click-to-start timer in the bar. GlazeWM could auto-switch to a "focus" workspace layout when active.

10. **Quick-launch bar** — A set of pinned app shortcuts (with the extracted icons) that appear on hover over an area — basically a mini dock.

## Code/DX Improvements

11. **Centralize the Framework Control API** — Right now `ThermalStatus` has its own `BASE_URL` + polling. You could extract a shared `useFrameworkControl()` hook that manages the connection, caches config, and exposes signals for temp/fan/battery/power — other widgets could consume it too.

12. **Retry failed icon extractions** — `icon-cache.ts` marks failures permanently. You could add a TTL or retry-on-next-focus so newly installed apps eventually get icons without a reload.

13. **Publish as a Zebar widget pack** — The `zpack.json` is already set up. You could publish to Zebar's community widget marketplace so other Framework laptop users can install it with one click.

## Visual Polish

14. **Smooth transitions on data changes** — `solid-transition-group` is already a dependency but isn't used much. Fade/slide transitions on widget appearance (e.g., when battery or weather first loads) would feel premium.

15. **Tooltip mini-dashboards** — On hover over CPU/Memory, show a richer tooltip with per-core temps, top processes, or a history graph.
