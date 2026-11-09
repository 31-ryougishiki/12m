# 第三方声明

## OpenBiliClaw

本项目的 B 站只读客户端参考了 OpenBiliClaw 的公开接口路径与 WBI 签名实现：

- 项目：OpenBiliClaw
- 仓库：https://github.com/whiteguo233/OpenBiliClaw
- 许可：MIT License
- 本地源码：`research/open-source/OpenBiliClaw/`

OpenBiliClaw 的 MIT 许可全文如下：

```text
MIT License

Copyright (c) 2026 OpenBiliClaw Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

本项目没有把 OpenBiliClaw 的 Python 后端、Chrome 扩展、SQLite 数据库或推荐画像打包进桌宠；只把公开、只读的 B 站接口路径与 WBI 签名方式重写为 C#。B 站接口并非官方稳定 API，使用风险由使用者自行承担。

## QRCoder

本项目使用 QRCoder 1.4.3 生成 B 站登录二维码：

- 项目：QRCoder
- 仓库：https://github.com/codebude/QRCoder
- 版本：1.4.3
- 许可：MIT License
- 本地文件：`vendor/qrcoder/QRCoder.dll`

```text
MIT License

Copyright (c) 2013-2024 Raffael Herrmann

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
