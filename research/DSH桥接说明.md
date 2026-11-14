# 曳尔沐 → DSH 工作台：接入与验证记录

## 2026-11-10 修复与路径交接

- DSH 0.2.2-rc.1 启动被 web profile 内遗留的 `dsh-session-projection@0.2.1-rc.3` 阻塞。旧覆盖目录已移出插件搜索路径并备份，使用 DSH 自带的匹配版本。
- 失效的 `dsh-doraemon` 从 web profile 的 bundle/dependency 配置移除；旧 `web-ui-win-bash` 入口停用。标准预设提供原生文件和 PowerShell 工具。
- 桥接创建会话时解析并挂载 `standard` agent preset。新版 Web 将工具放到各会话的预设内；只调用裸 `agents.create()` 会缺少文件工具。老版本未提供预设服务时兼容原方式。
- 「打开 DSH」通过带桥接认证的 `GET /open` 获取本机浏览器入口，使用 DSH `connection.authenticatedUrl()`；访问凭证不放进任务、健康状态或日志。普通地址在新版 DSH 返回 401，不能直接用作首次打开入口。
- 每次从聊天或工作台委托任务，桌宠先刷新当前存档的派生文件，再以独立 `knowledge` 字段附带 9 个绝对路径：`SKILL.md`、`workflows.md`、`graph.json`、`profile.md`、`episodes.md` 和四类资料索引。切换存档后跟随当前存档，不依赖模型猜路径。
- 路径字段仅允许既定派生文件。资料不授予额外权限，流程草稿需核实，DSH 按任务读取且不修改派生文件；不传递存档主文件、连接配置或密钥。用户原任务仍保持原有长度限制。
- 自动测试覆盖路径存在、草稿边界、刷新、存档切换、提示词委托能力、HTTP 认证、路径越界拒绝、预设先挂载后发送任务。
- 真实只读会话成功读取本机 `workflows.md`、`graph.json`、`SKILL.md`，核对到 2 套流程、46 个知识节点、0 条关系，并识别两套流程均为待核实草稿；无需完整访问。浏览器认证入口返回 HTTP 200。

以下为旧版接入记录，其中裸会话创建、普通页面 URL 和旧 Windows 工具限制由上述实现替代。

整理日期：2026-11-09。记录 v1.5 新增的 DSH 工作台：桌宠如何把实际动手任务交给 DSH agent、插件如何暴露本机 API、权限与安全边界，以及本轮实测结果。

## 1. 目标

v1.4 之前，桌宠只有 DeepSeek 对话；她能“说”，但不能实际改文件。v1.5 增加两层：

- **桌面宠物**：新增「DSH 工作台」页；聊天里可以在回复末尾提出 `[[DSH_TASK]]` 任务块，由用户确认后执行。
- **DSH host 插件**：`dsh-elyms-bridge` 在本机起 loopback HTTP API，通过 `ctx.agents` 创建真实 DSH 会话、设置 permission、发送 followup、等待 idle、读取最后一条 assistant 文本。

桌宠不直接调用 DSH 内部服务，也不把 DSH token/密钥放进任务；它只读 `%LOCALAPPDATA%/ElymsDesktopPet/dsh-bridge.json` 的握手信息，再带随机 token 请求 `127.0.0.1`。

## 2. 两个插件

| 插件 | 作用 |
| --- | --- |
| `dsh-elyms-pet`（`dsh-plugin/`） | DSH web profile 启动时拉起/召回 `dist/ElymsPet.exe`；不参与工作台任务 |
| `dsh-elyms-bridge`（`dsh-bridge-plugin/`） | 提供工作台 loopback API，创建真实 DSH 会话并返回结果 |

两者都通过 `~/.dsh/profiles/web/node_modules` 的 junction + `~/.dsh/profiles/web/cordis.patch.yml` 的 insert 挂载；`install-dsh-plugins.ps1` 负责补齐。DSH 开着时 HMR 会热加载；关闭时下次启动生效。

## 3. 桥接 API

握手文件：

```json
{
  "version": 1,
  "host": "127.0.0.1",
  "port": 52960,
  "token": "48 个随机 hex 字符",
  "pid": 21656,
  "startedAt": "2026-11-08T21:38:31.279Z",
  "workspace": "D:\\code\\12m",
  "webUrl": "http://127.0.0.1:3080"
}
```

