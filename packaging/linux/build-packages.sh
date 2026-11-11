#!/bin/bash
# Build the Linux packages into out/: rawconverter_<version>_amd64.deb (Debian/Ubuntu),
# rawconverter-<version>-1.x86_64.rpm (Fedora/RHEL/Rocky/Alma), rawconverter-<version>-1-x86_64.pkg.tar.zst (Arch)
# and a portable RawConverter-<version>-linux-x64.tar.gz for other distributions. Uses nFPM in Docker.
# The version comes from Directory.Build.props unless VERSION is set (the release workflow sets it from the tag).
set -euo pipefail
cd "$(dirname "$0")/../.."

export VERSION=${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)}
publish=out/RawConverter-linux-x64
stage=out/linux-stage

rm -rf "$publish" "$stage"
for project in src/RawConverter.Cli/RawConverter.Cli.csproj src/RawConverter.Gui/RawConverter.Gui.csproj; do
    dotnet publish "$project" -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:Version="$VERSION" \
        -o "$publish"
done

# Windows libraries and debug symbols are not needed on Linux.
cp -a "$publish" "$stage"
rm -f "$stage"/*.pdb "$stage"/timsdata.dll "$stage"/lpsolve55.dll
cp LICENSE "$stage"/
mkdir -p "$stage"/Licenses
cp -a Licenses/. "$stage"/Licenses/
chmod -R u=rwX,go=rX "$stage"
chmod 755 "$stage"/RawConverter "$stage"/RawConverterGUI

for packager in deb rpm archlinux; do
    docker run --rm -u "$(id -u):$(id -g)" -e VERSION -v "$PWD:/work" -w /work goreleaser/nfpm:v2.43.1 \
        package --config packaging/linux/nfpm.yaml --packager "$packager" --target out/
done

tar -C out --transform "s:^linux-stage:RawConverter-$VERSION-linux-x64:" -czf "out/RawConverter-$VERSION-linux-x64.tar.gz" linux-stage
ls -l out/rawconverter[_-]* out/RawConverter-*-linux-x64.tar.gz
