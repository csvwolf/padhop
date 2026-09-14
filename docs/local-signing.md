# 本机自签（可选）

安装 install.exe 时，「选择安装组件」中的本机自签默认不勾选。

| 选择 | 行为 |
| --- | --- |
| 不勾选 | 使用普通窗口模式，不修改证书信任库。 |
| 勾选并确认 | 在本机生成代码签名证书，信任该证书，签署本机的 PadHop 程序，启用 UIAccess 操作管理员窗口。Steam 不必提权。 |

这不需要购买证书，亦不能证明公共发行者身份，不会使其他电脑自动信任这份程序。证书加入本机根证书库，对本机所有用户生效；系统对证书的信任并非仅凭“PadHop”名称限定。程序或输入流程如果被滥用，可能操作管理员窗口。

脚本只签署三个固定程序，不签第三方驱动或任意文件。每次创建独立的代码签名证书，私钥不可导出，正常操作结束后删除私钥，不上传、不打包 PFX。不会关闭 UAC、Secure Boot、驱动签名检查。企业策略仍可能禁止 UIAccess；安装器会检查实际 TokenUIAccess，失败时尝试还原并提示。

安装后改变主意，直接重新运行 install.exe：勾选该组件以启用，取消勾选以撤销。无需先卸载，个人配置保留。

## 到期、升级与撤销

证书有效期一年，不使用公共时间戳；到期前重新签名。升级先撤销旧签名、恢复旧标准文件，再安装新版；如果继续选择本机自签，则为新版创建新证书。卸载会移除本项目记录的确切证书，保留个人配置和共享 ViGEmBus。

安装后也可在管理员 PowerShell 中执行：

```powershell
& "$env:ProgramFiles\PadHop\Local-Signing.ps1" -Action Status
& "$env:ProgramFiles\PadHop\Local-Signing.ps1" -Action Enable
& "$env:ProgramFiles\PadHop\Local-Signing.ps1" -Action Disable
```

Enable 会再次要求输入 YES。Disable 恢复签名前文件并删除本次信任。如程序已被其他版本改写、备份损坏，脚本会拒绝覆盖未知文件；可用 `-Action RemoveTrust` 只撤销信任，然后重新安装。

诊断：安装目录 `local-signing-last.log`；恢复记录：`.local-signing/state.json`。异常断电/强制结束可能中断私钥删除或还原，请保留记录，恢复后撤销；不要直接删除恢复目录。如果签名过程中断且个人证书库仍有相同指纹的 `PadHop Local Only` 证书，需要删除该证书及私钥。本脚本不触碰其他名称/指纹的证书。

自动部署必须同时指定 `/COMPONENTS=app,localuiaccess /ACCEPTLOCALTRUST=YES` 才能同意信任变更；仅指定任务不会自动获得同意。

GitHub 一次性 Windows 环境已通过实际安装测试：缺少明确同意时拒绝自签、默认标准模式、本机签名及 TokenUIAccess、私钥证书删除、升级还原、重新签名使用新证书、卸载清理信任。另有备份还原、未知升级保护及损坏备份拒绝测试。未向开发者机器增加新证书；企业策略和实际手柄操作仍需在目标电脑验证。
