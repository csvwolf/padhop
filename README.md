<div align="center">
  <img src="assets/app.png" alt="PadHop 图标" width="96" height="96" />
  <h1>PadHop · 跃控</h1>
  <p>让 Steam Controller 2 在更多地方用得上，也用得顺手。</p>

[![Windows build](https://github.com/csvwolf/padhop/actions/workflows/windows.yml/badge.svg?branch=master)](https://github.com/csvwolf/padhop/actions/workflows/windows.yml)
[![Release](https://img.shields.io/github/v/release/csvwolf/padhop?include_prereleases&label=release&color=5ecbf5)](https://github.com/csvwolf/padhop/releases)
[![Downloads](https://img.shields.io/github/downloads/csvwolf/padhop/total?color=5ecbf5)](https://github.com/csvwolf/padhop/releases)
[![MIT](https://img.shields.io/badge/license-MIT-5ecbf5)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078d4)](#安装与使用)

**[下载安装程序](https://github.com/csvwolf/padhop/releases) · [使用说明](#安装与使用) · [反馈问题](https://github.com/csvwolf/padhop/issues)**

</div>

Windows 上的控制器输入配置工具：全局与单独应用配置、左右触摸板、键鼠映射、组合快捷键、宏、屏幕键盘，以及可选 Xbox 虚拟手柄输出。独立开发项目，与 Valve 无隶属关系。

**0.2.1 实验版：目前优先支持蓝牙 SC2。** USB/接收器、Steam Deck/Moonlight 转发设备不属于当前支持范围。协议与固件差异仍需实机验证，离线测试不能证明所有游戏兼容。

## 安装与使用

Windows 10 1903+/11 x64，.NET Framework 4.8。Release 下载 `install.exe`，按中文向导安装，从开始菜单打开 PadHop。升级运行新版安装程序；安装、升级均保留个人配置。卸载从 Windows「已安装的应用」进行。安装器需要管理员权限，程序安装后以普通用户运行。

- 默认标准模式操作普通窗口。安装时可选「本机自签」以启用管理员窗口操作；默认不勾选，需要明确确认本机证书信任变更。详见 [本机自签与撤销](docs/local-signing.md)。
- 公共信任的 UIAccess 发行版仍需发布者代码签名；本机自签只在用户自己的电脑生效。不会分发统一私钥或静默导入证书。
- 安装向导可选 Xbox 输出组件：未安装时调用内置的官方 ViGEmBus 1.22.0 安装程序，已安装则跳过。卸载 PadHop 保留共享驱动。驱动已停止维护，不保证未来 Windows 兼容；安装失败会显示错误，需要重启时提示重启。启用虚拟输出可能与 Steam 的输出重复，应按应用配置选择输出来源。

选择蓝牙设备，在「手柄输入设置」选择或创建配置。试用体验当前编辑参数，应用控制实际使用配置；保存覆盖该命名配置，另存为创建副本。黑名单模式默认接管，排除列表中的应用；白名单模式只接管指定应用。单独应用配置独立于黑白名单。Steam 普通窗口按应用处理，大屏模式让出输入。

托盘可重新打开窗口；重复启动会激活已有实例。配置保存在 `%LOCALAPPDATA%/PadHop`，卸载默认保留。分享给别人请使用「分享当前配置」，完整备份包含应用路径，适合自己恢复。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap-installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```

构建使用系统 .NET Framework C# 编译器，固定 NuGet 依赖版本并校验原生 DLL SHA-256。首次恢复需要联网；运行软件本身没有遥测或自动更新请求。产物在 `bin/`，发布安装程序在 `dist/install.exe`。

可选录制依赖、敏感日志与恢复流程见 [触觉录制](docs/capture.md)。数据说明见 [隐私](docs/privacy.md)，发布限制见 [发布与验证](docs/release.md)，签名准备状态见 [Code signing policy](docs/signing.md)。

## 许可与致谢

PadHop 使用 [MIT](LICENSE)。触摸板算法参考 SteamlessController，虚拟手柄客户端使用 ViGEmClient，安装器可选分发 BSD 许可的 ViGEmBus 官方安装程序，蓝牙 WPR 配置来自 Microsoft busiotools。完整归属及改动说明见 [第三方声明](THIRD-PARTY-NOTICES.txt)。微软 BTETLParse 工具不随项目分发。`branding/` 的生成概念图是品牌草案，当前程序图标仍为简洁版本。
