#!/bin/bash
# Build the Windows installer (out/RawConverter-<version>-win-x64-setup.exe) on Linux, using Inno Setup in Docker.
# Publishes the Windows build first, and downloads Microsoft's Visual C++ runtime installer into
# packaging/windows/cache (not committed).
# The version comes from Directory.Build.props unless VERSION is set (the release workflow sets it from the tag).
set -euo pipefail
cd "$(dirname "$0")/../.."

version=${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)}
out=out/RawConverter-win-x64
cache=packaging/windows/cache

rm -rf "$out"
for project in src/RawConverter.Cli/RawConverter.Cli.csproj src/RawConverter.Gui/RawConverter.Gui.csproj; do
    dotnet publish "$project" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:Version="$version" -o "$out"
done

mkdir -p "$cache"
if [ ! -f "$cache/vc_redist.x64.exe" ]; then
    curl -fsSL -o "$cache/vc_redist.x64.exe" https://aka.ms/vc14/vc_redist.x64.exe
fi

# The image's Wine prefix belongs to its own user (xclient, uid 1000), so the container runs as that user (not as
# the caller, e.g. uid 1001 on GitHub's runners: "wine: /home/xclient/.wine is not owned by you"); out/ is made
# writable for it.
chmod a+rwx out
docker run --rm -v "$PWD:/work" -w /work/packaging/windows amake/innosetup:latest \
    /DAppVersion="$version" RawConverter.iss
ls -l "out/RawConverter-$version-win-x64-setup.exe"
