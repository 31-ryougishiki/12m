param([switch]$Render)
$ErrorActionPreference = 'Stop'
$runDir = Join-Path $PSScriptRoot ('artifacts/tests/' + [guid]::NewGuid().ToString('N'))
& (Join-Path $PSScriptRoot 'build.ps1') -Out (Join-Path $runDir 'ElymsPet.exe') -Tests
Push-Location $runDir
try {
    & (Join-Path $runDir 'ElymsPet.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Offline C# tests failed' }
    if ($Render) {
        $process = Start-Process -FilePath (Join-Path $runDir 'ElymsPet.exe') -ArgumentList '--render' -WorkingDirectory $runDir -WindowStyle Hidden -PassThru
        if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Preview render timed out' }
        if ($process.ExitCode -ne 0) { throw 'Preview render failed' }
    }
} finally { Pop-Location }
& node --test (Join-Path $PSScriptRoot 'dsh-bridge-plugin/test/bridge.test.js')
if ($LASTEXITCODE -ne 0) { throw 'Bridge tests failed' }
Write-Output ('Test output: ' + $runDir)
