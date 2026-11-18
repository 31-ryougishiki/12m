# togawa 个人分支：远程部署与本机数据迁移教程

更新日期：2026-11-18。适用场景：本机已经有知识图谱，在另一台 Windows 电脑拉取 `12m` 并继续学习。代码使用个人分支 `togawa`，个人资料另行迁移。Git 仓库不包含你正在使用的存档、图谱、复述、聊天和密钥。

操作顺序：远程准备环境 → 拉取并构建 → 本机导出数据 → 远程备份并导入 → 启用插件 → 配置 AI、源码和可选 DSH → 验证。注明“本机”或“远程”的命令分别在对应电脑执行，不必在本机重新克隆项目。

## 1. 远程环境要求

- Windows 10/11，或具备 Desktop Experience 的 Windows Server，并有可交互桌面。当前是 WPF 桌宠，没有浏览器客户端，不能直接在 Linux 或无桌面的容器运行。
- .NET Framework 4.8 或 4.8.1；仅安装现代 `.NET` SDK 不能替代 Framework 运行时。
- 源码部署需要 Git for Windows 和 Windows PowerShell。`build.ps1` 使用 Framework 自带编译器，不要求 Visual Studio、Python 或 Node.js。
- 完整测试或打包需要支持 `node --test` 的 Node.js，WPF 窗口测试需要交互桌面。
- 远程电脑能访问 GitHub 和所配置的模型 API，默认 API 为 `api.deepseek.com`。

官方说明：[WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)、[.NET Framework 安装](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)。

通过远程桌面登录，用将来运行桌宠的同一 Windows 账户配置。`%LOCALAPPDATA%` 对应当前账户，用另一账户启动会读取另一套数据。

## 2. 远程拉取 togawa 并构建

在**远程 PowerShell**执行；`C:\Apps\12m` 应是尚不存在的新目录，已有克隆参见第 8 节。

```powershell
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path C:\Apps -Force | Out-Null
git clone --branch togawa https://github.com/31-ryougishiki/12m.git C:\Apps\12m
if ($LASTEXITCODE -ne 0) { throw '克隆失败，请检查网络、仓库权限和目标目录' }
Set-Location C:\Apps\12m
git branch --show-current
$framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if ($framework.Release -lt 528040) { throw '请先安装 .NET Framework 4.8 或更高版本' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
if ($LASTEXITCODE -ne 0) { throw '构建失败，请查看上述错误' }
```

分支输出应为 `togawa`，构建结束应显示 `Built ...\dist\ElymsPet.exe`。先完成下面的数据导入，再启动程序。

ExecutionPolicy 只作用于本次 PowerShell 子进程，组织策略限制脚本时按组织规定配置。需要 GitHub 权限时走 Git 正常认证流程，不将访问令牌写进 URL。

保留整个 `dist` 运行目录，包括 EXE、`.config`、DLL、`fonts` 和 `plugin-packages`；不要只复制 EXE。仓库根目录旧 ZIP 是历史产物。

## 3. 本机导出知识图谱与学习资料

### 数据位置与迁移范围

在**本机**从托盘选择“退出”，等正在保存的操作完成。若 DSH 会自动拉起桌宠，也先退出 DSH 或停用自动启动。

按 `Win + R` 打开 `%LOCALAPPDATA%\ElymsDesktopPet`：

