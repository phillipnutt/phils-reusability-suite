# Compiles src/Core with Tools/CoreTests.cs (no KSP needed) and runs the checks against the stock craft
# of a KSP install. Usage: Tools/Test-Core.ps1 -Ksp <KSP folder>   (or set the KSP_DIR environment variable)
param([string]$Ksp = $env:KSP_DIR)
$ErrorActionPreference = 'Stop'
if (-not $Ksp) { throw 'Give the KSP folder: Tools/Test-Core.ps1 -Ksp <KSP folder>, or set KSP_DIR.' }
$repo = Split-Path $PSScriptRoot -Parent
$csc = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
if (-not (Test-Path $csc)) { $csc = (Get-Command csc.exe -ErrorAction Stop).Source }
$out = Join-Path ([System.IO.Path]::GetTempPath()) 'PhilsReusabilitySuite-CoreTests.exe'
$core = Get-ChildItem (Join-Path $repo 'src\Core') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /langversion:latest /out:$out @core (Join-Path $PSScriptRoot 'CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
& $out $Ksp
exit $LASTEXITCODE
