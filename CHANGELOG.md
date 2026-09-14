# Changelog

## 0.2.5 — Talaria

- Rename the app to Talaria with the approved anime mecha wing icon in the UI, tray and installer.
- Preserve PadHop installation, settings and signing identities for upgrades.
- Update GitHub links and accept both old and new release asset URLs.
- Older versions need one manual installer download after the repository rename.

## 0.2.4 — experimental

- 软件、安装向导及本机签名维护支持简体中文 / English；关于页可选择语言，重启生效。
- 新增英文 README 与双向语言链接，品牌增加 Steam Controller 副标题。
- 翻译覆盖参数帮助、映射编辑、录制提示和错误信息；用户配置名与路径保持原样。

## 0.2.3 — experimental

- 新增「关于」：版本、项目/作者链接、日志入口、证书维护及更新；可选自动检查并提示，下载和安装分别由用户主动点击。

- 关于页显示本机签名有效期，提前 30 天提醒，支持管理员确认后的一键续期。
- 续期重新签名并清理旧信任与私钥，保留用户配置；取消提权不改变签名。

## 0.2.2 — experimental

- 本机自签移至安装组件选择页；明确说明重新运行安装程序可启用或撤销，个人配置保留。
- 静默安装改用组件参数，仍要求显式同意本机信任变更。

## 0.2.1 — experimental

- Add opt-in local signing in the installer with explicit trust disclosure, one-year local certificates, private-key cleanup, rollback and uninstall cleanup.
- Include the UIAccess payload and recovery script; users do not need to compile source.

## 0.2.0 — experimental

- Name the project PadHop and separate source, user data and build outputs.
- Add reproducible dependency restoration, standard/UIAccess build modes and installable ZIP packaging.
- Add MIT license, third-party notices and privacy/release documentation.
- Add sanitized single-preset sharing and limit default engine logs.
- Configure optional capture tools per user and retain diagnostic recovery state.

- Add a Chinese native install.exe with upgrade/uninstall and optional bundled official ViGEmBus installation; preserve existing shared drivers.
- Validate UIAccess publisher signatures again on the target machine; release assets contain install.exe only.
