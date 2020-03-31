dotnet build ./RawConverterDotnetCore.csproj


dotnet publish -r linux-x64 --configuration Release ./RawConverterDotnetCore.csproj /property:GenerateFullPaths=true
#cp Common/*so bin/Release/netcoreapp3.0/
