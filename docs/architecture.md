# 开发与扩展约定

本次重构保持 .NET Framework 4.8 / C# 5、WPF 与现有存档格式。版本号只在 `src/AppVersion.cs` 定义，程序集信息、界面、HTTP User-Agent 和发布包名由此生成。插件自己的 package 版本与桥接协议版本独立管理。

## 依赖方向

```text
Program → PetWindow → PetServices（组装与释放）
                      ├─ ChatService → ChatSession / ConversationRequest / ConversationContext
                      ├─ MemoryService → IRepository<MemoryBook> / IClock / MemoryStore
                      │                                        └─ MemoryRetrieval
                      ├─ ArchiveService → IRepository<ArchiveBook>
                      ├─ AutonomyService → AutonomyRequest / AutonomyPlanner / AutonomyPolicy
                      ├─ BiliService → BiliExplorer / BiliApi
                      └─ DshWorkbench → DshApi
业务服务 → WorkScope → IWorkScheduler
业务服务 → PetFeedback（Act / Say / Wake）
ControlWindow → CompanionStatusViewModel
```

六个业务服务不再引用 `PetWindow`。`PetServices` 只在窗口入口组装依赖，服务不接收这个容器。新增功能需要什么依赖，就通过构造函数明确传入；不要把窗口、容器或全局字典传进服务再自行查找。

窗口负责 MessageBox、拖动、热键与动画。DSH 确认由传入的确认函数处理，服务不用创建对话框。面板每秒刷新派生状态，属性没有变化就不发 WPF 通知；40ms 动画循环只负责桌宠。页签以 `PanelTab` 标识，增减或调换页签时不改其它功能的数字索引。

## 后台任务

每个服务、登录窗口和联网页拥有自己的 `WorkScope`，关闭时 Dispose。作用域捕获后台异常并回到 UI 线程恢复状态；排队工作和排队回调都检查当前代次，取消或关闭后丢弃旧结果。B站草稿、发布与删除使用各自的失败回调，不会错误结束另一项浏览任务。

取消会使排队工作和结果交付失效，并通过 `NetworkCancellation` 中止作用域内登记的 DeepSeek / Web HTTP 请求。普通聊天支持流式回复与停止，断流结果不会当作完整回答保存。B站和 DSH 的传输没有接入这套 HTTP 中止；DSH 的明确取消仍走原取消接口。取消本机等待不承诺撤销远端已接受的动作。

工作线程使用捕获的字符串、对话消息或 `PetState.Snapshot()`，不读取 UI 正在修改的设置。自主活动在规划返回、执行动作之前再次检查开关、睡眠、专注和用户任务；Dispose 后的规划结果不会产生聊天、笔记或 DSH 委托。

## 上下文与存储

- `ConversationRequest` 在每轮生成只读提示词，分别处理用户输入、当轮召回和网页证据。普通聊天、显式联网和自动联网都传当轮记忆，不再在 ChatSession 上挂一个会残留的 MemoryContext。
- `ConversationContext` 负责预算与历史选择。用户原始输入限制不再二次截断网页证据；来源要求在证据限长之后保留。ChatSession 只保存用户原话和回答。
- Memory 的模型、标记解析、合并操作、检索评分和服务分别维护。`MemoryMutation` 区分新增、合并更新和拒绝；新增计数为零的合并也保存并通知面板。关闭记忆会阻止自动写入，手动确认、删除仍可用。
- 记忆、聊天、存档、活动日志使用传入的仓库；测试无需修改真实数据目录。`JsonFile` 统一主文件/备份回退、原子替换、进程内写入串行化和唯一临时文件。写入失败在服务中保留错误，聊天状态会提示未持久化。
- 路径继续使用 `memories.json`、`chat-history.json`、`archives/archives.json`、`autonomy-journal.json`。没有数据库迁移，也没有更改 DataMember 名称。状态迁移仍由原 Schema 逻辑处理。
- 预览使用内存仓库，不加载账号、不读取真实聊天、不启动自主计时器，也不执行后台联网任务。

`Storage`、ApiKey、BiliAuth 与 DshApi 的本机入口暂保留静态兼容接口；`PetState` 仍是既有持久化 DTO。下一步若要增加多账号、多角色或替换网络供应商，可逐项为这些边界引入实例接口。没有为了这次拆分改变现有数据模型或重写全部 WPF 页面。

设置入口保存 API Key 时使用当前 Windows 用户的 DPAPI，内存中的 `ApiKey` 仍供请求使用，磁盘通过 `ProtectedApiKey` 存储。读取旧明文设置时自动迁移，同时加密旧备份；迁移到另一台电脑或 Windows 账号后需重新填写密钥。独立的 `api-key.txt` 和环境变量继续按既有优先级支持，由用户管理。

## DSH 桥接

`job-registry.js` 负责同步准入、活跃任务名额与历史淘汰。HTTP 读完并验证 body 后才调用 `tryAdd`，检查与预留之间没有 await。任务完成或失败释放名额；插件停止后拒绝迟到的请求。创建 agent 或设置权限之后再检查取消，避免已经取消的任务继续发送 prompt。

## 构建与验证

```powershell
./build.ps1                         # Windows 自带 Framework 编译器，无需安装 SDK
./build.ps1 -Tests                  # 额外构建独立测试 EXE
./test.ps1                         # 离线 C# + Node 本机假主机测试
./test.ps1 -Render                 # 额外渲染 UI 预览
./package.ps1                      # 检查、全新暂存目录、复制依赖、哈希清单、ZIP
```

`ElymsPet.csproj` 与 `tests/ElymsPet.Tests.csproj` 可由安装 .NET Framework 4.8 Developer Pack 的 Visual Studio/MSBuild 打开和编译。当前机器没有 .NET SDK/Developer Pack，本次实际使用已安装的 Framework 编译器验证；项目文件和脚本都自动包含各自目录下的 C# 文件，不再手工登记新增源码。

