param([string]$Out = 'dist/ElymsPet.exe', [switch]$Tests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
$Out = [System.IO.Path]::GetFullPath($Out)
$outDir = Split-Path -Parent $Out
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$refs = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Security.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll')
$outArg = '/out:' + $Out
$argsList = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output','/codepage:65001',$outArg,'/win32manifest:src/app.manifest','/win32icon:assets/pet.ico','/resource:assets/sprites.png,sprites.png','/resource:assets/pet.ico,pet.ico')
foreach ($ref in $refs) { $argsList += '/reference:' + (Join-Path $framework $ref) }
$argsList += '/resource:assets/persona/elyms.txt,elyms-persona.txt'
$argsList += '/reference:vendor\qrcoder\QRCoder.dll'
$argsList += '/reference:vendor\msagl\Microsoft.Msagl.dll'
$argsList += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -LiteralPath 'src/App.config' -Destination ($Out + '.config') -Force
Copy-Item -LiteralPath 'vendor/qrcoder/QRCoder.dll' -Destination $outDir -Force
Copy-Item -LiteralPath 'vendor/msagl/Microsoft.Msagl.dll' -Destination $outDir -Force
Copy-Item -LiteralPath 'vendor/msagl/LICENSE.txt' -Destination (Join-Path $outDir 'MSAGL-LICENSE.txt') -Force
if ($Tests) {
    $testExe = Join-Path $outDir 'ElymsPet.Tests.exe'
    $testArgs = @('/nologo','/target:exe','/utf8output','/codepage:65001',('/out:' + $testExe),('/reference:' + $Out))
    foreach ($ref in $refs) { $testArgs += '/reference:' + (Join-Path $framework $ref) }
    $testArgs += @(Get-ChildItem -LiteralPath 'tests/unit' -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
    & $compiler @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    Copy-Item -LiteralPath 'src/App.config' -Destination ($testExe + '.config') -Force
}
Write-Output ('Built ' + $Out)
