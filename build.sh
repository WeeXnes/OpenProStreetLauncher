#!/usr/bin/env bash
# Builds the OpenProStreet launcher for Windows and Linux with the .NET 9 SDK:
# runs the tests, publishes self-contained executables for both win-x64 and linux-x64
# to out/release (players don't need .NET installed), then verifies the Linux binary's CLI commands.
set -euo pipefail
cd "$(dirname "$0")"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "== dotnet $(dotnet --version)"

echo "== tests"
dotnet run --project tests/LauncherTests.csproj -c Release

echo "== publish"
rm -rf out
mkdir -p out/publish-linux out/publish-win out/release

echo "== publish linux-x64"
dotnet publish OpenProStreetLauncher.csproj -c Release -r linux-x64 -o out/publish-linux
cp out/publish-linux/OpenProStreetLauncher out/release/
chmod +x out/release/OpenProStreetLauncher

echo "== publish win-x64"
dotnet publish OpenProStreetLauncher.csproj -c Release -r win-x64 -o out/publish-win
cp out/publish-win/OpenProStreetLauncher.exe out/release/

ls -lh out/release

echo "== verify binaries"
test -f out/release/OpenProStreetLauncher.exe
test -f out/release/OpenProStreetLauncher

echo "== verify linux commands"
bin=out/release/OpenProStreetLauncher
version=$(grep -oP '<Version>\K[^<]+' OpenProStreetLauncher.csproj)
"$bin" version | grep -qx "OpenProStreet Launcher $version" || { echo "version doesn't print $version"; exit 1; }
"$bin" help | grep -q "install \[--system\]"

home=$(mktemp -d)
HOME="$home" PATH="$home/.local/bin:$PATH" "$bin" install
test -x "$home/.local/bin/openprostreet-launcher"
grep -q "^Exec=$home/.local/bin/openprostreet-launcher$" "$home/.local/share/applications/openprostreet-launcher.desktop"
test -s "$home/.local/share/icons/hicolor/256x256/apps/openprostreet-launcher.png"
HOME="$home" "$home/.local/bin/openprostreet-launcher" version | grep -q "$version"

if HOME="$home" "$home/.local/bin/openprostreet-launcher" update --force; then
    echo "update check passed"
else
    echo "no prior release on OneDev yet, skipping update check"
fi

HOME="$home" "$bin" uninstall
test ! -e "$home/.local/bin/openprostreet-launcher" && test ! -e "$home/.local/share/applications/openprostreet-launcher.desktop"
rm -rf "$home"
echo "verification passed"