所有请求带 `Authorization: Bearer <token>`。未授权请求返回 401。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/health` | 桥接、工作区、活动任务数 |
| GET | `/tasks` | 最近 20 个任务摘要 |
| POST | `/task` | 创建任务，202 返回 jobId |
| GET | `/task/:id` | 状态、text、progress、sessionId、tools、seconds |
| POST | `/task/:id/cancel` | 请求取消 |

`POST /task` 接受 `prompt`、`permission`、`cwd`、`title`。`cwd` 默认工作区，且必须在工作区内（除非配置 `allowAnyCwd`）。`permission` 默认 `workspace-write`，可选 `read-only`、`danger-full-access`。

## 4. 会话执行流程

桥接插件当前直接使用 `ctx.agents`（而不是 `ctx.typertGateway`）：

1. `ctx.agents.create({ sessionId: randomUUID(), meta: { cwd }, agentOptions: { provider, model, reasoningEffort } })`
2. 用 `ctx.commands.execute(agent, '/permission <permission>', [], signal)` 设置权限；
3. 构造一条带 `id`/`role`/`source` 的 UserMessage，`agent.followup(message)`；
4. 每秒读 `ctx.sessions.get(id).deriveMessages()` 更新进度；
5. `agent.whenIdle()` 结束后取最后一条含文本的 assistant 消息；
6. 返回 text、tools、seconds、sessionId；插件 unload 时 dispose 仍存活的 handle。

为什么不用 gateway：`ctx.typertGateway.invoke()` 要求显式 `peer`（`InvokeRemoteRequest.peer`），直接调用会报 `Cannot read properties of undefined (reading 'signal')`。`ctx.agents` 是公开的编程接口，正好满足“创建会话 + 驱动 + 取结果”，也不需要伪造 Peer。

## 5. 提示词与权限

桥接把任务包装成一次性任务：

```text
你是由桌面宠物「曳尔沐」转交任务的 DSH 工作台执行者。
请直接完成下面的工作，不要反问、不要等待用户确认……
重要限制：
1. 不要操作桌面 GUI，不要打开可见窗口，不要控制鼠标或键盘，不要截图，不要使用浏览器工具打开页面。
2. 不要读取、输出或传播 API Key、密码、令牌等秘密。
3. 不要运行会破坏系统或删除工作区之外文件的操作。
完成后用简洁中文总结：改了什么、如何验证、还有什么没做或需要用户决定。
任务：……
```

- 桌宠侧默认 `workspace-write`；只有用户在设置/工作台勾选「允许完整访问」才用 `danger-full-access`。
- 聊天提案默认弹确认框；`DshAutoRun` 可以跳过确认。
- DSH 的 permission 是实际权限闸门；提示词只是行为约束，不是沙箱。

## 6. 桌宠侧

- `src/Dsh.cs`：握手解析、HTTP 客户端、任务标记解析、`DshWorkbench` 轮询与状态。
- `src/DshPanel.cs`：工作台页；任务输入、权限、进度、结果、最近任务。
- `src/Persona.cs`：DSH 在线时注入能力说明与 `[[DSH_TASK]]` 格式；离线时明确禁止输出标记。
- `src/ChatService.cs`：解析可见回复里的任务块、弹确认、启动桥接任务、完成后把结果和合成上下文写回聊天。
- `src/ControlWindow.cs` / `PetWindow.cs`：新增「DSH 工作台」标签页、右键/托盘入口、设置项。

## 7. 实测结果

### 7.1 桥接加载

- 通过 profile patch insert 后 HMR 热加载，`dsh-bridge.json` 出现，端口随机、token 48 位。
- 未授权 `/health` 返回 401。
- 加载桥接时 `Get-Process ElymsPet` 为空，说明没有启动/干扰桌宠窗口。

### 7.2 只读任务

- 任务：查看 `README.md` 前 60 行并总结；`permission=read-only`。
- 结果：`done`，10.1 秒，3 次工具；DSH agent 报告 read-only 模式下 `win_bash` 因 `SANDBOX_UNSUPPORTED` 被拒，会话工具集也没有独立文件读取工具；它没有编造内容，而是如实说明限制。这证明桥接能创建会话、收工具结果并取回最终文本。

### 7.3 工作区可写任务

- 任务：只读查看 README 前 20 行并报告版本号；`permission=workspace-write`；明确要求不修改文件。
- 结果：`done`，85.8 秒，9 次工具；DSH agent 通过浏览器 `view-source:file:///D:/code/12m/README.md` 读取了内容，确认版本 v1.4，并报告未修改文件。工作区可写权限下 `win_bash` 仍被沙箱拒绝，但会话可用其他工具完成读取。

### 7.4 C# 端到端

- `ElymsPet.exe --dsh-test`：PASS，`health=ok`，`workspace=D:\code\12m`，会话创建成功，`status=done`，0—2 秒，`reply=接通`，`tools=0`。结果写入 `tests/dsh-test.txt`。
- 重复执行通过，说明握手轮询、JSON 映射和错误处理可用。

### 7.5 主动提案

- `ElymsPet.exe --chat "我想让你实际动手，给 D:\code\12m 的 README.md 末尾加一行 v1.5 DSH 工作台说明……"`：PASS，1.69 秒，`529+178` tokens；回复先说明“真正执行的是 DSH”，末尾输出包含目标、目录、验收标准的 `[[DSH_TASK]]` 块。
- 自检覆盖 `DshTask.Extract`：标记被剥离、可见回复保留、任务文本正确。

