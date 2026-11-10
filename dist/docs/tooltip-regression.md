# 提示框崩溃修复与回归测试

验证日期：2026-11-10。

## 问题与修复

用户日志在 08:07:34 记录了提示框展开时的 XamlParseException：无法设置未知成员 `System.Windows.Controls.TextBlock.TextWrapping`。全局 ToolTip 模板将 `TextBlock.TextWrapping` 放在 ContentPresenter 上，但它不是附加属性。模板直到原生提示框打开才加载，所以仅构造窗口或渲染静态页面不能覆盖这个错误。随后 Popup 布局的 NullReferenceException 出现在同一异常链中。

在修改前，新增原生弹窗测试的第一项即可复现相同异常。修复为字符串 DataTemplate，由其中的 TextBlock 设置 TextWrapping；保留非字符串视觉内容的呈现能力和 360 像素宽度上限。

## 自动化覆盖

运行 `./test.ps1 -Render`，完整脚本成功结束。测试隔离到临时存储目录，界面使用预览数据，不调用模型 API。

- 既有检查：基础 101 项、存档 13 项、存档学习 20 项、知识图谱 28 项、综合回归 68 项。
- 新增弹窗检查 46 项，实际打开 75 次提示框，覆盖 27 种应用内提示文本。
- 覆盖短文本、500 字中文、超长连续 URL、多行 Unicode、空内容、打开时更新内容和自定义视觉内容；验证换行、宽度和原生窗口创建。
- 连续开关 40 次；打开菜单、子菜单、文本编辑菜单和确认对话框；遍历 10 个主页面及内部页签，覆盖悬浮聊天与存档模式。
- 等待真实图谱异步布局，打开节点及连线提示；在新布局进行中销毁图谱，检查延迟 UI 异常。
- 完成应用预览渲染；Node 桥接服务 2 项测试通过。

人工检查生成的长中文与多行提示框截图，换行和内容呈现正常。

## 证据与边界

- 修复前复现：`artifacts/tooltip-fix/reproduction-before-fix.txt`。
- 完整运行输出：`artifacts/tooltip-fix/full-suite.txt`。
- 完整测试构建：`artifacts/tests/a12d381db692475983c64791728127bf/`。
- 弹窗截图：该构建下 `tests/popup-preview/`。

日志中的较早 DeepSeek 错误为 DNS 解析失败；本次域名解析成功。未发送真实模型请求，因此不据此认定 API 鉴权、额度或完整问答链路已通过。保留原有 error.log，部署后以日志大小和更新时间判断是否出现新增错误。
