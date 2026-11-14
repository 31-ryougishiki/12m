# 存档云同步

入口：陪伴面板 → 存档单元 → 云同步。

本版提供自动上传和手动恢复：所有存档作为一个快照保存，每次上传使用唯一对象名。不同电脑的快照互不覆盖；不会自动合并，也不会自动拉取覆盖本地。自动上传开启后，每两分钟检查一次，仅在本次运行中内容发生变化时上传；重启后第一次检查会重新上传。关闭前可点击「立即上传」确保最后的修改已上传。

## 当前电脑配置

- Bucket：`elyms-archive-test1`
- 地域：`cn-shenzhen`
- 目录：`elyms/archives/`
- Endpoint 由程序构造为 HTTPS 的 OSS 公网域名，无需设置 CORS。

浏览器登录不是桌宠调用 OSS 的凭证。请在 RAM 创建专用用户及 AccessKey，将下面的最小权限策略授予该用户，然后在桌宠本机界面填写 ID 和 Secret。不要使用主账号密钥，不要把 Secret 发进聊天或提交到仓库。另一台电脑需各自配置访问凭证。

权限策略见 [archive-cloud-policy.json](archive-cloud-policy.json)。仅允许列出指定前缀，以及读取和上传其中的对象，不授予删除或管理 Bucket 的权限。依据 [阿里云 RAM 目录权限文档](https://www.alibabacloud.com/help/en/oss/user-guide/access-control-base-on-ram-policy)。Bucket 应保持私有。

## 使用

1. 填写 Bucket、地域和目录，填入专用 RAM AccessKey ID / Secret，点击「保存云同步设置」。两项密钥均留空时保留已保存凭证。
2. 点击「刷新云端版本」验证列举权限，然后点击「立即上传」。首次空列表是正常情况。
3. 需要时启用「每两分钟检查变化并自动上传」。上传失败保留本地存档，下个周期重试；不会阻塞访谈。
4. 在另一台电脑配置同一个 Bucket / 地域 / 目录，刷新版本，选择需要的快照并恢复。
5. 恢复会替换全部存档，先在本地 `archives/cloud-backups/` 保留完整旧快照。下载期间若本地又被编辑，恢复会取消。知识库 Markdown 从下载的数据重新生成。

## 数据和限制

上传 `ArchiveBook` 中的所有存档：访谈、画像、知识图谱、资料卡及缓存文本、流程、案例和当前选中存档。不会上传聊天历史、记忆、AI Key、B站 Cookie 或本机设置；资料链接对应的外部附件不额外上传。

凭证经 Windows DPAPI 加密保存在 `%LOCALAPPDATA%/ElymsDesktopPet/archive-cloud.json`，只能由当前 Windows 用户解密。不要分发这个文件或其备份。同步文件采用 HTTPS 传输，不做客户端内容加密，访问权限由 OSS/RAM 控制。

单个快照上限 32 MB。云端旧版本不会自动清理，存储、请求和公网下载可能产生 OSS 用量。只支持当前存档格式，拒绝未来版本、空文档和非法存档目录标识。恢复前备份可通过保留副本后替换本机 `archives/archives.json` 进行人工恢复（需先退出桌宠）。

签名使用 [OSS V4](https://www.alibabacloud.com/help/en/oss/developer-reference/recommend-to-use-signature-version-4)，请求禁止重定向，不在 URL 或错误文本中放凭证。网络超时 30 秒。当前实现不支持 STS 自动续期。

## 验证状态

离线 C# 回归及 Node 桥接测试通过，包括新增的存档往返、格式/路径/大小检查、DPAPI 和签名测试。2026-11-10 已完成真实 OSS 验证：新版桌宠界面上传成功并列出两个版本，下载内容哈希一致；使用真实云端版本在隔离目录调用恢复服务，确认恢复前完整备份、恢复结果和派生知识库文件正常，当前用户存档未被覆盖。跨电脑恢复及两分钟自动上传周期尚未实测；自动上传保持关闭。
