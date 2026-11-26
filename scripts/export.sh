#!/usr/bin/env bash
# Exports the Godot client for one platform and packs it for download.
# Usage: GODOT=/path/to/godot scripts/export.sh <linux|windows|macos> [version]
# Needs the Godot 4.7 .NET editor, its export templates (matching "4.7.2.stable.mono") and the .NET SDK.
# Result: build/OpenPolitics-<version>-<platform>.(tar.gz|zip). The game's data (data/base) is staged into the
# project and travels inside the .pck; the licences are added next to the game.
set -euo pipefail
cd "$(dirname "$0")/.."
: "${GODOT:?set GODOT to the Godot 4.7 .NET executable}"
platform="${1:?usage: export.sh <linux|windows|macos> [version]}"
version="${2:-dev}"
# Godot finds the .NET SDK through DOTNET_ROOT: use the one on PATH (CI), else the per-user install.
if [ -z "${DOTNET_ROOT:-}" ]; then
  if command -v dotnet >/dev/null 2>&1; then DOTNET_ROOT="$(dirname "$(readlink -f "$(command -v dotnet)")")"; else DOTNET_ROOT="$HOME/.dotnet"; fi
fi
export DOTNET_ROOT DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

case "$platform" in
  linux)   preset="Linux";   out="build/linux/OpenPolitics.x86_64" ;;
  windows) preset="Windows"; out="build/windows/OpenPolitics.exe" ;;
  macos)   preset="macOS";   out="build/macos/OpenPolitics.zip" ;;
  *) echo "unknown platform: $platform" >&2; exit 2 ;;
esac

rm -rf "build/$platform" client-godot/data
mkdir -p "build/$platform" client-godot/data
cp -r data/base client-godot/data/base

# Godot's .NET export needs a solution file next to the project; without one it only warns, skips the game's assemblies
# and writes a game that starts, shows the logo and quits. The solution is not kept in the repository, so make one here.
rm -f client-godot/OpenPolitics.Client.sln client-godot/OpenPolitics.Client.slnx
(cd client-godot && dotnet new sln -n OpenPolitics.Client --format sln >/dev/null && dotnet sln OpenPolitics.Client.sln add OpenPolitics.Client.csproj >/dev/null)
trap 'rm -f client-godot/OpenPolitics.Client.sln' EXIT

"$GODOT" --headless --path client-godot --import
"$GODOT" --headless --path client-godot --export-release "$preset" "$(pwd)/$out"

# The game's C# assemblies travel in a data_* folder next to the executable; a build without it cannot run.
if ! ls -d "build/$platform"/data_*/OpenPolitics.Client.dll >/dev/null 2>&1 && [ "$platform" != "macos" ]; then
  echo "export failed: no .NET assemblies (data_*/OpenPolitics.Client.dll) in build/$platform" >&2
  exit 1
fi

name="OpenPolitics-$version-$platform"
# Licences travel with the game: code (MIT), data (CC BY-SA 4.0), the Inter font (SIL OFL).
mkdir -p "build/$platform/licenses"
cp LICENSE "build/$platform/licenses/LICENSE-code.txt"
cp data/LICENSE "build/$platform/licenses/LICENSE-data.txt"
cp client-godot/assets/fonts/Inter-LICENSE.txt "build/$platform/licenses/LICENSE-Inter-font.txt"
case "$platform" in
  linux)
    chmod +x "build/$platform/OpenPolitics.x86_64"
    tar -C build -czf "build/$name.tar.gz" --transform "s,^$platform,OpenPolitics," "$platform"
    ;;
  windows)
    (cd build && rm -rf OpenPolitics && cp -r "$platform" OpenPolitics && zip -qr "$name.zip" OpenPolitics && rm -rf OpenPolitics)
    ;;
  macos)
    # Godot wrote OpenPolitics.zip holding OpenPolitics.app; add the licences beside the app, not inside the signed bundle.
    (cd "build/$platform" && zip -qr OpenPolitics.zip licenses && mv OpenPolitics.zip "../$name.zip")
    ;;
esac
ls -lh build/"$name".*
