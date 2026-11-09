param([string]$Out = 'dist/ElymsPet.exe')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
New-Item -ItemType Directory -Force dist | Out-Null
$refs = @('System.dll','System.Core.dll','System.Runtime.Serialization.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Security.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll')
$outArg = '/out:' + $Out
$argsList = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output','/codepage:65001',$outArg,'/win32manifest:src/app.manifest','/win32icon:assets/pet.ico','/resource:assets/sprites.png,sprites.png','/resource:assets/pet.ico,pet.ico')
foreach ($ref in $refs) { $argsList += '/reference:' + (Join-Path $framework $ref) }
$argsList += '/reference:vendor\qrcoder\QRCoder.dll'
$argsList += @('src\State.cs','src\JsonFile.cs','src\Memory.cs','src\MemoryPanel.cs','src\ApiKey.cs','src\Persona.cs','src\Chat.cs','src\ChatService.cs','src\ChatView.cs','src\Web.cs','src\WebPanel.cs','src\Bili.cs','src\BiliAuth.cs','src\BiliLoginWindow.cs','src\BiliService.cs','src\BiliPanel.cs','src\Autonomy.cs','src\Dsh.cs','src\DshPanel.cs','src\Archive.cs','src\ArchiveService.cs','src\ArchivePanel.cs','src\Ui.cs','src\PetWindow.cs','src\ChatBarWindow.cs','src\ControlWindow.cs','src\Program.cs')
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Output ('Built ' + $Out)
