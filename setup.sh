#!/usr/bin/env bash
# Installs what is needed to develop and test SimuDeckParser on Linux: the .NET 10 SDK, in
# ~/.dotnet (no root needed, works on immutable distributions too), then the NuGet packages.
#
# Usage: ./setup.sh
#
# Building the Windows version from Linux needs nothing more (./build.sh windows). The app
# keeps the Encore Decks session in the Secret Service through libsecret, which desktop
# distributions ship.
set -euo pipefail
cd "$(dirname "$0")"

if [ -n "${1:-}" ]; then
    sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
    exit 0
fi

export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    echo "==> Installing the .NET 10 SDK in ~/.dotnet"
    script="$(mktemp)"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$script"
    bash "$script" --channel 10.0 --install-dir "$HOME/.dotnet"
    rm -f "$script"
fi

echo "==> Downloading the NuGet packages"
dotnet restore >/dev/null

echo
echo "Done. Add ~/.dotnet to your PATH (e.g. in ~/.bashrc):"
echo '  export PATH="$HOME/.dotnet:$PATH"'
echo "Next steps:"
echo "  dotnet run --project SimuDeckParser.App   # run a debug build"
echo "  dotnet test                               # run the tests"
echo "  ./build.sh                                # release archive in dist/"
echo "  ./install.sh                              # build and install"
