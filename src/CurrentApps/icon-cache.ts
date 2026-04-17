import * as zebar from "zebar";

// In-memory cache: processName → base64 data URL | "pending" | "failed"
const iconState = new Map<string, string>();

// Listeners waiting for icon resolution
const listeners = new Map<string, Array<(url: string | null) => void>>();

// PowerShell one-liner: find exe path from process name, extract its icon,
// resize to 32x32, convert to PNG, output as base64 string on stdout.
// No file system writes needed — everything stays in memory.
function buildScript(processName: string): string {
  // Escape single quotes in process name for safety
  const safe = processName.replace(/'/g, "''");
  return [
    "$ErrorActionPreference='Stop'",
    `$p=(Get-Process -Name '${safe}'|Where-Object{$_.Path}|Select-Object -First 1).Path`,
    "if(!$p){exit 1}",
    "Add-Type -AssemblyName System.Drawing",
    "$ico=[System.Drawing.Icon]::ExtractAssociatedIcon($p)",
    "if(!$ico){exit 1}",
    "$bmp=$ico.ToBitmap()",
    "$thumb=New-Object System.Drawing.Bitmap(32,32)",
    "$g=[System.Drawing.Graphics]::FromImage($thumb)",
    "$g.InterpolationMode='HighQualityBicubic'",
    "$g.DrawImage($bmp,0,0,32,32)",
    "$g.Dispose()",
    "$ms=New-Object System.IO.MemoryStream",
    "$thumb.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)",
    "$b64=[Convert]::ToBase64String($ms.ToArray())",
    "$ms.Dispose()",
    "$thumb.Dispose()",
    "$bmp.Dispose()",
    "$ico.Dispose()",
    "Write-Output $b64",
  ].join(";");
}

/**
 * Get the icon URL for a process. Returns immediately with cached value
 * or null if pending/failed. Calls onChange(url) when available.
 */
export function getProcessIcon(
  processName: string,
  onChange: (url: string | null) => void,
): string | null {
  const cached = iconState.get(processName);

  if (cached === "failed") return null;
  if (cached === "pending") {
    const list = listeners.get(processName) ?? [];
    list.push(onChange);
    listeners.set(processName, list);
    return null;
  }
  if (cached) return cached;

  // Start extraction
  iconState.set(processName, "pending");
  const list = listeners.get(processName) ?? [];
  list.push(onChange);
  listeners.set(processName, list);

  const script = buildScript(processName);

  zebar
    .shellExec("powershell", [
      "-NoProfile",
      "-NonInteractive",
      "-Command",
      script,
    ])
    .then((result) => {
      const b64 = result.stdout?.trim();
      if ((result.code === 0 || result.code === null) && b64 && b64.length > 100) {
        const dataUrl = `data:image/png;base64,${b64}`;
        iconState.set(processName, dataUrl);
        notifyListeners(processName, dataUrl);
      } else {
        console.warn(
          `[icon-cache] Failed for "${processName}": code=${result.code}, stdout=${b64?.length ?? 0} chars, stderr=${result.stderr?.trim()}`,
        );
        iconState.set(processName, "failed");
        notifyListeners(processName, null);
      }
    })
    .catch((err) => {
      console.error(`[icon-cache] shellExec error for "${processName}":`, err);
      iconState.set(processName, "failed");
      notifyListeners(processName, null);
    });

  return null;
}

function notifyListeners(processName: string, url: string | null) {
  const list = listeners.get(processName);
  if (list) {
    for (const cb of list) cb(url);
    listeners.delete(processName);
  }
}
