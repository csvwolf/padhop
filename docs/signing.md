# Code signing policy

当前状态：尚未取得公共信任签名，未提交 SignPath Foundation 申请。默认 Release 为未签名标准实验版；用户可主动选择[本机自签](local-signing.md)，这不代表获得公共信任证书。下面是申请准备方案，不代表已获赞助或签名服务已启用。

优先申请 [SignPath Foundation 免费开源签名](https://signpath.org/)。它要求公开开源源码、已有发行版本、明确的软件说明、MFA、签名角色与人工发布审批；最终资格由基金会审核。

拟由仓库维护者 [csvwolf](https://github.com/csvwolf) 担任作者、审查者和签名批准人，提交申请前需本人确认角色、MFA 状态及服务条款。审批通过后，按服务方要求更新此处的赞助署名。

签名仅针对 Talaria 自己构建的程序。ViGEmBus 官方安装程序保持原签名；ViGEmClient 等第三方文件保留原有归属，不使用本项目凭证重新签名。程序产品名与版本由 VERSION 统一生成。

预期发行顺序：GitHub 托管 runner 编译 UIAccess 程序 → 提交程序签名 → 验证签名 → 构建 Inno Setup 安装器及卸载器 → 按服务方批准的配置完成签名 → 发布唯一附件 install.exe。需要同时确认内嵌卸载器的签名方式，不能只签安装 EXE 外壳就声称 UIAccess 已就绪。

没有获批的组织 ID、项目配置和 API 凭证时，不启用签名工作流；不创建假凭证，不静默安装自签信任根，也不把普通构建冒充可信签名版。证书与账号审批由服务方完成，维护者需批准每次正式签名。

[隐私说明](privacy.md) · [服务方条件](https://signpath.org/terms.html) · [GitHub 集成](https://docs.signpath.io/trusted-build-systems/github)
