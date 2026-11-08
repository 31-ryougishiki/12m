# 曳尔沐桌宠接入 DeepSeek V4.1 Flash：研究与实现记录

整理日期：2026-11-09（中国标准时间）。本文记录 v1.1 的接口核对、请求参数、密钥处理、角色提示和实测结果，方便以后换模型或排查问题。

## 1. 模型与接口核对

用项目提供的密钥请求 `GET https://api.deepseek.com/models`，服务端返回两个模型：

| 接口模型名 | 展示名 | 上下文 | 最大输出 | 输入模态 | 推理强度 |
| --- | --- | --- | --- | --- | --- |
| `deepseek-flash` | DeepSeek-V4.1-Flash | 1,048,576 | 393,216 | 文本、图片 | low / high / max，默认 high |
| `deepseek-v4-pro` | DeepSeek-V4-Pro | 1,048,576 | 393,216 | 文本 | low / high / max，默认 high |

用户口语里的“deepseekv4.1-flash”对应接口模型名 `deepseek-flash`。官方文档说明它由 DeepSeek-V4.1-Flash-1017 提供服务；旧名 `deepseek-v4-flash`、`deepseek-v4-flash-vision-exp` 仍被接受，但会路由到同一模型并按 Flash 价格计费。桌宠默认写 `deepseek-flash`，避免以后旧名下线。

接口是 OpenAI 兼容格式：

- Base URL：`https://api.deepseek.com`
- 对话：`POST https://api.deepseek.com/chat/completions`
- 认证：`Authorization: Bearer <API_KEY>`
- 文档：https://api-docs.deepseek.com/api/create-chat-completion

密钥请求 `GET https://api.deepseek.com/user/balance` 返回 `is_available: true`，当时余额为 466.72 CNY，可以正常调用。

## 2. 关键参数选择

### 2.1 默认关闭思考模式

桌宠聊天要求响应快。同一句“今天有点累，陪我聊聊”，实测：

| 配置 | 耗时 | completion tokens | 说明 |
| --- | --- | --- | --- |
| `thinking: {type: disabled}` | 0.57 s | 10 | 直接回答，适合桌宠 |
| `thinking: {type: enabled}`, `reasoning_effort: low` | 1.48 s | 141（其中 131 推理） | 更慢，推理 token 也计费 |
| `thinking: {type: enabled}`, `reasoning_effort: high` | 1.42 s | 98（其中 83 推理） | 本次样本没有更快，且更贵 |

因此默认发送：

```json
{
  "model": "deepseek-flash",
  "messages": [ ... ],
  "max_tokens": 600,
  "temperature": 1.1,
  "thinking": { "type": "disabled" }
}
```

设置页的“深度思考”勾选后改为：

```json
{
  "model": "deepseek-flash",
  "messages": [ ... ],
  "max_tokens": 2048,
  "temperature": 1.1,
  "thinking": { "type": "enabled" },
  "reasoning_effort": "low"
}
```

`max_tokens` 在思考模式下放大到 2048，避免推理 token 占满后没有正文。正文最多保留 4000 字。

### 2.2 不做流式

桌宠气泡和面板都不需要逐字输出；非流式实现更简单，也避免 SSE 解析、断流重试和 UI 频繁刷新。实测非思考模式 0.6—1.3 秒，可接受。

## 3. 工程实现

新增 `src/Chat.cs`，不引入第三方包，继续用 Windows 自带 .NET Framework 4.8 和 `csc.exe`：

- `DeepSeek.Complete`：用 `HttpWebRequest` 发请求，显式启用 TLS 1.2，设置 45 秒超时；支持系统代理；成功与 HTTP 错误都解析统一 JSON。
- `DataContractJsonSerializer`：请求和响应用 `[DataContract]` 类映射；未知字段忽略；`content`、`reasoning_content`、`usage` 都能读取。
- `ChatSession`：保存最近 16 条（8 轮）上下文；每次构造“系统提示 + 历史 + 本轮用户消息”。面板重开时从内存历史恢复显示。
- 异步：面板把请求放到线程池，完成后通过 WPF `Dispatcher.BeginInvoke` 回 UI 线程；请求期间禁用输入并显示“正在接通 DeepSeek…”。
- 气泡：等待时显示“正在接通”，收到后把回复截到 120 字放入气泡，完整内容留在面板记录里。

## 4. 角色系统提示

系统提示每次请求都会带上，主要内容：

- 身份：曳尔沐，E.T.D 六队副队长，现在住在用户桌面上。
- 身份边界：只是借用了小 Y 的形象，不是小 Y 长大的样子；不把 Y 的生平、Y 与 Exe 的往事说成自己的亲历。
- 性格与语言：亲切、机灵、通信 / 信号 / 缓存 / 来电的比喻；简体中文，一般 1—3 句、40—80 字。
- 能力边界：不能操作电脑、看不到屏幕、不能打电话、不能替用户做决定；被问到时坦白。
- 事实边界：不声称官方台词，不确定的剧情说不清楚，不编造。
- 安全：面对严重自伤表达，劝用户联系信任的人或当地紧急援助，不假装专业帮助。
- 当前信息：日期、星期、本地时间、相处印记、能量缓存、是否睡眠 / 专注 / 暂停。