| 内容 | 相对于数据目录的位置 | 迁移建议 |
| --- | --- | --- |
| 全部存档、图谱、节点复述、访谈、流程、资料卡 | `archives\archives.json` | 必须，同时保留已有 `.bak` |
| 最近完整节点讲解 | `learning\` | 建议迁移 |
| 历史讲解、反馈和审阅记录 | `repository-explanations\` | 建议迁移，供历史恢复与诊断 |
| 长期记忆 | `memories.json` | 按需，含已有 `.bak` |
| 聊天记录 | `chat-history.json` | 按需，含已有 `.bak` |
| 自主活动记录 | `autonomy-journal.json` | 按需，含已有 `.bak` |
| 最近选择的专题 | `graph-view.json` | 按需，含已有 `.bak` |
| 设置、便签、显示偏好 | `settings.json` | 可另行迁移，仍需检查密钥、位置和自主活动设置 |
| 外部附件、源码仓库 | 原来保存的位置 | 另行复制或获取，不包含在存档 JSON 中 |

`archives.json` 是图谱业务数据。`archives\knowledge\...\graph.json`、`SKILL.md` 和索引是派生文件，会在存档保存/切换时重新生成。不要只复制派生图谱，也不要把旧的 `graph-edit.json` 编辑请求带到新机执行。

### 生成迁移包

在**本机 PowerShell**执行。命令只读取允许迁移的资料，在桌面建立新包，不改原数据；不会导出设置、API 凭据、B站登录、OSS 凭据、桥接连接或插件文件。

```powershell
$ErrorActionPreference = 'Stop'
if (Get-Process -Name ElymsPet -ErrorAction SilentlyContinue) {
    throw '请先退出曳尔沐，再导出'
}
$dataDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ElymsDesktopPet'
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$exportDir = Join-Path ([Environment]::GetFolderPath('Desktop')) ('ElymsData-' + $stamp)
if (!(Test-Path -LiteralPath (Join-Path $dataDir 'archives\archives.json'))) {
    throw '当前账户下未找到存档，请检查 Windows 账户和数据目录'
}
New-Item -ItemType Directory -Path (Join-Path $exportDir 'archives') -Force | Out-Null
$files = @(
    'archives\archives.json', 'archives\archives.json.bak',
    'memories.json', 'memories.json.bak', 'chat-history.json', 'chat-history.json.bak',
    'autonomy-journal.json', 'autonomy-journal.json.bak', 'graph-view.json', 'graph-view.json.bak'
)
foreach ($relative in $files) {
    $source = Join-Path $dataDir $relative
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $exportDir $relative)
    }
}
foreach ($name in @('learning', 'repository-explanations')) {
    $source = Join-Path $dataDir $name
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $exportDir -Recurse
    }
}
$zip = $exportDir + '.zip'
Compress-Archive -Path (Join-Path $exportDir '*') -DestinationPath $zip
Get-FileHash -LiteralPath $zip -Algorithm SHA256
Write-Output ('迁移包：' + $zip)
```

通过远程桌面文件传输等可信方式把 ZIP 传到远程电脑，记录输出的 SHA256。压缩失败或日志过大时，可直接传输新生成的 `ElymsData-...` 目录。包内有个人对话和学习资料，应私下传输，不提交 GitHub。

## 4. 远程备份并导入

在**远程电脑**退出桌宠及会自动启动它的 DSH 插件。可用 `Get-FileHash -LiteralPath '迁移包的实际路径' -Algorithm SHA256` 核对 ZIP 与本机一致，然后完整解压到新目录，例如 `C:\Transfer\ElymsData`。该目录下应直接有 `archives\archives.json`，不要多套一层。

以下操作保留原数据目录，再建立新目录，避免旧请求、旧索引与新存档混用。它用本机快照替换远程全部存档及本次导入的资料，不合并远程修改；原目录仍保留在旁边，可以恢复。

在**远程 PowerShell**执行，先将 `$importDir` 改为实际解压目录：

```powershell
$ErrorActionPreference = 'Stop'
$importDir = 'C:\Transfer\ElymsData'
if (Get-Process -Name ElymsPet -ErrorAction SilentlyContinue) { throw '请先退出远程曳尔沐' }
$archive = Join-Path $importDir 'archives\archives.json'
if (!(Test-Path -LiteralPath $archive)) { throw '解压目录下缺少 archives\archives.json' }
$book = Get-Content -LiteralPath $archive -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$book.Slots -or $book.Slots.Count -eq 0) { throw '存档为空，请检查迁移包' }
$localRoot = [IO.Path]::GetFullPath([Environment]::GetFolderPath('LocalApplicationData'))
$dataDir = [IO.Path]::GetFullPath((Join-Path $localRoot 'ElymsDesktopPet'))
if ((Split-Path -Parent $dataDir) -ne $localRoot -or (Split-Path -Leaf $dataDir) -ne 'ElymsDesktopPet') {
    throw '数据目录检查失败，未执行迁移'
}
if (Test-Path -LiteralPath $dataDir) {
    if ((Get-Item -LiteralPath $dataDir).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw '数据目录是链接，请先确认真实存储位置'
    }
    $backupName = 'ElymsDesktopPet.before-import-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    Rename-Item -LiteralPath $dataDir -NewName $backupName
    Write-Output ('原数据已保留：' + (Join-Path $localRoot $backupName))
}
New-Item -ItemType Directory -Path (Join-Path $dataDir 'archives') -Force | Out-Null
$files = @(
    'archives\archives.json', 'archives\archives.json.bak',
    'memories.json', 'memories.json.bak', 'chat-history.json', 'chat-history.json.bak',
    'autonomy-journal.json', 'autonomy-journal.json.bak', 'graph-view.json', 'graph-view.json.bak'
)
foreach ($relative in $files) {
    $source = Join-Path $importDir $relative
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $dataDir $relative)
    }
}
foreach ($name in @('learning', 'repository-explanations')) {
    $source = Join-Path $importDir $name
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $dataDir -Recurse
    }
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne (Get-FileHash -LiteralPath (Join-Path $dataDir 'archives\archives.json')).Hash) {
    throw '导入后的存档哈希不一致，请勿启动程序'
}
Write-Output ('已导入：' + $dataDir)
```

若导入中断，先不要启动程序；原目录仍在 `ElymsDesktopPet.before-import-...`。补齐导入，或将未完成的新目录改名保留，再把原备份改回 `ElymsDesktopPet`。不要在未备份时反复覆盖。

要保留便签、字号等设置，可在启动前单独复制本机 `settings.json`，或使用远程原备份里的设置文件。来自另一台电脑的加密 API Key 无法直接使用；复制设置后检查窗口位置、自主活动开关等。B站重新扫码，OSS 重新配置凭据，`dsh-bridge.json` 由远程桥接生成。

## 5. 启动、启用插件和配置 AI

在**远程 PowerShell**执行：

```powershell
Start-Process -FilePath C:\Apps\12m\dist\ElymsPet.exe `
    -ArgumentList '--panel' -WorkingDirectory C:\Apps\12m\dist
```

