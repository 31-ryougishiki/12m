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
# Build official feature assemblies against the shared host contract. The host has
# no static reference to these assemblies; only installed modules are loaded.
$pluginDescriptions = @{
 archive = @('存档单元','交流新内容、整理流程、逐项补充与审阅。','本地存档读写；按请求调用 AI 和公开资料')
 graph = @('知识图谱','领域图谱、节点编辑、关系浏览和 AI / DSH 编辑接收。','本地知识库读写；按请求读取映射仓库源码片段并调用 AI；读取图谱编辑请求')
 bili = @('B站','搜索视频、阅读评论、登录和互动。','访问 B站；本机保存登录；发布或删除评论仍需确认')
 dsh = @('DSH 连接','委托任务、回传结果、传递流程与图谱路径。','连接本机 DSH；任务权限沿用 DSH 设置')
 cloud = @('云同步','为共享存档与图谱上传版本和恢复备份。','访问已配置的 OSS；本机加密保存凭据')
}
$catalog = @()
$pluginAssemblies = @()
foreach ($pluginId in @('archive','graph','bili','dsh','cloud')) {
    $pluginDir = Join-Path $outDir ('plugin-packages/' + $pluginId)
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    $pluginDll = Join-Path $pluginDir ('ElymsPlugin.' + $pluginId + '.dll')
    $pluginArgs = @('/nologo','/target:library','/optimize+','/utf8output','/codepage:65001',('/out:' + $pluginDll),('/reference:' + $Out))
    foreach ($ref in $refs) { $pluginArgs += '/reference:' + (Join-Path $framework $ref) }
    $pluginArgs += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot ('plugins/' + $pluginId)) -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler @pluginArgs
    if ($LASTEXITCODE -ne 0) { throw ('Plugin compilation failed: ' + $pluginId) }
    Copy-Item -LiteralPath $pluginDll -Destination (Join-Path $pluginDir 'module.dll') -Force
    $description = $pluginDescriptions[$pluginId]
    $catalog += @{Id=$pluginId;Name=$description[0];Description=$description[1];Permissions=$description[2];Version='1.0.0';Api=1;Hash=(Get-FileHash -LiteralPath $pluginDll -Algorithm SHA256).Hash.ToLowerInvariant()}
    $pluginAssemblies += $pluginDll
}
$catalogPath = Join-Path $outDir 'official-plugins.json'
ConvertTo-Json -InputObject @($catalog) -Depth 4 | Set-Content -LiteralPath $catalogPath -Encoding UTF8
# Embed hashes in the host rather than trusting a mutable downloaded manifest.
$argsList += '/resource:' + $catalogPath + ',official-plugins.json'
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Host catalog compilation failed' }
Copy-Item -LiteralPath 'src/App.config' -Destination ($Out + '.config') -Force
Copy-Item -LiteralPath 'vendor/qrcoder/QRCoder.dll' -Destination $outDir -Force
Copy-Item -LiteralPath 'vendor/msagl/Microsoft.Msagl.dll' -Destination $outDir -Force
Copy-Item -LiteralPath 'vendor/msagl/LICENSE.txt' -Destination (Join-Path $outDir 'MSAGL-LICENSE.txt') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $outDir 'fonts') | Out-Null
Get-ChildItem -LiteralPath 'vendor/pixel-font' -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $outDir 'fonts') -Force}
if ($Tests) {
    $testExe = Join-Path $outDir 'ElymsPet.Tests.exe'
    $testArgs = @('/nologo','/target:exe','/utf8output','/codepage:65001',('/out:' + $testExe),('/reference:' + $Out))
    foreach ($pluginDll in $pluginAssemblies) {
        $testArgs += '/reference:' + $pluginDll
        Copy-Item -LiteralPath $pluginDll -Destination $outDir -Force
    }
    foreach ($ref in $refs) { $testArgs += '/reference:' + (Join-Path $framework $ref) }
    $testArgs += @(Get-ChildItem -LiteralPath 'tests/unit' -Filter '*.cs' -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName })
    & $compiler @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    Copy-Item -LiteralPath 'src/App.config' -Destination ($testExe + '.config') -Force
}
Write-Output ('Built ' + $Out)
