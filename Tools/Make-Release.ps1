# Builds a release zip, PhilsReusabilitySuite-<version>.zip, holding GameData/PhilsReusabilitySuite
# (the DLL, the ModuleManager patch, Settings.cfg, and the README, LICENSE and CHANGELOG).
# Build first (MSBuild with /p:KspDir=<your KSP folder>), then:
#   powershell -ExecutionPolicy Bypass -File Tools\Make-Release.ps1 -Version 0.1.0 -Out <folder>

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $root 'bin\Release\PhilsReusabilitySuite.dll'
if (-not (Test-Path $dll)) { throw 'Build first: bin\Release\PhilsReusabilitySuite.dll is missing.' }
$built = [Reflection.AssemblyName]::GetAssemblyName($dll).Version
if (-not $built.ToString().StartsWith($Version)) { throw "The built DLL is version $built, not $Version." }
New-Item -ItemType Directory -Force $Out | Out-Null

$path = Join-Path $Out "PhilsReusabilitySuite-$Version.zip"
if (Test-Path $path) { Remove-Item $path }
$zip = [IO.Compression.ZipFile]::Open($path, 'Create')
# Zip entry names always use forward slashes (Windows PowerShell 5.1's
# Compress-Archive writes backslashes, which other tools misread).
function AddFile($src, $entry) {
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $src, $entry, 'Optimal')
}
$gd = Join-Path $root 'GameData'
Get-ChildItem (Join-Path $gd 'PhilsReusabilitySuite') -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(Plugins|PluginData)\\' } |
    ForEach-Object { AddFile $_.FullName ('GameData/' + $_.FullName.Substring($gd.Length + 1).Replace('\', '/')) }
AddFile $dll 'GameData/PhilsReusabilitySuite/Plugins/PhilsReusabilitySuite.dll'
foreach ($d in 'LICENSE', 'README.md', 'CHANGELOG.md') { AddFile (Join-Path $root $d) "GameData/PhilsReusabilitySuite/$d" }
$zip.Dispose()

$check = [IO.Compression.ZipFile]::OpenRead($path)
$check.Entries | ForEach-Object { "  $($_.FullName)  ($($_.Length) bytes)" }
$check.Dispose()
Write-Host "Wrote $path"