1. 陪伴面板 →“插件管理”，安装并启用“存档单元”和“知识图谱”；按需安装“DSH 连接”“云同步”“B站”。更改后从托盘退出并重新启动。
2. 复制存档不会自动创建插件安装状态。遇到版本不匹配时从当前程序安装/修复插件并重启，不用旧机器的 DLL 替换新包。
3. “设置 → AI 对话”重新填写 API Key，保存并测试连接。DPAPI 保护的 Key、Cookie 和 OSS 凭据不能作为通用跨机凭证复制。
4. 在存档单元选择原存档，再选择图谱专题，不必先新建存档。无 Key 仍可阅读已保存内容，生成和联网功能需完成配置。
5. 检查节点介绍、模块、来源、个人复述和已保存讲解；不要点击“重新生成”来验证旧讲解是否已迁移。
6. 退出再启动一次，确认资料仍在，然后开启需要的自主活动或自动上传。

程序不会自动设置开机启动。如需登录后启动，在 `Win + R → shell:startup` 目录放 EXE 快捷方式。桌宠应运行在登录用户的桌面会话；注销会结束应用，断开远程桌面后是否保留会话取决于主机策略。

## 6. 配置源码阅读和 DSH（按需）

### 两个源码仓库与编辑器

只读图谱正文不必克隆源码。需要本地跳转、源码核实或 DSH 阅读时，在**远程 PowerShell**执行：

```powershell
New-Item -ItemType Directory -Path C:\Repos -Force | Out-Null
git clone https://github.com/vllm-project/vllm.git C:\Repos\vllm
if ($LASTEXITCODE -ne 0) { throw 'vLLM 克隆失败' }
git clone https://github.com/vllm-project/vllm-ascend.git C:\Repos\vllm-ascend
if ($LASTEXITCODE -ne 0) { throw 'vLLM-Ascend 克隆失败' }
```

不加 `--depth 1`：图谱引用历史提交，需要相应 Git 对象；浅克隆的最新文件不能代替旧源码。也可以迁移本机完整仓库目录，包含 `.git`。

