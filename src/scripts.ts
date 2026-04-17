import * as zebar from "zebar";

const widget = zebar.currentWidget();

// htmlPath is absolute, e.g. "C:\Users\BYK\.glzr\zebar\attaquer-custom\dist\index.html"
// Scripts live in dist/assets/scripts/ relative to that.
const baseDir = widget.htmlPath
  .replace(/\\/g, "/")            // normalize to forward slashes
  .replace(/\/[^/]+$/, "");       // strip filename → .../dist

const SCRIPTS_DIR = `${baseDir}/assets/scripts`;

export function scriptPath(filename: string): string {
  return `${SCRIPTS_DIR}/${filename}`;
}
