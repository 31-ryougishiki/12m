param([Parameter(Mandatory=$true)][string]$Graph,[switch]$Apply)
$ErrorActionPreference='Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$folder=Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $Graph).Path) 'import-bin'
[void][IO.Directory]::CreateDirectory($folder)
Copy-Item -LiteralPath 'dist/ElymsPet.exe' -Destination (Join-Path $folder 'ElymsPet.exe')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $folder 'ImportGlobalModules.exe'
& $compiler /nologo /target:exe /codepage:65001 ('/out:'+$exe) ('/reference:'+(Join-Path $folder 'ElymsPet.exe')) /reference:System.Core.dll /reference:System.Runtime.Serialization.dll (Resolve-Path 'tools/ImportGlobalModules.cs').Path
if($LASTEXITCODE -ne 0){throw 'Import helper compilation failed.'}
if($Apply){& $exe (Resolve-Path -LiteralPath $Graph).Path --apply}else{& $exe (Resolve-Path -LiteralPath $Graph).Path}
if($LASTEXITCODE -ne 0){throw 'Import validation/application failed.'}
