# OpenBiliClaw 参考与 B 站只读接入

## 拉取的源码

- 仓库：https://github.com/whiteguo233/OpenBiliClaw
- 许可：MIT
- 本地源码：`research/open-source/OpenBiliClaw/`（浅克隆，保留 `LICENSE` 与项目文档）
- 调研日期：2026-11-09

OpenBiliClaw 是一个完整的本地个性化内容发现 Agent：Python 3.11+、FastAPI、SQLite、Chrome 扩展、画像 / 推荐 / 浏览器扩展链路，以及可选的 DSH 客户端插件。它不是只读的 B 站小工具。

## 为什么没有把整个项目塞进桌宠

桌宠是 .NET Framework 4.8 的 WPF 单文件程序，当前便携包约 2.3 MB。直接内嵌 OpenBiliClaw 需要：

- Python 3.11+ 与大量依赖（FastAPI、httpx、bilibili-api-python、可选 BGE-M3 嵌入模型）；
- 常驻后端、SQLite 数据库、配置与 Cookie；
- Chrome / Firefox 扩展来同步登录态；
- 首次初始化、画像构建与推荐循环。

这会让便携包从“双击即用”变成“先装 Python 环境、跑服务、登录 B 站、等画像建立”。对这次需求来说太重，也会把桌宠的故障面扩大很多。

## 实际移植了什么

`src/Bili.cs` 是原生 C# 的只读 B 站客户端，接口路径与 WBI 细节参考了 OpenBiliClaw 的 `src/openbiliclaw/bilibili/api.py`：

- `GET /x/frontend/finger/spi` 获取匿名 `buvid3` / `buvid4`；
- `GET /x/web-interface/nav` 获取 WBI `img_key` / `sub_key`；
- 标准 WBI mixin 表、参数排序、`wts`、`w_rid` MD5 签名；
- `GET /x/web-interface/wbi/search/type` 搜索公开视频；
- `GET /x/web-interface/wbi/view` 读取视频详情（避开普通 `/view` 的 412）；
- `GET /x/v2/reply/main` 读取热门评论；
- `GET /x/web-interface/ranking/v2` 在搜索失败时改看热门；
- 250 ms 请求间隔、WBI 5 分钟缓存、匿名 Cookie 30 分钟缓存、412 后刷新会话重试一次。

没有移植：完整画像、推荐导演、SQLite、Chrome 扩展、登录 Cookie、点赞/投币/收藏/评论等写操作。

## 桌宠里的行为

- `BiliExplorer` 先让模型根据存档画像选一个搜索词（没有模型时从内置兴趣列表里挑），搜索 8 条；
- 从搜索结果里随机停在前 3 条之一，读取详情和最多 5 条热门评论；
- 用固定思考模式让模型写 80—160 字第一人称逛后感，明确“没看画面、没听声音”；
- `BiliPanel` 展示视频卡片、播放/弹幕/日期、简介、评论、打开/复制链接；
- 聊天里 `/逛B站 [关键词]` 或“去 B 站逛逛”会走同一条链路，把结果写回对话。

## 限制

- 只读公开内容；不登录，因此看不到关注流、收藏、历史、个性化推荐，也不能点赞/评论/投币。
- 不播放视频、不做语音转写；只读标题、简介、评论，不能声称“看过视频”。
- B 站接口不是公开稳定 API，可能 412 / 风控 / 字段变化；失败会提示并尝试热门。
- 搜索关键词、视频标题/简介/评论会发送给 DeepSeek 用于生成逛后感。

## 许可

OpenBiliClaw 使用 MIT License，完整许可见 `research/open-source/OpenBiliClaw/LICENSE` 和项目根目录的 `THIRD_PARTY_NOTICES.md`。
