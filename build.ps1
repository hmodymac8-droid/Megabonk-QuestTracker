# Builds Quest Tracker, optionally installs it into your game, and makes the Nexus zips in .\dist
#   .\build.ps1             build + package
#   .\build.ps1 -Install    also copy the DLL into your Megabonk BepInEx\plugins folder
param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Megabonk",
    [switch]$Install
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Zips with forward-slash paths so Vortex, 7-Zip and Steam Deck/Linux all extract them correctly.
function New-Zip([string]$SourceDir, [string]$ZipPath) {
    if (Test-Path $ZipPath) { Remove-Item $ZipPath }
    $zip = [IO.Compression.ZipFile]::Open($ZipPath, 'Create')
    try {
        $base = (Resolve-Path $SourceDir).Path.TrimEnd('\') + '\'
        Get-ChildItem $SourceDir -Recurse -File -Force | ForEach-Object {
            $rel = $_.FullName.Substring($base.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel, 'Optimal') | Out-Null
        }
    } finally { $zip.Dispose() }
}
$root = $PSScriptRoot
[xml]$proj = Get-Content "$root\QuestTracker.csproj"
$version = $proj.Project.PropertyGroup.Version

dotnet build "$root\QuestTracker.csproj" -c Release -nologo -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$dll = "$root\bin\Release\net6.0\QuestTracker.dll"

if ($Install) {
    $dest = "$GameDir\BepInEx\plugins\QuestTracker"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $dll $dest -Force
    Write-Host "Installed to $dest"
}

$dist = "$root\dist"
$stage = "$root\obj\stage"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $dist | Out-Null

# 1) Mod only: for players who already have BepInEx (and for Vortex)
$modOnly = "$stage\mod"
New-Item -ItemType Directory -Force "$modOnly\BepInEx\plugins\QuestTracker" | Out-Null
Copy-Item $dll "$modOnly\BepInEx\plugins\QuestTracker\"
Copy-Item "$root\package\README.txt" "$modOnly\BepInEx\plugins\QuestTracker\"
New-Zip $modOnly "$dist\QuestTracker-$version.zip"

# 2) All-in-one: BepInEx + the mod, for first-time modders (one extract and done)
$aio = "$stage\aio"
Copy-Item -Recurse $modOnly $aio
foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") { Copy-Item "$GameDir\$f" "$aio\" }
Copy-Item -Recurse "$GameDir\dotnet" "$aio\dotnet"
Copy-Item -Recurse "$GameDir\BepInEx\core" "$aio\BepInEx\core"
Copy-Item "$root\package\INSTALL - READ ME.txt" "$aio\"
New-Zip $aio "$dist\QuestTracker-$version-AllInOne-with-BepInEx.zip"

Get-ChildItem $dist | ForEach-Object { "{0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB) }
