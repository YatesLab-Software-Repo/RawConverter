rm -r obj/
rm -r out/*
dotnet build ./RawConverterDotnetCore.csproj

rm -r ./out/RawConverterDotNetCoreWindows;
rm -r ./out/RawConverterDotNetCoreLinux;

dotnet publish -c Release --self-contained true -r win10-x64 -o ./out/RawConverterDotNetCoreWindows/;
dotnet publish -r linux-x64 -o ./out/RawConverterDotNetCoreLinux/; 
#dotnet publish --self-contained -r linux-x64 --configuration Release ./RawConverterDotnetCore.csproj /property:GenerateFullPaths=true -o out
#cp Common/*so bin/Release/netcoreapp3.0/