测试源码位于 `tests/unit/`，原 101 项检查已从生产入口移到独立测试程序；另有 64 项回归覆盖聊天复制入口、人设资源与入口、联网回包上下文、响应元数据/截断、标记容错、合并落盘、写入开关、当轮上下文、证据尾部、任务异常/取消/关闭、自主规划迟到与顺延、AI 开关门控、预览隔离、事件释放、并发 JSON 写入与备份恢复。桥接测试位于 `dsh-bridge-plugin/test/`，使用 Node 内建测试工具和假 DSH 主机，包含同时提交两个未完成 body 的 HTTP 请求。

测试与渲染输出写入被忽略的 `artifacts/`；仓库原有截图、验收记录和历史便携包保留。在线诊断命令仍在 `DiagnosticCommands.cs`，不会被 `test.ps1` 调用。离线检查不能证明真实 AI、B站登录/评论和实际 DSH 主机当前可用。

发布脚本从源码插件复制，不使用可能过期的 `dist/` 插件副本。默认不替换正在运行的 EXE、不自动安装插件。便携包带独立测试程序，`--self-test` 兼容入口仍可使用。

## 数据保存分工

本机数据根目录为 `%LOCALAPPDATA%/ElymsDesktopPet/`。现有格式继续保留；新增讲解保存独立于图谱和诊断日志，避免长文本参与每次图谱编辑、修订摘要及云同步。

| 内容 | 当前保存位置 | 用途与恢复方式 |
| --- | --- | --- |
| 桌宠状态、显示/功能设置、便签、专注计时、API配置 | `settings.json` | 用户设置；API密钥使用当前 Windows 用户的 DPAPI 保存，内存解密供调用 |
| 聊天原话和回答 | `chat-history.json` | 聊天历史和上下文，不存模型思考过程 |
| 长期事实、偏好、习惯等记忆 | `memories.json` | 与原始聊天分开维护的记忆条目 |
| 存档、访谈、画像、经验、工作流程、资料卡、知识图谱 | `archives/archives.json` | 主要业务数据；节点的介绍、代码、来源、学习目标、已保存复述、术语/流程翻译都随存档保存 |
| 最近一次完整节点讲解 | `learning/explanations/<存档哈希>/<专题哈希>/<节点哈希>/latest.json` | 自动恢复阅读；保存正文、文末答疑、三轮审阅、逐句代码对应及固定提交的上下文原文、请求标识、时间和资料/规则版本；失败不覆盖成功记录 |
| 每次讲解/复述反馈的生成记录 | `repository-explanations/yyyyMMdd.jsonl` | 追加历史，用于诊断、prompt迭代及旧讲解恢复；包含失败和审阅过程，不能不加筛选就作为可用讲解 |
| 存档导出的知识文件 | `archives/knowledge/<存档哈希>/` | `profile.md`、`workflows.md`、`episodes.md`、`changes.md`、`graph.json`、资料索引等派生文件；DSH图谱编辑请求/回执也在此目录 |
| 自主活动记录 | `autonomy-journal.json` | 已执行/未执行活动及结果，和对话、用户存档分开 |
| 本地仓库映射、固定提交源码副本 | `local-repositories.json`、`source-snapshots/` | 环境配置与可重建的源码快照；不是讲解正文 |
| 最近图谱专题、插件安装状态 | `graph-view.json`、`plugins/installed.json`、`plugins/installed/` | 视图偏好、已启用插件清单及校验后的模块文件 |
| 云同步配置、B站登录凭据 | `archive-cloud.json`、`bili-account.dat` | 本机连接配置/加密凭据；不当作学习内容迁移 |
| DSH连接与诊断记录 | `dsh-bridge.json`、`dsh-source-research/`、`source-research/`、`graph-edit-diagnostics/`、`error.log` | 运行连接信息及诊断；握手文件可能含连接令牌，不混入业务文档或分享包 |

普通 JSON 状态使用原子替换，并保留上一份 `.bak`；追加 JSONL 保留历史。讲解当前只保留一份便于读取的 `latest.json` 和上一版备份，完整历史继续留在日志中。旧日志按需补建讲解保存文件，不批量搬迁或删除原数据。切换节点、重启、查看历史不会调用模型；明确点击重新生成才产生新调用。预览与测试使用隔离存储。

当前云同步包是 `ArchiveBook`，包含存档和图谱，**不包含**聊天、长期记忆、本机凭据、独立讲解保存文件和生成日志。备份完整的本地学习资料时需同时保留 `archives/`、`learning/` 和 `repository-explanations/`。未保存的输入草稿与运行中的请求仍是临时状态；不因这次增加讲解保存而承诺跨重启恢复。

## 人设与剧情维护

`assets/persona/elyms.txt` 是所有角色生成入口的共同基准。`CharacterPersona` 只负责读取不可变的嵌入文本；`Persona` 在其后拼接本轮能力、记忆、画像和状态。B站选题与两种存档访谈共用同一基准，再追加任务格式。存档条目整理器和连通性测试不是角色对话，继续使用自己的专用提示。

剧情档案留在 `research/`，不把 OCR 全文或网络评论塞入系统提示。增加设定时先补证据 ID、来源层级和不确定项，再改运行时文本和 `tests/persona/cases.json`。不得将作者推测、角色自述、用户画像自动升级成角色事实。新角色入口应复用基准，并独立提供任务格式与能力状态。

`tools/evaluate-persona.ps1` 默认只生成合成场景提示，`-Live` 才调用当前配置模型；不执行任何返回标记。其结果需人工评分，不能用离线字符串检查冒充模型人设质量评测。
