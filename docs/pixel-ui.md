# 像素界面

共享主题在 src/Ui.cs。标题和按钮使用随附 Fusion Pixel 字体，正文使用 Microsoft YaHei UI，代码使用 Consolas；字体缺失时回退，应用不依赖网络。

视觉使用近白 #F2F4FA 背景、白色卡片、深墨描边及青粉紫点缀。圆盘外轮廓和扇区以 4px 阶梯近似圆弧，8 个图标由像素矩阵绘制，保留扇区真实命中、数字键、方向键、Enter 与 Esc 操作。没有用截图替代控件。

功能窗口、输入、按钮、对话卡片和弹出菜单统一方角，知识图谱保留 CORE/S/A/B 色阶。正文不强制使用小号点阵字，以保证长段落、代码和中文的可读性。

选择器、搜索框和密码输入采用自定义简洁控件，搜索支持占位提示与一键清除；图谱复原为框内右上角圆形小图标。交互参考 https://fluent2.microsoft.design/components/web/react/core/searchbox/usage 与 https://fluent2.microsoft.design/components/web/react/core/combobox/usage 。
