import { Component, createSignal, onCleanup, onMount, Show } from "solid-js";
import * as zebar from "zebar";
import "./style.css";

type ThermalSample = { ts_ms: number; temps: Record<string, number>; rpms: number[] };
type FanCalibration = { points: [number, number][]; updated_at: number };
type FanConfig = { calibration?: FanCalibration | null };
type Config = { fan?: FanConfig | null };

const BASE_URL = "http://127.0.0.1:30912/api";
const FC_UI_URL = "http://127.0.0.1:30912";
const THERMAL_POLL_MS = 2000;

// --- Cubic spline interpolation (matches FC's web/src/lib/spline.ts) ---

function cubicSplineInterpolate(points: [number, number][], x: number): number {
  if (points.length === 0) return 0;
  if (points.length === 1) return points[0][1];

  // Sort by x
  const sorted = [...points].sort((a, b) => a[0] - b[0]);
  const n = sorted.length;

  // Clamp to range
  if (x <= sorted[0][0]) return sorted[0][1];
  if (x >= sorted[n - 1][0]) return sorted[n - 1][1];

  const xs = sorted.map((p) => p[0]);
  const ys = sorted.map((p) => p[1]);

  // Compute natural cubic spline coefficients
  const h: number[] = [];
  for (let i = 0; i < n - 1; i++) h.push(xs[i + 1] - xs[i]);

  const alpha: number[] = [0];
  for (let i = 1; i < n - 1; i++) {
    alpha.push((3 / h[i]) * (ys[i + 1] - ys[i]) - (3 / h[i - 1]) * (ys[i] - ys[i - 1]));
  }

  const l = new Array(n).fill(1);
  const mu = new Array(n).fill(0);
  const z = new Array(n).fill(0);

  for (let i = 1; i < n - 1; i++) {
    l[i] = 2 * (xs[i + 1] - xs[i - 1]) - h[i - 1] * mu[i - 1];
    mu[i] = h[i] / l[i];
    z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
  }

  const b = new Array(n).fill(0);
  const c = new Array(n).fill(0);
  const d = new Array(n).fill(0);

  for (let j = n - 2; j >= 0; j--) {
    c[j] = z[j] - mu[j] * c[j + 1];
    b[j] = (ys[j + 1] - ys[j]) / h[j] - (h[j] * (c[j + 1] + 2 * c[j])) / 3;
    d[j] = (c[j + 1] - c[j]) / (3 * h[j]);
  }

  // Find the right interval
  let i = 0;
  for (let j = 0; j < n - 1; j++) {
    if (x >= xs[j] && x <= xs[j + 1]) {
      i = j;
      break;
    }
  }

  const dx = x - xs[i];
  return ys[i] + b[i] * dx + c[i] * dx * dx + d[i] * dx * dx * dx;
}

function rpmToPercent(rpm: number, calibrationPoints: [number, number][]): number {
  // Invert calibration: [duty%, rpm] -> [rpm, duty%]
  const inverted: [number, number][] = calibrationPoints.map(([duty, rpmVal]) => [rpmVal, duty]);
  const duty = cubicSplineInterpolate(inverted, rpm);
  return Math.max(0, Math.min(100, Math.round(duty)));
}

// --- Styling helpers ---

function maxTemp(temps: Record<string, number>): number | null {
  let max: number | null = null;
  for (const v of Object.values(temps)) {
    if (Number.isFinite(v) && (max === null || v > max)) max = v;
  }
  return max;
}

function tempClass(c: number): string {
  if (c < 60) return "low-usage";
  if (c < 75) return "medium-usage";
  if (c < 90) return "high-usage";
  return "extreme-usage";
}

function fanPctClass(pct: number): string {
  if (pct < 35) return "low-usage";
  if (pct < 55) return "medium-usage";
  if (pct < 80) return "high-usage";
  return "extreme-usage";
}

// --- Component ---

const ThermalStatus: Component = () => {
  const [temp, setTemp] = createSignal<number | null>(null);
  const [rpm, setRpm] = createSignal<number | null>(null);
  const [fanPct, setFanPct] = createSignal<number | null>(null);
  let calibrationPoints: [number, number][] | null = null;

  const fetchCalibration = async () => {
    try {
      const res = await fetch(`${BASE_URL}/config`, { cache: "no-store" });
      if (!res.ok) return;
      const cfg: Config = await res.json();
      const pts = cfg.fan?.calibration?.points;
      if (pts && pts.length > 1) {
        calibrationPoints = pts;
      }
    } catch { /* offline */ }
  };

  const pollThermal = async () => {
    try {
      const res = await fetch(`${BASE_URL}/thermal/history`, { cache: "no-store" });
      if (!res.ok) return;
      const samples: ThermalSample[] = await res.json();
      const latest = samples[samples.length - 1];
      if (!latest) return;
      const t = maxTemp(latest.temps);
      if (t !== null) setTemp(t);
      const currentRpm = latest.rpms?.[0] ?? 0;
      setRpm(currentRpm);
      if (calibrationPoints) {
        setFanPct(rpmToPercent(currentRpm, calibrationPoints));
      }
    } catch { /* offline */ }
  };

  const openFC = () => {
    zebar.shellExec(
      "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
      `--app=${FC_UI_URL}`,
    );
  };

  let interval: ReturnType<typeof setInterval>;
  onMount(() => {
    fetchCalibration();
    pollThermal();
    interval = setInterval(pollThermal, THERMAL_POLL_MS);
  });
  onCleanup(() => clearInterval(interval));

  return (
    <Show when={temp() !== null || fanPct() !== null || rpm() !== null}>
      <div class="thermal" onClick={openFC}>
        <Show when={fanPct() !== null}>
          <span class={`thermal-item ${fanPctClass(fanPct()!)}`}>
            <img class="i-fan-img" src="./assets/icons/icons8-fan-32.png" />
            {fanPct()!}%
          </span>
        </Show>
        <Show when={fanPct() === null && rpm() !== null}>
          <span class="thermal-item low-usage">
            <img class="i-fan-img" src="./assets/icons/icons8-fan-32.png" />
            {rpm()}
          </span>
        </Show>
        <Show when={temp() !== null}>
          <span class={`thermal-item ${tempClass(temp()!)}`}>
            <i>{"\uf2c9"}</i>
            {Math.round(temp()!)}°C
          </span>
        </Show>
      </div>
    </Show>
  );
};

export default ThermalStatus;
