#!/usr/bin/env bash
# Builds WeissSchwarz Simulator Tools in release mode and packages it into dist/: one self-contained
# executable per system, which needs no .NET installed to run.
#
# Usage: ./build.sh [linux|windows|all]   (default: linux)
#   linux     -> dist/weiss_schwarz_simulator_tools-<version>-linux-x86_64.tar.gz
#   windows   -> dist/weiss_schwarz_simulator_tools-<version>-windows-x86_64.zip (built from Linux too)
#   all       both
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# The SDK itself does not need ICU (some immutable distributions ship without it).
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

name="weiss_schwarz_simulator_tools"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
what="${1:-linux}"

# Files shipped next to the executable (the fonts are embedded, OFL.txt is their licence).
extras=(README.md LICENSE fonts/OFL.txt)

# Publishes the app for the .NET runtime identifier $1 into $2.
publish() {
    dotnet publish WeissSchwarzSimulatorTools.App -c Release -r "$1" --self-contained \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=none -o "$2" >/dev/null
}

stage() {
    local dir="$1" rid="$2" exe="$3"
    rm -rf "$dir" obj-publish
    publish "$rid" obj-publish
    mkdir -p "$dir"
    cp "obj-publish/$exe" "$dir/"
    cp "${extras[@]}" "$dir/"
    rm -rf obj-publish
}

build_linux() {
    echo "==> Building for Linux"
    local pkg="$name-$version-linux-x86_64"
    stage "dist/$pkg" linux-x64 WeissSchwarzSimulatorTools
    mv "dist/$pkg/WeissSchwarzSimulatorTools" "dist/$pkg/$name"
    tar -C dist -czf "dist/$pkg.tar.gz" "$pkg"
    echo "    dist/$pkg.tar.gz"
}

build_windows() {
    echo "==> Building for Windows"
    local pkg="$name-$version-windows-x86_64"
    stage "dist/$pkg" win-x64 WeissSchwarzSimulatorTools.exe
    mv "dist/$pkg/WeissSchwarzSimulatorTools.exe" "dist/$pkg/$name.exe"
    rm -f "dist/$pkg.zip"
    (cd dist && zip -qr "$pkg.zip" "$pkg")
    echo "    dist/$pkg.zip"
}

case "$what" in
    linux) build_linux ;;
    windows) build_windows ;;
    all) build_linux; build_windows ;;
    -h|--help) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//' ;;
    *) echo "Unknown target: $what (expected linux, windows or all)" >&2; exit 1 ;;
esac
