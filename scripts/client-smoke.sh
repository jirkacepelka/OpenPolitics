#!/usr/bin/env bash
# Builds the Godot client and plays scripted quarters through every screen of every scenario (headless).
# Usage: GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64 scripts/client-smoke.sh [screenshot-dir]
# With a screenshot dir the run is windowed (needs a display) and writes a PNG of every tab.
# RES=WxH sets the window size (default 1600x900) and UISCALE=1.3 the interface scale, to check other shapes of window.
set -euo pipefail
cd "$(dirname "$0")/../client-godot"
: "${GODOT:?set GODOT to the Godot 4.7 .NET executable}"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" DOTNET_CLI_TELEMETRY_OPTOUT=1
"$GODOT" --headless --path . --build-solutions --quit
if [ $# -ge 1 ]; then
  "$GODOT" --path . --resolution "${RES:-1600x900}" -- --smoke --screenshots "$1" --res "${RES:-1600x900}" ${UISCALE:+--ui-scale $UISCALE}
else
  "$GODOT" --headless --path . -- --smoke
fi
