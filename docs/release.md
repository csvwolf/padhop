# 发布与验证

Release 用户附件只提供 `install.exe`（Inno Setup 中文向导），包含可选 ViGEmBus 官方安装程序；检测到已有驱动时跳过。版本信息放在 Release 标题与说明中。当前本地产物为未签名 standard 实验版，不是独立审计的稳定版。仓库保留旧 ZIP 脚本用于开发调试，不作为用户下载入口。

默认 standard 不操作管理员窗口；用户可主动选择本机自签，详见 local-signing.md。本机信任不是公共发布者签名。需要完整跨权限体验的发行者应提供可信代码签名证书：

```powershell
.\scripts\build.ps1 -UiAccess -SigningThumbprint '<当前用户证书库中的发布证书指纹>'
.\scripts\build-installer.ps1
```

三个可执行文件必须具有有效且相同的发布者签名。安装器不导入证书。生产发布还应按证书服务商流程增加可信时间戳，并在干净 Windows 上验证签名链、Program Files ACL、普通 Steam + 管理员目标窗口。不要分发个人 PFX、测试根证书或开发者配置。

构建脚本执行引擎与界面的合成自测；`scripts/test.ps1` 还运行 Python 分析与噪声过滤测试以及 PowerShell 语法检查。测试覆盖离线逻辑，不包含真实手感或新机器驱动安装。

发布前实测：蓝牙连接/重连、普通 Steam 与游戏共存、按键释放、双触摸板手感、宏取消、屏幕键盘、虚拟 Xbox、托盘恢复、安装升级卸载。UIAccess 另测高权限窗口。包 SHA-256 用于完整性检查，不能替代发布者签名。

旧版本迁移：先在旧程序导出完整配置备份，在新程序导入。旧目录应保留为私人备份；不要把录制日志或签名材料复制到 Git。

安装器源码：`installer/PadHop.iss`。首次构建运行 `scripts/bootstrap-installer.ps1` 获取固定版本编译器，并校验签名及 SHA-256。编译器的商业使用许可请按上游要求处理。UIAccess 安装器在目标电脑再次验证三个程序签名，拒绝不受信任的私人测试签名，不安装根证书。还应使用发布证书签署 install.exe 及卸载程序（Inno Setup SignTool/SignedUninstaller）；当前未提供发布证书，尚未完成此签名步骤。

## GitHub 自动发布

`master` 的推送和指向 `master` 的 PR 自动编译、自测并生成安装器。普通构建保留 Actions artifact；只有版本标签才发布 Release，附件仅 `install.exe`，SHA-256 放在 Release 说明中。

更新根目录 `VERSION`（例如 `0.2.1`）和 CHANGELOG，提交到 `master`，等构建成功后创建相同版本标签：

```powershell
git tag -a v0.2.1 -m 'PadHop 0.2.1'
git push origin v0.2.1
```

工作流验证标签与 VERSION 一致、提交属于 master，并在构建成功后发布标准实验版（prerelease）。没有发布证书时不会自动宣称 UIAccess 可用。已存在的 Release 不会被覆盖；修复后使用新版本号。GitHub 自动附带的源码下载由平台提供，我们只上传一个 install.exe。