系统提示不包含 API Key、便签内容或聊天以外文件。提示约 350 tokens；由于内容稳定，可命中 DeepSeek 的上下文缓存，降低重复成本。

## 5. 密钥处理

查找顺序：

1. `settings.json` 的 `ApiKey`
2. 环境变量 `DEEPSEEK_API_KEY`
3. `%LOCALAPPDATA%/ElymsDesktopPet/api-key.txt`
4. EXE 同目录的 `api-key.txt`

`api-key.txt` 支持 `sk-...` 单行，也支持 `DEEPSEEK_API_KEY=sk-...`；每行一个，`#` 开头为注释。`--set-key` 写入设置文件，`--clear-key` 清除设置文件里的密钥。

安全措施：

- 源码、EXE、ZIP 都不含密钥；本机密钥只在 `%LOCALAPPDATA%` 的设置文件里。
- 界面只显示 `sk-…` + 后四位；日志只写 HTTP 状态和错误摘要，不写密钥、不写聊天正文。
- 粘贴 `Bearer sk-...` 会自动去掉前缀；拒绝含换行的密钥，避免请求头注入。
- 设置文件是明文 JSON，和大多数桌面应用一样依赖 Windows 用户目录权限；不要把 `settings.json` 发给别人。

## 6. 错误分类

| 情况 | 界面提示 |
| --- | --- |
| 没有密钥 | 还没有配置 API Key。打开「设置」填入密钥，就能开始在线对话。 |
| HTTP 401 / 403 | 密钥没有通过验证，请在「设置」里检查 API Key。 |
| HTTP 402 | 账户余额不足，暂时只能先用离线对白陪你。 |
| HTTP 404 | 接口地址似乎不对，请检查「接口地址」。 |
| HTTP 429 | 请求有点频繁，等几秒再发一次就好。 |
| HTTP 400 | 这次请求被拒绝了。（附服务端说明） |
| HTTP 5xx | DeepSeek 服务暂时不太稳定，稍后再试。 |
| 超时 | 等了好久都没有回应，可能是网络太慢。 |
| 网络失败 | 网络没有接上。检查一下网络或代理，我再试一次。 |
| 空回复 / 截断 | 这次没有收到文字回复 / 回复被截断了。 |

任何失败都会调用 `Dialogue.Reply` 生成离线回复，写入会话历史，并在面板里标为“曳尔沐（离线）”。

## 7. 实测结果

自动检查：`ElymsPet.exe --self-test` 通过 27 项，新增 DeepSeek 相关检查包括：

- Base URL 归一化与校验、密钥掩码；
- 角色提示包含身份边界与能力边界，且不含密钥；
- 正常回复 JSON、API 错误 JSON、空回复；
- 16 条上下文上限与清空；
- 设置文件密钥优先、`api-key.txt` 回退；
- 旧版设置缺少 AI 字段时补齐默认值。

真实接口：

- `--api-test`：PASS，`deepseek-flash`，0.60—0.85 s，`23+1` tokens，回复“接通”。
- `--chat "介绍一下你自己吧，顺便说说你和 Y 是什么关系"`：PASS，1.04 s，`352+54` tokens，回复明确区分“借用小 Y 的形象”和“不是 Y 长大的样子”。
- 错误密钥：HTTP 401，界面提示“密钥没有通过验证”，退出码 1；恢复密钥后重新 PASS。
- 思考模式：`--chat` 1.92 s，`373+141` tokens，回复正常，验证 `thinking` / `reasoning_effort` 序列化可用。

真实窗口：

- 面板显示“● 已接入 DeepSeek V4.1 Flash · 密钥保存在本机”，密钥提示为 `sk-…023e`。
- Enter 发送成功；回复“靠这条桌面上的信号线呀……”耗时 1.1 s、46 tokens；气泡同步显示。
- “测试连接”返回“● 连接正常 · deepseek-flash · 0.6s · 回复：接通”。
- 设置页可切换模型、地址、深度思考和清空记忆。

## 8. 已知限制

- 不做流式输出；长回复要等完整响应。
- 不支持图片 / 视觉识屏；虽然 `deepseek-flash` 接受图片输入，桌宠没有采集屏幕，也没有上传截图。
- 没有工具调用；模型不能真的操作电脑。
- 不显示余额和实时费用；需要到 DeepSeek 平台查看。
- 角色一致性依赖提示词，模型仍可能偶尔说出不符合设定的内容；身份边界不能靠提示词百分百保证。
- 密钥以明文保存在本机设置文件，未使用 DPAPI 加密。

## 9. 参考来源

- [DeepSeek API：Your First API Call](https://api-docs.deepseek.com/)
- [Chat Completions API](https://api-docs.deepseek.com/api/create-chat-completion)
- [Lists Models](https://api-docs.deepseek.com/api/list-models)
- [DeepSeek V4.1 Flash 公告（微信公众号）](https://mp.weixin.qq.com/s/qg0NU3NNUbp1co2PdkAPAg)
- 本机 `GET https://api.deepseek.com/models`、`GET /user/balance` 与聊天接口实测结果
