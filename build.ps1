# Build script for ChaperoneTweak
# Compiles Assembly-CSharp.dll from source and deploys it to the Steam installation.

$InstallDir = "C:\Program Files (x86)\Steam\steamapps\common\ChaperoneTweak\ChaperoneTweak_Data\Managed"
$SourceRoot  = "$PSScriptRoot\ChaperoneTweak\Assets"
$OutputDll   = "$PSScriptRoot\Assembly-CSharp.dll"

# csc.exe ships with .NET Framework on all Windows machines
$Csc = "${env:SystemRoot}\Microsoft.NET\Framework64\v3.5\Smcs.exe"
if (-not (Test-Path $Csc)) {
    # Fall back to the Mono compiler bundled with the game itself
    $Csc = "$InstallDir\..\Mono\bin\smcs.exe"
}
if (-not (Test-Path $Csc)) {
    Write-Error "Could not find a C# compiler. Tried Framework64\v3.5\Smcs.exe and the game's Mono bin."
    exit 1
}

# Collect all .cs files except those under Plugins (already in Assembly-CSharp-firstpass.dll)
$Sources = Get-ChildItem -Path $SourceRoot -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '\\Plugins\\' } |
    ForEach-Object { "`"$($_.FullName)`"" }

if (-not $Sources) {
    Write-Error "No source files found under $SourceRoot"
    exit 1
}

Write-Host "Compiling $($Sources.Count) source files..."

# References: DLLs already present in the Steam installation
$Refs = @(
    "UnityEngine.dll",
    "UnityEngine.UI.dll",
    "Assembly-CSharp-firstpass.dll",
    "System.dll",
    "System.Core.dll"
) | ForEach-Object { "-r:`"$InstallDir\$_`"" }

$Args = @(
    "-target:library",
    "-out:`"$OutputDll`"",
    "-nowarn:0169,0414,0649"   # suppress common Unity-generated field warnings
) + $Refs + $Sources

& $Csc @Args

if ($LASTEXITCODE -ne 0) {
    Write-Error "Compilation failed."
    exit 1
}

Write-Host "Compilation succeeded. Deploying to Steam installation..."
Copy-Item -Path $OutputDll -Destination "$InstallDir\Assembly-CSharp.dll" -Force
Write-Host "Done. Launch ChaperoneTweak to test."
