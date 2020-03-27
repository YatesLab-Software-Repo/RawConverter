dotnet build ./RawConverterDotnetCore.csproj


dotnet publish --configuration Release ./RawConverterDotnetCore.csproj /property:GenerateFullPaths=true
#cp Common/*so bin/Release/netcoreapp3.0/