在图谱节点“代码 → 配置本地仓库”中，将 `vllm-project/vllm` 映射到 `C:\Repos\vllm`，将 `vllm-project/vllm-ascend` 映射到 `C:\Repos\vllm-ascend`，选择远程实际安装的 `Code.exe` / `Cursor.exe`。不要原样复制旧的 `local-repositories.json` 后假定路径仍有效。

以节点 Sources 链接中的 40 位 SHA 为准检查版本（替换变量，并按实际仓库调整路径）：

```powershell
$sourceCommit = '替换成节点来源中的完整40位提交SHA'
git -C C:\Repos\vllm cat-file -t $sourceCommit
```

成功输出 `commit`。若缺失，在对应仓库获取历史；浅克隆可先执行 `git fetch --unshallow origin`。若上游提供该对象，也可 `git fetch origin $sourceCommit`。取不到时保留缺失提示，不拿最新源码冒充旧版本。无需 checkout 到图谱提交，应用可从 Git 对象读取固定版本快照。

### 远程 DSH 桥接

先在远程安装、启动 DSH，完成模型配置，确保当前账户有可用的 `web` profile，再执行：

```powershell
Set-Location C:\Apps\12m
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install-dsh-plugins.ps1
if ($LASTEXITCODE -ne 0) { throw '安装失败，请检查 DSH web profile 和脚本提示' }
```

脚本挂载桌宠自动启动与工作台桥接两个 host 插件，首次配置从当前目录生成 EXE 路径和 workspace。若已有同名配置或 junction 指向旧目录，脚本可能保留旧项；核对 `%USERPROFILE%\.dsh\profiles\web\cordis.patch.yml` 的 `exePath` / `workspace` 及 junction 目标。DSH 热加载可能自动拉起桌宠，应在数据导入完成后安装。

DSH 与桌宠在同一远程电脑、同一账户运行，桥接重新生成 `%LOCALAPPDATA%\ElymsDesktopPet\dsh-bridge.json`。不要复制本机握手文件，也不需要向公网开放桥接端口。图谱源码核实走只读请求，普通写入任务遵循委托配置。

验证：打开桌宠 DSH 工作台确认连接，再对一个有源码映射的节点请求解释实现，检查结果中的文件、版本和证据。启动 DSH 不代表模型鉴权、路径和工具权限均已验证。详细约定见 [桥接插件说明](../dsh-bridge-plugin/README.md)。

## 7. 两台电脑交替使用与 OSS

首次完整迁移建议用文件包。后续可在插件管理启用“云同步”，重启后进入“存档单元 → 云同步”：

1. 本机配置自己的 Bucket、地域、目录及专用 RAM 凭据，点击“立即上传”。
2. 远程重新配置同一 Bucket、地域、目录的访问凭据，刷新云端版本，选择刚上传的版本恢复。
3. 恢复替换全部存档，先在远程 `archives/cloud-backups/` 备份原存档。不同机器的云端快照分别保存，不自动合并。
4. 切换电脑前上传当前版本，在另一台继续编辑前手动恢复所需版本，避免双方修改互相覆盖。

| 内容 | OSS 存档快照 |
| --- | --- |
| 存档、图谱、节点复述、访谈、流程、资料卡和缓存文本 | 包含 |
| `learning/` 独立讲解、`repository-explanations/` 历史日志 | 不包含，另行文件迁移 |
| 聊天、记忆、自主记录、设置、密钥和登录态 | 不包含 |
| 本地源码、外部附件、仓库映射和编辑器 | 不包含 |

自动上传不是双向同步：不会自动下载覆盖另一台，也不合并内容。不要让两个运行中的应用使用同一个共享数据目录。权限与限制见 [存档云同步](archive-cloud-sync.md)。

## 8. 以后更新代码与维护 togawa

`togawa` 是独立个人分支，`main` 的新提交不会自动进入它。更新代码不迁移个人数据，也不自动同步另一台机器的图谱。

### 远程更新 togawa

退出桌宠和会重新启动它的进程，备份数据，然后在**远程 PowerShell**执行：

