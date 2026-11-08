$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
New-Item -ItemType Directory -Force dist | Out-Null
$refs = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll')
$argsList = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output','/codepage:65001','/out:dist/ElymsPet.exe','/win32manifest:src/app.manifest','/win32icon:assets/pet.ico','/resource:assets/sprites.png,sprites.png','/resource:assets/pet.ico,pet.ico')
foreach ($ref in $refs) { $argsList += '/reference:' + (Join-Path $framework $ref) }
$argsList += @('src\State.cs','src\Chat.cs','src\PetWindow.cs','src\ControlWindow.cs','src\Program.cs')
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Output 'Built dist/ElymsPet.exe'
