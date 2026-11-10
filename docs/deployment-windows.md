# Windows 远程部署

本项目是 WPF 桌面程序。部署后通过远程桌面连接，在远程 Windows 用户的桌面中使用桌宠；当前没有浏览器客户端或多用户服务端。WPF 仅支持 Windows，参见 [微软 WPF 文档](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)。

## 环境

- Windows 10/11，或具备 Desktop Experience 的 Windows Server。建议使用仍受系统供应商支持的版本。
- .NET Framework 4.8 或 4.8.1；安装与系统对应关系见 [微软安装文档](https://learn.microsoft.com/en-us/dotnet/framework/install/on-windows-and-server)。仅安装现代 `.NET` SDK 不能替代 Framework 运行时。
- 从源码部署需要 Git for Windows 和 Windows PowerShell。`build.ps1` 使用 Framework 自带编译器，不要求 Visual Studio、Python 或 Node.js。
- 运行完整测试或打包脚本还需要提供 `node --test` 的 Node.js（建议受支持的 LTS 版本）。WPF 弹窗测试需要可交互的 Windows 桌面。
- 远程机器需能访问所配置的模型 API；默认是 `api.deepseek.com`。代码拉取还需访问 GitHub。

在远程桌面登录目标 Windows 账户后执行以下命令。桌宠的数据属于该账户，日后也应使用同一账户启动。

## 从源码部署

在 PowerShell 中执行；目录可自行替换：

```powershell
New-Item -ItemType Directory -Path C:\Apps -Force | Out-Null
Set-Location C:\Apps
git clone https://github.com/31-ryougishiki/12m.git
Set-Location C:\Apps\12m
if ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release -lt 528040) {
    throw '请先安装 .NET Framework 4.8 或更高版本'
}
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
if ($LASTEXITCODE -ne 0) { throw '构建失败，请先查看上述错误' }
Start-Process -FilePath .\dist\ElymsPet.exe -ArgumentList '--panel' -WorkingDirectory (Resolve-Path .\dist).Path
```

这里的 ExecutionPolicy 只作用于本次 PowerShell 子进程；若组织策略禁止脚本，应按组织规定配置。需要仓库权限时，通过 Git 的正常登录流程认证，不将访问令牌写入 clone URL。

必须保留 EXE 旁的 `ElymsPet.exe.config`、`QRCoder.dll`、`Microsoft.Msagl.dll` 与许可文件；不要只复制 EXE。仓库根目录的旧版本 ZIP 是历史产物，当前部署以源码构建为准。

## 使用便携包部署

也可在开发电脑的交互式 Windows 桌面运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

脚本先运行离线测试，再从源码构建，将当前版本 ZIP 输出到 `artifacts\releases\`，并生成 `SHA256SUMS.txt`。将新生成的 ZIP 传到远程机器，完整解压到例如 `C:\Apps\ElymsPet`，双击其中的 `ElymsPet.exe`。使用便携包的目标电脑不需要 Git、Node.js 或编译工具。

## 首次配置与检查

1. 打开「设置 → AI 对话」，输入自己的 API Key 并保存，使用界面中的连接测试。不要把密钥提交到 GitHub。
2. 打开「存档单元」，新建存档并启动访谈；回答一题后检查知识条目与掌握边界，再打开知识图谱确认节点可选中、提示框可悬停。
3. 完成一次保存，退出后重新打开，确认存档仍在。离线测试不会验证真实模型鉴权、额度或联网问答质量。
4. 如需 DSH，按随附插件 README 安装到同一远程电脑的 DSH，并保持 DSH 运行。本机桥接绑定 `127.0.0.1`，不需要向公网开放桥接端口。

程序不会自动设置开机启动。若需要登录后启动，可在 `Win + R → shell:startup` 打开的目录中放置 EXE 的快捷方式；该方式只在用户登录后启动。不要将可视桌宠设置为“无论用户是否登录都运行”的后台服务。云主机必须提供可交互桌面；注销账户会结束其桌面应用，断开远程桌面后的会话保留行为取决于主机策略。

## 数据迁移与备份

程序数据目录为 `%LOCALAPPDATA%\ElymsDesktopPet`，与代码目录分离。推送 GitHub 不会迁移现有个人知识库。

两端都从托盘选择退出后，先备份目标机器的已有数据，再通过可信文件传输复制以下内容到目标账户的同名数据目录：

- `archives\` 整个目录：存档、生成的 skills、资料索引、图谱及其子目录。
- `memories.json`、`chat-history.json`、`autonomy-journal.json` 及需要的 `.bak`：长期记忆、聊天与自主记录。
- 存档引用的外部本地文件：另外复制并在新机器检查路径；仅复制索引不能迁移仍在其它目录的附件。

`settings.json` 包含 API Key、便签及本机位置等设置。如需保留这些内容，只在自己的可信设备间单独迁移，并重新检查设置；也可在目标机重新配置。B站登录文件 `bili-account.dat` 由 Windows 用户保护机制加密，换机后请重新扫码；`dsh-bridge.json` 是当前桥接实例生成的连接信息，不要从旧机复制。

两台机器的知识库不会自动同步；不要同时覆盖写入同一份数据目录。

## 更新

先从托盘退出桌宠，备份个人数据，再在代码目录执行：

```powershell
Set-Location C:\Apps\12m
git pull --ff-only origin main
if ($LASTEXITCODE -ne 0) { throw '拉取失败，请处理提示后继续' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
if ($LASTEXITCODE -ne 0) { throw '构建失败' }
Start-Process -FilePath .\dist\ElymsPet.exe -ArgumentList '--panel' -WorkingDirectory (Resolve-Path .\dist).Path
```

如果之前重新构建导致 Git 提示 `dist` 中已跟踪文件发生变化，先确认本地修改，再用新的 clone 目录部署，或处理这些构建产物后重试；不要覆盖尚未备份的源代码修改。便携包用户将新版完整解压到新目录后启动即可，个人数据仍使用原账户的数据目录。

## 排错

```powershell
# 最近的运行错误（文件只在发生错误后出现）
Get-Content "$env:LOCALAPPDATA\ElymsDesktopPet\error.log" -Tail 80
# 默认模型服务的域名与 HTTPS 连通性
Resolve-DnsName api.deepseek.com
Test-NetConnection api.deepseek.com -Port 443
# 完整离线回归与界面预览（需 Node.js 与交互桌面）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Render
```

DNS 和端口连通只能证明网络通路，API 鉴权仍需在应用内测试。窗口不见时双击托盘图标或按 `Ctrl + Alt + E` 召回；热键会受远程桌面键盘转发设置影响。DLL 缺失时重新构建或完整解压便携包。日志可能包含个人输入，分享前请检查内容。
