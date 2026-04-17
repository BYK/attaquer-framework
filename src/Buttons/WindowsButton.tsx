import "./style.css";
import { Component } from "solid-js";
import { GlazeWmOutput } from "zebar";
import { useAnimatedClick } from "../hooks/useAnimatedClick";
import { scriptPath } from "../scripts";

interface WindowsButtonProps {
  glazewm: GlazeWmOutput;
}

const WindowsButton: Component<WindowsButtonProps> = (props) => {
  const { isActive, handleClick } = useAnimatedClick();

  const handleWindowsClick = () => {
    handleClick();
    props.glazewm.runCommand(
      `shell-exec ${scriptPath("OpenStartMenu.vbs")}`,
    );
  };
  return (
    <button
      class={`logo ${isActive() ? "clicked-animated" : ""}`}
      onClick={handleWindowsClick}
    >
      <span class="content"></span>
    </button>
  );
};

export default WindowsButton;
