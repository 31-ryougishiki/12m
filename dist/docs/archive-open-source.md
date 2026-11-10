# 存档单元：开源调研与集成记录

调研日期：2026-11-10。目标是从真实工作经历中整理能力边界、可复用流程与资料，并让后续任务按需使用。下面三项是相邻领域的开源机制，不是现成的个人能力访谈产品。

## 参考项目与实际落点

| 项目 | 核验版本与许可证 | 参考机制 | 本项目实现 |
| --- | --- | --- | --- |
| [Mem0](https://github.com/mem0ai/mem0) | `f1791daa86d469249c9b002f62644d58a1eb96a0`，[Apache-2.0](https://github.com/mem0ai/mem0/blob/f1791daa86d469249c9b002f62644d58a1eb96a0/LICENSE) | 新信息与旧记忆比较，区分新增、更新与无变化；更新保留标识 | `ArchiveStore.Merge`、`ArchiveLearning.Record`：确定性 ADD / UPDATE / no-op，保存修改前后快照，界面可恢复当前变更。无原话依据的新提取不能降低已有掌握程度。删除仍由用户操作，不接受模型删除命令。 |
| [LangMem](https://github.com/langchain-ai/langmem) | `8620b986af5b0c7593e6b7fbece2998690d22d15`，[MIT](https://github.com/langchain-ai/langmem/blob/8620b986af5b0c7593e6b7fbece2998690d22d15/LICENSE) | 将知识、经历、过程规则分开保存，利用反馈改善后续行为 | `ArchiveLearning`：经历记录场景、做法与判断、结果、经验和原话。案例必须匹配用户访谈原话，重复提取不重复插入。流程页可填写成功／失败／未验证的复盘，供后续访谈修订草稿。 |
| [Voyager](https://github.com/MineDojo/Voyager) | `55e45a880755d0c8c66ca7fb5fe7962ac8974f89`，[MIT](https://github.com/MineDojo/Voyager/blob/55e45a880755d0c8c66ca7fb5fe7962ac8974f89/LICENSE) | 按任务检索技能库，并依据结果反馈判断技能有效性 | `ArchiveRecall`：根据当前消息选择相关知识、已确认流程、案例和资料，分类限制条数及总字数；流程必须完整入选，不能截掉验收或异常处理。用户报告失败后退回草稿，成功反馈不会自动确认。 |

原始实现入口：

- [Mem0 更新判定提示词](https://github.com/mem0ai/mem0/blob/f1791daa86d469249c9b002f62644d58a1eb96a0/mem0/configs/prompts.py)
- [LangMem 概念文档：语义、经历及过程记忆](https://langchain-ai.github.io/langmem/concepts/conceptual_guide/)
- [LangMem 反馈优化入口](https://github.com/langchain-ai/langmem/blob/8620b986af5b0c7593e6b7fbece2998690d22d15/src/langmem/prompts/optimization.py)
- [Voyager 技能检索](https://github.com/MineDojo/Voyager/blob/55e45a880755d0c8c66ca7fb5fe7962ac8974f89/voyager/agents/skill.py)、[结果评估](https://github.com/MineDojo/Voyager/blob/55e45a880755d0c8c66ca7fb5fe7962ac8974f89/voyager/agents/critic.py)

## 适配方式与边界

本次使用这些项目的公开架构思路，在既有 .NET Framework 4.8 应用中独立实现 C# 逻辑；没有复制其代码或提示词，没有打包这些项目，也没有新增 Python、向量数据库、模型训练或外部服务依赖。许可证核验用于记录来源，不表示本项目整体改用了其许可证。

检索使用本项目已有的中文二元词组和英文词切分，按词项匹配及标题／触发条件权重排序，不是 Voyager 的向量检索，也没有声称具有同等召回效果。普通聊天通过 `PetServices → ChatService.BuildTurns → ArchiveService.Recall` 使用当前问题检索；自主活动等没有明确用户任务的旧入口仍使用总体摘要。

经验与复盘是用户自述，不是自动执行成功的证明。系统不优化人物身份或系统权限，也不把网页当成指令。流程改善通过后续 API 访谈生成草稿，并由用户确认。模型提取的案例必须包含可在访谈用户发言中匹配的原话；没有依据的案例丢弃。相同 task + evidence 视为同一案例，修订可移除后重新整理。

## 数据与页面

存档格式升级为版本 3，旧字段保持兼容，缺少 `Episodes` / `Changes` 的旧存档初始化为空。保留最近 200 次知识变更与 200 条经验案例，界面显示最近 20 次知识变更；其他案例可在「案例与复盘」页查看、继续深挖或移除。恢复只适用于修改后的记录仍与当前知识一致的情况，避免覆盖后来的修订。删除知识时同时清理该知识的历史快照。

派生文件增加 `episodes.md`、`changes.md`，`SKILL.md` 链接经验记录。删除存档同时删除这些自动生成文件，用户自行添加的附件保持保留。资料卡没有读取时间时显式使用 UTC 空时间，避免东八区序列化下溢。

## 验证

`tests/unit/ArchiveLearningTests.cs` 增加 20 项离线检查：no-op、无依据降级保护、稳定标识与历史快照、案例原话匹配与去重、任务相关检索、草稿排除、预算内完整流程、空反馈拒绝、失败使确认失效、成功不自动确认、Markdown 输出、重启恢复、历史恢复及过期保护、删除历史清理、真实聊天组装路径、两种整理提示词协议。

编译、原有回归和新增检查通过。未调用真实 API 验证模型提取质量；目前测试保障协议、数据处理与调用路径，不代表所有同义表达都能检索到。
