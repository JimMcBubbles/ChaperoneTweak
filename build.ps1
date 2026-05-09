# Build script for ChaperoneTweak
# Compiles Assembly-CSharp.dll from source and deploys it to the Steam installation.
#
# Unity 6 splits UnityEngine into many UnityEngine.*.dll modules.
# This script globs all of them so new modules (e.g. UnityEngine.XRModule.dll) are
# picked up automatically without editing the reference list.

$InstallDir = "C:\Program Files (x86)\Steam\steamapps\common\ChaperoneTweak\ChaperoneTweak_Data\Managed"
$SourceRoot  = "$PSScriptRoot\ChaperoneTweak\Assets"
$OutputDll   = "$PSScriptRoot\Assembly-CSharp.dll"

# csc.exe ships with .NET Framework on all Windows machines
$Csc = "${env:SystemRoot}\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $Csc)) {
    $Csc = "${env:SystemRoot}\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $Csc)) {
    Write-Error "Could not find csc.exe. Please install .NET Framework 4."
    exit 1
}

# Collect all .cs files except those under Plugins (already in Assembly-CSharp-firstpass.dll)
$Sources = Get-ChildItem -Path $SourceRoot -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '\\Plugins\\' -and $_.FullName -notmatch '\\Editor\\' } |
    ForEach-Object { "`"$($_.FullName)`"" }

if (-not $Sources) {
    Write-Error "No source files found under $SourceRoot"
    exit 1
}

Write-Host "Compiling $($Sources.Count) source files..."

# Reference all UnityEngine module DLLs (handles both Unity 5.x monolithic and Unity 6 modular layouts)
$Refs = Get-ChildItem -Path $InstallDir -Filter "UnityEngine*.dll" |
    ForEach-Object { "-r:`"$($_.FullName)`"" }

$Refs += "-r:`"$InstallDir\Assembly-CSharp-firstpass.dll`""
$Refs += "-r:`"$InstallDir\System.dll`""
$Refs += "-r:`"$InstallDir\System.Core.dll`""
$Refs += "-r:`"$InstallDir\mscorlib.dll`""

$Args = @(
    "-target:library",
    "-out:`"$OutputDll`"",
    "-nostdlib",     # don't auto-reference .NET Framework mscorlib; use the game's Mono one
    "-noconfig",     # don't load any default response files
    "-nowarn:0169,0414,0649"
) + $Refs + $Sources

& $Csc @Args

if ($LASTEXITCODE -ne 0) {
    Write-Error "Compilation failed."
    exit 1
}

Write-Host "Compilation succeeded. Deploying to Steam installation..."
Copy-Item -Path $OutputDll -Destination "$InstallDir\Assembly-CSharp.dll" -Force
Write-Host "Done. Launch ChaperoneTweak to test."