### 7.6 真实窗口验收（用户允许弹窗后）

- 启动 `dist/ElymsPet.exe --panel`；「DSH 工作台」显示桥接在线、工作区 `D:\code\12m`；「测试连接」显示正常。
- **工作台手动任务**：勾选「完整访问」，提交在 `tests\dsh-live` 创建 `proof.txt` 的任务。状态从排队 → 正在做 → 完成；**11.1 秒、5 次工具**；文件内容逐字节正确。首次完成后连接行停在“已提交给 DSH，正在等它接线…”，修复 `DshPanel.Finished` 后，第二次 `status.txt` 任务显示「任务完成 · 6.1 秒 · 工具 2 次」，文件内容 `status ok`。
- **聊天提案“是”**：DeepSeek 回复先说明真正执行的是 DSH，随后弹出确认框，显示任务、完整访问警告、「是(Y) / 否(N)」。点“是”后 **10.4 秒、4 次工具**，创建 `chat-ok.txt`，内容 `chat popup ok`；聊天出现 DSH 结果气泡。首次输入框下方仍显示 DeepSeek 耗时，修复 `ChatService.LastHint` 后，后续任务显示 `DSH · 6.1s · 工具 2 次`。
- **聊天提案“否”**：再次提交创建 `should-not-exist.txt`，在确认框点“否”；目录没有该文件，聊天显示「好，先不做。想执行时可以在「DSH 工作台」里手动提交。」。
- **截图**：`preview/dsh-workbench.png`、`preview/dsh-workbench-result.png`、`preview/dsh-proposal.jpg`、`preview/dsh-chat-result.png`、`preview/dsh-declined.png`。
- 任务使用 `danger-full-access` 完成；DSH agent 的最终回复明确说明未打开窗口、未使用浏览器、未触碰桌面 GUI，本轮未观察到 GUI 操作。提示词约束仍不是系统级沙箱。

### 7.7 自动测试

`ElymsPet.exe --self-test`：57 项 PASS，新增：
- DSH 任务标记解析；
- DSH 在线/离线时 Persona 是否输出能力说明与标记格式；
- 桥接握手 JSON 解析。

`ElymsPet.exe --ui-smoke`：PASS；在临时数据目录、不 Show 任何窗口的情况下构造桌宠、陪伴面板、DSH 工作台与浮动框，切换全部 7 个标签页并 `UpdateLayout`，未抛异常；结果在 `tests/ui-smoke.txt`。

## 8. 安全边界

- 只绑定 `127.0.0.1`，token 每次启动随机，只写当前用户 `%LOCALAPPDATA%`。
- 握手文件和任务正文都不含 DeepSeek API Key；任务提示词要求 agent 不读取/输出密钥。
- 工作区白名单默认只允许配置的 workspace；`cwd` 越界返回 403。
- 写权限由 DSH 的 `/permission` 设置；完整访问需要用户显式勾选/确认。
- 桥接不是沙箱：同一个用户下的其它本机进程如果读到握手文件，也能调用；DSH agent 的工具与文件访问仍由 DSH 权限系统决定。
- 任务提示词“不操作桌面”不是系统级禁止；DSH 的浏览器/工具仍可能被使用。实测工作区可写任务使用了浏览器 `view-source`（说明需要更严格限制时，应只给 `read-only`，或关闭相关工具）。

## 9. 未完成/未实测

- 取消当前任务按钮、并发任务上限、DSH 关闭/重启时的任务恢复、端口冲突没有逐项实测。
- 「DSH 任务自动执行」开关没有在真实任务里打开验证；默认仍关闭。
- 没有流式输出；长任务只轮询进度，最终一次性显示结论。
- 主动提案依赖模型是否按提示输出 `[[DSH_TASK]]`；本轮样本成功，不能保证每次。工作台手动提交是稳定入口。
- 工作区可写权限下 `win_bash` 在本机 DSH 组合中仍被 `SANDBOX_UNSUPPORTED` 拒绝；实际文件修改使用了 `danger-full-access`。
- 完整访问下 DSH agent 权限很大；提示词要求不操作桌面只是行为约束。混合 DPI 多屏、不同 DSH 工具组合、其它模型路由未覆盖。

## 10. 文件

- `dsh-bridge-plugin/`：host 插件、README、bundle patch。
- `dsh-plugin/`：桌宠自动拉起插件。
- `install-dsh-plugins.ps1`：安装/修复脚本。
- `src/Dsh.cs`、`src/DshPanel.cs`：客户端与工作台 UI。
- `tests/dsh-test.txt`、`tests/chat-test.txt`：实测输出。
