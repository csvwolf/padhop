# 可选触觉录制

普通映射与预设不需要 Python。完整蓝牙录制需要 Python 3.9+、Windows WPR 和微软 BTETLParse.exe。请从微软 Bluetooth Test Platform 的官方来源取得分析工具；遵守其许可，勿上传到本仓库。

在安装目录执行：

```powershell
.\configure-capture.ps1 -Python 'C:\Tools\Python\python.exe' -Parser 'C:\Tools\BTP\BTETLParse.exe'
```

脚本验证解析器 Microsoft 签名，工具路径仅写入用户数据目录。来源链接与 WPR 配置版本见源码 `tools/capture/PROVENANCE.txt`。

录制时按界面的准备、静止、慢滑、快滑、按压步骤操作。蓝牙诊断需要一次管理员授权；临时修改 BTHPORT 的三个诊断值，并在结束时恢复。不要同时运行其他蓝牙诊断工具。捕获可能包含其他设备内容，见隐私说明。

如果断电或进程被强制终止，管理员 PowerShell 中运行安装目录 `capture/Recover-Capture.ps1`。恢复信息保存在受 HKLM 权限保护的 `SOFTWARE/PadHopCaptureRecovery`，存在未完成恢复时拒绝新的采集。恢复脚本仅处理约定的三个诊断值，保留其他工具修改的不同值。异常类型需手动检查；驱动刷新失败时保留恢复信息。异常中断后的 WPR 会话可能还需根据采集目录 `trace-instance.txt` 用 `wpr -cancel -instancename <该会话名>` 单独停止，不要取消其他会话。

原始数据留在 `%LOCALAPPDATA%/PadHop/captures`，用户自行删除。当前录制仍为实验功能，尚未在干净机器验证所有依赖安装组合。
