dotnet build ./RawConverterDotnetCore.csproj
cp common/*so bin/release/netcoreapp3.0/

 dotnet publish /home/yateslab/gitRepos/RawConverterDotnetCore/RawConverterDotnetCore.csproj /property:GenerateFullPaths=true
