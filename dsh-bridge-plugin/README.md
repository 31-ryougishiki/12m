# dsh-elyms-bridge

让曳尔沐桌面桌宠调用 DSH 的 **本机工作台 API**：桌宠提交一个任务，本插件在 DSH Host 里创建真实会话、设置权限、发送提示词，等 agent 完成后把最后一段中文总结和工具次数返回给桌宠。

它是 host-only 插件，**不打开窗口、不渲染 Web 内容、不控制桌面**。桌宠侧的确认按钮才是写权限的人为闸门。

## 工作方式

1. 插件在 `127.0.0.1` 随机端口启动一个只接受本机请求的 HTTP 服务。
2. 把端口与随机 token 写入 `%LOCALAPPDATA%/ElymsDesktopPet/dsh-bridge.json`。
3. 桌宠读取握手文件，用 `Authorization: Bearer <token>` 调用。
4. `POST /task` 通过 `ctx.typertGateway` 调 `session/create`，再用 `session/rename`、`/permission`、`session/prompt` 启动会话。
5. 等 `agent.whenIdle()` 结束后，从 `session.deriveMessages()` 取最后一条 assistant 文本。
6. 任务提示词明确要求 agent：不要打开 GUI 窗口、不要控制鼠标键盘、不要读取或输出密钥。

## 安装到 web profile

推荐直接运行项目根的 `install-dsh-plugins.ps1`，它会同时挂载 `dsh-elyms-pet` 与本插件。手工安装：

```powershell
# 1. 目录 junction
New-Item -ItemType Junction `
  -Path "$env:USERPROFILE\.dsh\profiles\web\node_modules\dsh-elyms-bridge" `
  -Target "D:\code\12m\dsh-bridge-plugin"

# 2. 在 ~/.dsh/profiles/web/cordis.patch.yml 末尾加入
# - insert:
#     - id: elyms-bridge
#       name: 'dsh-elyms-bridge'
#       config:
#         enabled: true
#         workspace: 'D:/code/12m'
#         defaultPermission: 'workspace-write'
```

DSH 的 HMR 会热加载这个新 entry。它不会启动桌宠；桌宠的自动启动由单独的 `dsh-elyms-pet` 插件负责。

## HTTP API

所有请求都要带 `Authorization: Bearer <handshake.token>`。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/health` | 进程、工作区、活动任务数 |
| GET | `/tasks` | 最近 20 个任务摘要 |
| POST | `/task` | 创建任务，202 返回 `{jobId, title, status}` |
| GET | `/task/:id` | 查询状态、结果、进度、会话 id、工具次数 |
| POST | `/task/:id/cancel` | 请求取消当前 agent |

`POST /task` 请求：

```json
{
  "prompt": "给 D:\\code\\12m 的 README 加一行 v1.5 说明",
  "permission": "workspace-write",
  "cwd": "D:/code/12m",
  "title": "更新 README"
}
```

`permission` 可为 `read-only` / `workspace-write` / `danger-full-access`，默认由 profile 配置决定。`cwd` 默认使用配置的 workspace；不在 workspace 内会被拒绝，除非配置 `allowAnyCwd: true`。

查询结果：

```json
{
  "id": "…",
  "status": "done",
  "sessionId": "…",
  "text": "我修改了 …，并运行了 …",
  "tools": 6,
  "seconds": 42.3,
  "error": "",
  "note": ""
}
```

## 配置

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `enabled` | `true` | 总开关 |
| `host` | `127.0.0.1` | 只建议本机回环地址 |
| `port` | `0` | 0 表示随机端口 |
| `workspace` | `process.cwd()` | 任务默认工作区，也是 cwd 白名单根 |
| `defaultPermission` | `workspace-write` | 未指定权限时的默认值 |
| `maxActive` | `1` | 同时运行的任务数，1—4 |
| `maxJobs` | `30` | 内存中保留的最近任务数 |
| `taskTimeoutMs` | `1800000` | 单任务超时，30 秒—2 小时 |
| `allowAnyCwd` | `false` | 是否允许 cwd 超出 workspace |
| `webUrl` | `http://127.0.0.1:3080` | 桌宠「打开 DSH」按钮使用的地址 |

## 安全边界

- 只绑定 `127.0.0.1`，随机 24 字节 token 放在当前用户目录；token 不进入日志。
- 默认工作区白名单；未确认的桌宠任务不会被 bridge 自己启动。
- 写权限通过 DSH 的 `/permission` 命令设置；桌面宠物在提交前会弹出确认，说明会实际读写文件。
- agent 仍然拥有 DSH 会话的权限；bridge 不是沙箱。建议先用 `read-only` 测试，再按需放开 `workspace-write`。
- 任务提示词要求不控制 GUI、不打开窗口、不读取密钥，但这只是提示词约束，不是系统级隔离。

## 卸载

从 profile patch 删除 `id: elyms-bridge` 的 insert 段，删除 node_modules junction，重启或让 HMR 重载即可。插件 dispose 会关闭 loopback server 并删除自己的握手文件。
