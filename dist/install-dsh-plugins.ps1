# 把曳尔沐的两个 DSH host 插件挂到当前 web profile：
#   dsh-elyms-pet     —— DSH 启动时拉起/召回桌面桌宠
#   dsh-elyms-bridge  —— 桌宠 → DSH 的本机工作台 API
#
# 只做文件与目录 junction 操作，不启动桌宠、不开关 DSH。
# DSH 正在运行时，cordis.patch.yml 的改动会由 HMR 热加载；未运行时下次启动生效。
$ErrorActionPreference = 'Stop'
$scriptRoot = $PSScriptRoot
$projectRoot = $scriptRoot
if (!(Test-Path -LiteralPath (Join-Path $scriptRoot 'src')) -and (Test-Path -LiteralPath (Join-Path (Split-Path $scriptRoot -Parent) 'src'))) {
  $projectRoot = Split-Path $scriptRoot -Parent
}
$petPlugin = Join-Path $scriptRoot 'dsh-plugin'
if (!(Test-Path -LiteralPath $petPlugin)) { $petPlugin = Join-Path $projectRoot 'dsh-plugin' }
$bridgePlugin = Join-Path $scriptRoot 'dsh-bridge-plugin'
if (!(Test-Path -LiteralPath $bridgePlugin)) { $bridgePlugin = Join-Path $projectRoot 'dsh-bridge-plugin' }
$exe = Join-Path $projectRoot 'dist\ElymsPet.exe'
if (!(Test-Path -LiteralPath $exe)) { $exe = Join-Path $scriptRoot 'ElymsPet.exe' }
$workspace = $projectRoot
$profileDir = Join-Path $env:USERPROFILE '.dsh\profiles\web'
$modules = Join-Path $profileDir 'node_modules'
$patch = Join-Path $profileDir 'cordis.patch.yml'

if (!(Test-Path -LiteralPath $profileDir)) { throw "没有找到 web profile：$profileDir" }
if (!(Test-Path -LiteralPath $modules)) { throw "没有找到 profile 的 node_modules：$modules" }
if (!(Test-Path -LiteralPath $patch)) { throw "没有找到 profile patch：$patch" }
if (!(Test-Path -LiteralPath $petPlugin)) { throw "没有找到 dsh-plugin：$petPlugin" }
if (!(Test-Path -LiteralPath $bridgePlugin)) { throw "没有找到 dsh-bridge-plugin：$bridgePlugin" }
if (!(Test-Path -LiteralPath $exe)) { throw "没有找到 ElymsPet.exe：$exe" }

function Ensure-Link([string]$name, [string]$target) {
  $link = Join-Path $modules $name
  if (Test-Path -LiteralPath $link) {
    $item = Get-Item -LiteralPath $link
    $actual = @($item.Target) -join ';'
    if ($actual -eq $target -or $actual -eq (Resolve-Path -LiteralPath $target).Path) {
      Write-Output "已存在：$name"
      return
    }
    Write-Warning "已存在但指向其它目录，未改动：$link -> $actual（期望 $target）"
    return
  }
  New-Item -ItemType Junction -Path $link -Target $target | Out-Null
  Write-Output "已创建：$name -> $target"
}

Ensure-Link 'dsh-elyms-pet' $petPlugin
Ensure-Link 'dsh-elyms-bridge' $bridgePlugin

$text = [System.IO.File]::ReadAllText($patch, [System.Text.Encoding]::UTF8)
$changed = $false
$exePath = $exe.Replace('\', '/')
$workspace = $workspace.Replace('\', '/')

if ($text -notmatch '(?m)^\s*-\s*id:\s*elyms-pet\s*$') {
  if (!$text.EndsWith("`n")) { $text += "`n" }
  $text += @"

# == dsh-elyms-pet: launch the Windows desktop pet when this profile starts.
- insert:
    - id: elyms-pet
      name: 'dsh-elyms-pet'
      config:
        enabled: true
        exePath: '$exePath'
        startDelayMs: 1500
        leaveRunning: true
"@
  $changed = $true
  Write-Output '已添加 elyms-pet 配置'
} else {
  Write-Output '已存在 elyms-pet 配置'
}

if ($text -notmatch '(?m)^\s*-\s*id:\s*elyms-bridge\s*$') {
  if (!$text.EndsWith("`n")) { $text += "`n" }
  $text += @"

# == dsh-elyms-bridge: loopback workbench API for the Elyms desktop pet.
- insert:
    - id: elyms-bridge
      name: 'dsh-elyms-bridge'
      config:
        enabled: true
        workspace: '$workspace'
        defaultPermission: 'workspace-write'
        webUrl: 'http://127.0.0.1:3080'
"@
  $changed = $true
  Write-Output '已添加 elyms-bridge 配置'
} else {
  Write-Output '已存在 elyms-bridge 配置'
}

if ($changed) {
  $backup = $patch + '.bak-elyms-install-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
  Copy-Item -LiteralPath $patch -Destination $backup
  $utf8 = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($patch, $text, $utf8)
  Write-Output "已写入 profile patch，备份：$backup"
  Write-Output 'DSH 正在运行时约 3—5 秒后热加载；未运行时下次启动生效。'
} else {
  Write-Output '配置没有变化，未写文件。'
}

Write-Output ''
Write-Output '检查：'
Write-Output '  dsh --profile web --dump-config | Select-String "elyms-pet|elyms-bridge"'
Write-Output '  .\dist\ElymsPet.exe --dsh-test'
