dotnet publish StarTruckerMP.Client/StarTruckerMP.Client.csproj ^
    -c Debug ^
    -r win-x64 ^
    -p:EnableWindowsTargeting=true ^
    -o "%StarTruckBepInEx%/plugins/StarTruckerMP"

dotnet publish StarTruckerMP.Client/StarTruckerMP.Client.csproj ^
    -c Debug ^
    -r win-x64 ^
    -p:EnableWindowsTargeting=true ^
    -o "%StarTruckBepInExSteam%/plugins/StarTruckerMP"