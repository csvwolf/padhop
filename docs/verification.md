# 本次验证记录

2026-09-14，Windows x64，standard 构建。

- UI、输入引擎、普通权限 helper、采集 worker 编译成功，警告作为错误。
- 引擎合成测试通过：输入报告、映射、压力状态、Win 组合键、宏取消、摇杆、滚动和触觉门限。
- UI 自测通过：编辑器、预设、配置迁移、分享来源路径清理。
- Python：HCI/GATT 身份关联、采集噪声筛选测试，以及 5 项分析测试通过。
- PowerShell 脚本语法检查通过。
- ZIP 文件清单校验通过；隔离目录安装复制成功；修改包内 README 后校验拒绝，未改动用户安装和配置。
- 已知个人用户名、设备序列号、蓝牙地址、私人证书指纹的源码扫描无命中；通用 token 扫描的命中为进程令牌与取消令牌变量。

随后增加并验证 Inno Setup 安装 EXE：独立测试 AppId 下安装、覆盖升级和原生卸载成功，检测并跳过现有 ViGEmBus，卸载保留共享驱动。UIAccess 安装脚本编译通过，签名校验脚本拒绝未签名程序。

本次未执行真实硬件手感测试、完整蓝牙录制、缺少驱动的新机器安装或可信发布证书的 UIAccess 安装。GitHub master 首次构建已在干净 Windows runner 上通过（https://github.com/csvwolf/padhop/actions/runs/34819171332），包括编译、离线测试、依赖安装与 install.exe 打包。其他 Git 托管平台可直接调用同一组 PowerShell 脚本。
