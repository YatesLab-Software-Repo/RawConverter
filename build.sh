#!/bin/bash
# Build, test, and publish self-contained command-line and GUI builds for Linux and Windows into out/.
set -euo pipefail
cd "$(dirname "$0")"

dotnet build RawConverter.sln -c Release
dotnet test RawConverter.sln -c Release --no-build

# The command-line tool and the GUI share one folder per platform (they use the same native libraries).
for rid in linux-x64 win-x64; do
    rm -rf "out/RawConverter-$rid"
    for project in src/RawConverter.Cli/RawConverter.Cli.csproj src/RawConverter.Gui/RawConverter.Gui.csproj; do
        dotnet publish "$project" -c Release -r "$rid" --self-contained -p:PublishSingleFile=true -o "out/RawConverter-$rid"
    done
done