```powershell
$ErrorActionPreference = 'Stop'
Set-Location C:\Apps\12m
git status --short
# 若有未保存的源码/文档修改，先提交或备份，再继续。
git switch togawa
if ($LASTEXITCODE -ne 0) { throw '切换分支失败，先处理提示' }
git pull --ff-only origin togawa
if ($LASTEXITCODE -ne 0) { throw '拉取失败，先处理分歧或本地修改' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
if ($LASTEXITCODE -ne 0) { throw '构建失败，不启动未完成的构建' }
Start-Process -FilePath .\dist\ElymsPet.exe -ArgumentList '--panel' `
    -WorkingDirectory (Resolve-Path .\dist).Path
```

旧克隆只有 `main` 时，在工作区干净的前提下先 `git fetch origin`，再 `git switch --track origin/togawa` 建立跟踪分支。

本仓库跟踪了一些 `dist` 文件，构建可能使它们发生变化。若构建产物阻止拉取，先确认和备份；可在另一个新目录 clone `togawa` 并构建。个人数据仍从同一账户的原数据目录读取，无需重新导入。更换运行目录后更新快捷方式、DSH `exePath`、workspace 和 junction；不要用 `reset --hard` 抹掉未确认的改动。

### 将 main 后续更新合入个人分支

在维护分支的开发电脑执行，先保存个人修改并保持工作区干净：

```powershell
git switch togawa
if ($LASTEXITCODE -ne 0) { throw '切换分支失败' }
git fetch origin
if ($LASTEXITCODE -ne 0) { throw '获取远端失败' }
git merge --ff-only origin/togawa
if ($LASTEXITCODE -ne 0) { throw '本地与远端 togawa 已分歧，先处理' }
git merge origin/main
if ($LASTEXITCODE -ne 0) { throw '合并未完成，解决冲突并验证后再推送' }
```

合并包含代码变化时完成相应构建/测试；有冲突先解决并提交。之后执行 `git push origin togawa`，不需要强推。远程使用机器再按本节的拉取步骤更新。

## 9. 可选：便携包部署

在开发电脑检出 `togawa` 后执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
if ($LASTEXITCODE -ne 0) { throw '打包失败，请检查测试和构建输出' }
```

脚本运行离线测试并构建，在 `artifacts/releases/` 生成 ZIP；暂存目录里的 `SHA256SUMS.txt` 文件清单随包分发。远程完整解压后不需要 Git、Node.js 或编译工具，但仍需 Windows 和 .NET Framework。数据导入、插件启用和配置步骤同前，用实际解压目录替换本文 EXE 路径。

## 10. 验收与排错

| 现象 | 优先检查 |
| --- | --- |
| 没有图谱或存档入口 | 插件是否安装、启用并已重启 |
| 有入口但图谱为空 | Windows 账户、`archives\archives.json` 位置、所选存档和专题 |
| 节点在但旧讲解不见 | 是否迁移 `learning/` 与讲解历史；OSS 不包含它们 |
| Key 无法解密或联网失败 | 远程重新填写 Key，检查 API 鉴权、额度与网络 |
| 本地代码跳转失败 | 仓库映射、编辑器、来源提交和文件是否存在 |
| 插件版本不匹配 | 从当前程序安装/修复并重启，不混用旧 DLL |
| DSH 未连接 | 远程 web profile、桥接是否运行，是否同一 Windows 账户 |
| 更新后仍运行旧版 | 实际 EXE 路径、快捷方式与 DSH 自动启动配置 |
| 窗口不见 | 双击托盘或按 `Ctrl + Alt + E`，检查远程热键转发和旧窗口位置 |

远程 PowerShell 检查：

```powershell
Get-Process ElymsPet -ErrorAction SilentlyContinue | Select-Object Id, Path
Get-Content "$env:LOCALAPPDATA\ElymsDesktopPet\error.log" -Tail 80 -ErrorAction SilentlyContinue
Resolve-DnsName api.deepseek.com
Test-NetConnection api.deepseek.com -Port 443
```

需要完整离线回归与 UI 验证时，在代码目录的交互桌面执行（需要 Node.js）：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Render
```

最终确认：原存档和专题可读、复述保留、旧讲解可恢复、API 测试成功、源码定位符合固定版本，并且重启仍能读取。DNS/端口检查和离线测试不等同于真实模型或 NPU 验证。日志可能包含个人输入，分享前检查。数据结构见 [开发说明中的数据保存分工](architecture.md#数据保存分工)。
