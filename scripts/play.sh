#!/usr/bin/env bash
# Starts OpenPolitics: builds the Godot client when needed, then runs it. Override GODOT to use another Godot 4.7 .NET binary.
set -e
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
GODOT="${GODOT:-$HOME/.local/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64}"
dotnet build client-godot -v quiet -nologo
exec "$GODOT" --path client-godot "$@"
