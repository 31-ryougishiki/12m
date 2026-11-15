param(
 [string]$Exe='artifacts/flow-reader-review/bin/ElymsPet.exe',
 [Parameter(Mandatory=$true)][string]$DraftPath,
 [Parameter(Mandatory=$true)][string]$EvidencePath
)
$ErrorActionPreference='Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$assembly=(Resolve-Path -LiteralPath $Exe).Path
$probe=Join-Path (Split-Path -Parent $assembly) 'FlowReaderLiveProbe.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /codepage:65001 ('/out:'+$probe) ('/reference:'+$assembly) /reference:System.Core.dll /reference:System.Runtime.Serialization.dll (Resolve-Path -LiteralPath 'tools/FlowReaderLiveProbe.cs').Path
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
$outputDir=Join-Path (Split-Path -Parent (Split-Path -Parent $assembly)) ('live-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($outputDir)
& $probe (Resolve-Path -LiteralPath $DraftPath).Path (Resolve-Path -LiteralPath $EvidencePath).Path $outputDir
if ($LASTEXITCODE -ne 0) { throw 'Live review failed; trace retained.' }
