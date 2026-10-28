#!/usr/bin/env bash
# Builds WeissSchwarz Simulator Tools in release mode and packages it as an AppImage in dist/:
# one file that runs on most Linux distributions, with no .NET installed and nothing to install.
#
# Usage: ./build-appimage.sh
#   -> dist/weiss_schwarz_simulator_tools-<version>-x86_64.AppImage
# appimagetool is downloaded once into ~/.cache/weiss_schwarz_simulator_tools (or $APPIMAGETOOL).
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# The SDK itself does not need ICU (some immutable distributions ship without it).
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

name="weiss_schwarz_simulator_tools"
id="weiss-schwarz-simulator-tools"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

if [ "${1:-}" = "-h" ] || [ "${1:-}" = "--help" ]; then
    sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
fi

# appimagetool: the one from $APPIMAGETOOL or the PATH, else a cached download.
tool="${APPIMAGETOOL:-$(command -v appimagetool || true)}"
if [ -z "$tool" ]; then
    tool="${XDG_CACHE_HOME:-$HOME/.cache}/$name/appimagetool-x86_64.AppImage"
    if [ ! -x "$tool" ]; then
        echo "==> Downloading appimagetool"
        mkdir -p "$(dirname "$tool")"
        curl -fL --progress-bar -o "$tool.part" \
            https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
        chmod +x "$tool.part"
        mv "$tool.part" "$tool"
    fi
fi

echo "==> Building for Linux"
appdir="dist/$name.AppDir"
rm -rf "$appdir" obj-publish
dotnet publish WeissSchwarzSimulatorTools.App -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -o obj-publish >/dev/null

echo "==> Packaging AppImage"
install -Dm755 obj-publish/WeissSchwarzSimulatorTools "$appdir/usr/bin/$name"
install -Dm644 -t "$appdir/usr/share/doc/$name" README.md LICENSE fonts/OFL.txt
rm -rf obj-publish

cat > "$appdir/AppRun" <<'APPRUN'
#!/bin/sh
here="$(dirname "$(readlink -f "$0")")"
exec "$here/usr/bin/weiss_schwarz_simulator_tools" "$@"
APPRUN
chmod 755 "$appdir/AppRun"

cat > "$appdir/$id.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=WeissSchwarz Simulator Tools
Comment=Browse cards and build decks for the Weiss Schwarz Simulator
Exec=$name
Icon=$id
Terminal=false
Categories=Game;CardGame;
DESKTOP

# The icon of the navigation rail (Material "cards-outline") on the primary colour of the theme.
cat > "$appdir/$id.svg" <<'SVG'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="56" fill="#6750A4"/>
  <path transform="translate(32 32) scale(8)" fill="#FFFFFF" d="M11.19,2.25C10.93,2.25 10.67,2.31 10.42,2.4L3.06,5.45C2.04,5.87 1.55,7.04 1.97,8.05L6.93,20C7.24,20.77 7.97,21.23 8.74,21.25C9,21.25 9.27,21.22 9.53,21.1L16.9,18.05C17.65,17.74 18.11,17 18.13,16.25C18.14,16 18.09,15.71 18,15.45L13,3.5C12.71,2.73 11.97,2.26 11.19,2.25M14.67,2.25L18.12,10.6V4.25A2,2 0 0,0 16.12,2.25M20.13,3.79V12.82L22.56,6.96C22.97,5.94 22.5,4.78 21.47,4.36M11.19,4.22L16.17,16.24L8.78,19.3L3.8,7.29"/>
</svg>
SVG
ln -sf "$id.svg" "$appdir/.DirIcon"

out="dist/$name-$version-x86_64.AppImage"
rm -f "$out"
# Extract-and-run so appimagetool works without FUSE (containers, some immutable distributions).
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream "$appdir" "$out" >/dev/null
rm -rf "$appdir"
echo "    $out"
