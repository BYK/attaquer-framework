import "./style.css";
import { Component, createSignal, onMount, JSX } from "solid-js";
import { GlazeWmOutput } from "zebar";
import { Window } from "glazewm";
import { useAnimatedClick } from "../hooks/useAnimatedClick";
import { getProcessIcon } from "./icon-cache";
import { scriptPath } from "../scripts";

interface ApplicationProps {
  glazewm: GlazeWmOutput;
  window: Window;
}

// Static icon overrides — these take precedence over auto-extraction.
// Use for apps where the exe icon is ugly or wrong.
const STATIC_ICONS: Record<string, string> = {
  brave: "./assets/icons/icons8-brave-32.png",
  Discord: "./assets/icons/icons8-discord-new-32.png",
  explorer: "./assets/icons/icons8-file-explorer-new-32.png",
  WindowsTerminal: "./assets/icons/icons8-terminal-32.png",
  zed: "./assets/icons/zed.png",
  Zed: "./assets/icons/zed-nightly.png",
  Cursor: "./assets/icons/cursor.png",
  Code: "./assets/icons/icons8-visual-studio-code-insides-32.png",
  devenv: "./assets/icons/icons8-visual-studio-32.png",
  ApplicationFrameHost: "./assets/icons/icons8-settings-32.png",
  Spotify: "./assets/icons/icons8-spotify-32.png",
  msedgewebview2: "./assets/icons/icons8-edge-32.png",
  steamwebhelper: "./assets/icons/icons8-steam-32.png",
  Messenger: "./assets/icons/icons8-facebook-messenger-32.png",
  SystemInformer: "./assets/icons/systeminformer-32x32.png",
  MediBangPaintPro: "./assets/icons/icons8-medibang-paint-32.png",
  "Docker Desktop": "./assets/icons/icons8-docker-32.png",
  obs64: "./assets/icons/icons8-obs-32.png",
  sublime_text: "./assets/icons/icons8-sublime-text-32.png",
  FanSpeedSetting: "./assets/icons/icons8-fan-32.png",
  "7zFM": "./assets/icons/icons8-7zip-32.png",
  Obsidian: "./assets/icons/Obsidian-32.png",
  AutoHotkeyUX: "./assets/icons/AutoHotkeyUX-32.png",
  Signal: "./assets/icons/Signal-32.png",
  "Universal x86 Tuning Utility":
    "./assets/icons/Universal-x86-Tuning-Utility-32.png",
  windhawk: "./assets/icons/windhawk-32.png",
  VirtualBox: "./assets/icons/VirtualBox-32.png",
  vmware: "./assets/icons/VMware-Workstation-Pro-32.png",
  "Feather Launcher": "./assets/icons/Feather-Launcher-32.png",
  dnplayer: "./assets/icons/LDPlayer-9-32.png",
  Postman: "./assets/icons/Postman-32.png",
  rider64: "./assets/icons/rider64-32.png",
  firefox: "./assets/icons/Firefox-32.png",
};

const GENERIC_ICON = "./assets/icons/icons8-application-32.png";

const Application: Component<ApplicationProps> = (props) => {
  const { isActive, handleClick } = useAnimatedClick();

  const handleAppClick = () => {
    handleClick();
    props.glazewm.runCommand(
      `shell-exec ${scriptPath("FocusWindow.ahk")} ${props.window.handle}`,
    );
  };

  const [iconSrc, setIconSrc] = createSignal<string>(
    STATIC_ICONS[props.window.processName] ?? GENERIC_ICON,
  );

  onMount(() => {
    // If we already have a static override, skip extraction
    if (STATIC_ICONS[props.window.processName]) return;

    // Try to get a cached or freshly extracted icon
    const cached = getProcessIcon(props.window.processName, (url) => {
      if (url) setIconSrc(url);
    });
    if (cached) setIconSrc(cached);
  });

  return (
    <button
      classList={{
        element: true,
        focus: props.window.hasFocus,
        "clicked-animated": isActive(),
      }}
      title={props.window.title}
      onClick={handleAppClick}
    >
      <img src={iconSrc()} class="app-icon" />
    </button>
  );
};

export default Application;
