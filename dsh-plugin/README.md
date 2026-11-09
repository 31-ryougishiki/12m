# dsh-elyms-pet

把 [曳尔沐桌面桌宠](../dist/ElymsPet.exe) 接成 DSH 的 host 插件：DSH 启动 `web` profile 时自动启动桌宠；如果桌宠已经在运行但被隐藏或开启了鼠标穿透，再启动一次会触发桌宠自己的单实例「召回」，把它重新显示到桌面并恢复操作。

这是 **DSH 宿主侧插件 + Windows 桌面进程**，不是 `@linxin666/dsh-pet` 那种网页内嵌宠物。两者可以同时存在：网页宠物在 DSH 页面里，曳尔沐在 Windows 桌面上。

## 工作原理

`lib/index.js` 是 Cordis host 插件。profile 加载它后，它会：

1. 等 `startDelayMs`（默认 1200 ms），避免和 DSH 自己的启动抢资源；
2. 启动 `ElymsPet.exe`（默认解析到本仓库 `dist/ElymsPet.exe`）；
3. 不注入任何 DSH 服务，不改 DSH 源码，不渲染 Web 内容。

`ElymsPet.exe` 自带单实例互斥锁和 `Local\ElymsDesktopPet.Recall` 事件。如果第二次启动时桌宠已经在运行，新进程只会发出召回信号然后退出，所以每次 DSH 打开都能把藏起来的曳尔沐叫回来。

## 安装到当前 web profile

仓库提供了 `install-dsh-plugins.ps1`（见项目根目录），它会同时安装本插件和 `dsh-elyms-bridge`，并：

- 在 `~/.dsh/profiles/web/node_modules/dsh-elyms-pet` 建立指向本目录的 junction；
- 备份 `~/.dsh/profiles/web/cordis.patch.yml`；
- 在 profile patch 中写入一条 `elyms-pet` 的 insert 记录（只写一次，重复执行不会重复添加）；
- 如果 DSH 的 HMR 正在运行，保存后约 2—3 秒会热加载并启动桌宠；没有 HMR 时下次启动 DSH 生效。

也可以手动安装：

```powershell
# 1. 让 profile 能解析这个包（目录 junction，不需要管理员权限）
cmd /c mklink /J "$env:USERPROFILE\.dsh\profiles\web\node_modules\dsh-elyms-pet" "D:\code\12m\dsh-plugin"

# 2. 在 ~/.dsh/profiles/web/cordis.patch.yml 末尾追加
# - insert:
#     - id: elyms-pet
#       name: 'dsh-elyms-pet'
#       config:
#         enabled: true
#         exePath: 'D:/code/12m/dist/ElymsPet.exe'
```

> 不要把 `dsh-elyms-pet` 同时加进 `dsh.profile.bundles` 和 profile patch 的 insert；那样会挂载两份。二选一：本仓库用的是 profile patch insert；如果以后把它作为 bundle 安装，就删掉 patch 里的 insert。

## 配置

插件 entry 的 `config` 支持：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `enabled` | `true` | 总开关。`false` 时不启动桌宠。 |
| `exePath` | 自动 | `ElymsPet.exe` 的绝对路径；相对路径按插件包目录解析。 |
| `args` | `[]` | 传给 EXE 的参数，例如 `["--panel"]`。 |
| `startDelayMs` | `1200` | 启动延迟，0—60000 ms。 |
| `leaveRunning` | `true` | 插件卸载时是否保留桌宠。默认保留；设为 `false` 时插件 dispose 会结束它。 |

默认查找顺序：`config.exePath` → 环境变量 `ELYMS_PET_EXE` → 插件包上一级的 `dist/ElymsPet.exe`。

## 验证

```powershell
# 1. 组合后的配置树里出现 elyms-pet
dsh --profile web --dump-config | Select-String "elyms-pet"

# 2. 看桌宠进程
Get-Process ElymsPet

# 3. 看 DSH 的 HMR 日志
Get-Content "$env:LOCALAPPDATA\Temp\dsh-*.log" -Tail 50
```

如果桌宠没出现，依次检查：`ElymsPet.exe` 路径、DSH 是否是 `web` profile、HMR 是否启用、`~/.dsh/logs` 或启动终端里的插件加载错误。

## 卸载

```powershell
# 从 cordis.patch.yml 删除 id: elyms-pet 的 insert 段
Remove-Item "$env:USERPROFILE\.dsh\profiles\web\node_modules\dsh-elyms-pet" -Force
```

`leaveRunning` 默认是 `true`，卸载插件不会关闭桌宠；需要关闭时退出托盘里的曳尔沐即可。

## 限制

- 只支持 Windows；启动的是 WPF 桌面程序，不进入 DSH 的 Web 页面。
- 不负责桌宠的关闭、崩溃重启或更新；它只负责「DSH 启动时拉起/召回」。
- 通过绝对路径/相对路径启动本机 EXE，等同于用户自己双击 `ElymsPet.exe`，拥有当前用户权限。
- DSH 的 `headless`、`sdk`、`acp` profile 默认没有挂这条 patch，不会弹桌宠；只有安装了它的 profile 会启动。
