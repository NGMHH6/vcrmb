# 小词窗 1.5.1 安装包与仓库验证

交付文件：`artifacts/小词窗-Setup-x64-v1.5.1.exe`，4,535,681 字节（约 4.33 MiB）。

SHA-256：`ac74b7c5f116cd0e6e7a22bc32f1615f37e44428d3a4458f8315c7f6c312273f`。

## 安装行为

- 中文安装向导，仅为当前用户安装，默认目录 `%LOCALAPPDATA%\Programs\Vcrmb`，无需管理员权限。
- 支持 Windows 10 1607+ / Windows 11 x64；安装前检查 .NET Framework 4.8 或兼容版本，不自动下载系统运行时。
- 创建开始菜单“小词窗”，桌面快捷方式默认不勾选，可自行选择。完成页可启动程序，静默安装不会自动启动。
- 便携版和安装版默认共用 `%LOCALAPPDATA%\Vcrmb` 的原有学习数据。升级前从托盘退出旧版；运行保护同时检查新版公共标记和旧版默认数据目录互斥量，不强制结束用户程序。
- 覆盖安装更新程序文件；卸载只删除登记的文件与快捷方式，保留学习记录、备份和安装目录中的其他用户文件。
- 安装文件采用明确白名单，含 5 个运行文件、README、第三方说明、依赖锁定信息、功能验证报告和 SHA-256 清单；不包含外置词库、源码、测试程序或用户学习库。

## 本次验证结果

环境：Windows 10 22H2 x64，PowerShell 7.6.5 / Windows PowerShell 5.1，已有 .NET Framework 4.8 系列运行时。

- **37 项核心/真实 SQLite 检查 + 7 项 WPF 界面检查通过**，警告视为编译错误。应用版本为 1.5.1.0，固定词库仍为 3674 条。
- **19 项安装检查通过**，在两种 PowerShell 下分别运行。使用同一个发行 EXE，验证记录包含其完整 SHA-256。覆盖正常安装、旧版运行拦截、9 个有效载荷文件的哈希、当前用户卸载项、开始菜单和桌面快捷方式、运行中升级/卸载拦截、退出后的覆盖安装、卸载后的程序清理与用户数据保留。
- 从已安装目录实际启动应用，独立测试库的第 4 组、每组 12 词、已练 1 词、第 38 词及 `draft` 输入均恢复正确，无 `error.log`。第 37 词正确/错误各 1 次、设置、组进度、未完成输入和备份在覆盖安装及卸载后均保持。测试刻意把独立学习目录放在安装目录内，确认卸载不递归清除用户数据。
- 正式学习目录只做文件哈希读取，未被测试实例打开；整个安装生命周期前后文件清单和 SHA-256 一致。测试创建的安装项、程序文件、开始菜单与桌面快捷方式均已移除；独立验证记录保留在 `artifacts/installer-smoke`。
- **从 Git 索引导出源码后成功重建 ZIP 和 EXE**，只复用经固定哈希验证的下载归档，不带构建产物或生成图标。使用 Windows PowerShell 5.1 执行 `build.ps1 -Installer`，发现并修复该版本 `ConvertFrom-Json` 的数组展开差异。未安装全局编译工具。
- 安装向导真实控件树确认中文标题、欢迎说明、下一步和取消按钮。截图接口首次报 `FrameArrived timed out`，重新选择窗口后重试报 `window capture timed out`；已停止截图尝试并清理预览进程。

原始证据（均不进入 Git）：`artifacts/test-results.json`、`artifacts/presentation-test-results.json`、`artifacts/installer-build.json`、`artifacts/installer-verification.json`、`artifacts/source-build-verification.json`。安装测试可用 `tests/Installer/Smoke.ps1` 复现；它会先拒绝已有安装、同名快捷方式或运行实例，避免覆盖用户现有安装。

## 仓库范围与限制

本地仓库使用 `main`。收录源码、测试、固定词库与校对、SVG 图标源、构建/安装脚本、依赖锁定、使用说明和验证报告；`.tools`、`build`、`artifacts`、自动生成的 ICO/PNG、SQLite、日志、编辑器缓存被忽略。没有公开发布词库，也未配置远端或推送。

安装包未使用代码签名证书，Windows 可能显示未知发布者。安装向导的完整画面与鼠标逐步操作尚未人工验收；Windows 11、缺失 .NET Framework 和低于最低版本的系统未实机验证。应用已有的全量人工快捷键、多屏/DPI 验证限制继续保留，不能用编译或静默安装替代这些检查。

## 工具来源

使用 [Inno Setup 7.1.0 官方发行](https://github.com/jrsoftware/issrc/releases/tag/is-7_1_0)，下载归档 SHA-256 为 `0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f`，与官方资产摘要一致，Authenticode 状态为 `Valid`，发布者为 `Pyrsys B.V.`。工具以官方 [portable 模式](https://github.com/jrsoftware/issrc/blob/is-7_1_0/isportable.iss) 提取到 `.tools`；采用原始安装引擎并保留版权，许可见 [Inno Setup License](https://jrsoftware.org/files/is/license.txt)。
