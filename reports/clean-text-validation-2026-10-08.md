# 小词窗 1.5.8 透明文字重影修正

用户反馈 1.5.7 的文字有重影感。该版在文字控件和外层容器上叠加了两个相反方向的阴影。1.5.8 移除全部文字阴影，恢复单层文字，不用描边或重复绘制增加对比度。

全透明默认使用浅色字，适合深色桌面或软件；设置 → 外观 → 全透明 → 文字颜色可选择深色字，用于浅色背景。字色由用户选择，不自动识别后方画面。释义、例句和输入采用同一主字色，答案和错误提示继续使用绿色、红色。磨砂与纯色继续使用独立的“深色外观”偏好。

## 本次验证

- `build.ps1 -Test` 在 Windows 10 x64、.NET Framework 4.8 工具链下通过，编译警告视为错误：41 项核心/真实 SQLite、14 项 WPF 检查。
- 数据库检查覆盖浅色/深色选择保存与重新打开、旧配置缺少新字段时默认浅色字、备份恢复。累计记录和未完成词继续保留，不变更数据库表结构。
- 设置窗口在全透明时显示文字颜色，切到纯色后隐藏，切回全透明仍保留选择。浅色/深色两种选项均能保存，原来的深色外观偏好不被覆盖。
- 原有编辑、回车提交、行内提示、重练、常亮光标与三个背景模式的 24 个裁剪状态继续通过；光标检查覆盖两种透明字色。
- 另用本进程的离屏原生窗口确认实际进入 `clear`，两种颜色选择均应用到实际控件；递归检查控件树没有残留文字效果。高对比度分支保留系统字色，测试没有抢占前台焦点。
- 已查看深色背景上的默认浅色字、浅色背景上的深色字，以及新设置页。预览保留释义、例句、输入和行内答案；错误状态另有渲染输出。

证据：`artifacts/test-results.json`、`artifacts/presentation-test-results.json`、`artifacts/native-layout-results.json`、`artifacts/clean-text-verification/verification.json`。预览位于 `artifacts/clean-text-verification/clean-text-preview.png` 和 `artifacts/appearance-preview/settings-clear-text.png`。

## 交付与边界

沿用 `build.ps1` 的发行清单，将本次已测试的构建打包为便携 ZIP，再由 `tools/build_installer.ps1` 生成 v1.5.8 安装包。包文件、运行文件与测试构建的一致性结果保存到 `artifacts/package-verification-v1.5.8.json`；后续可运行 `build.ps1 -Test -Installer` 完整重建。

图片是 WPF 控件叠加受控背景的渲染，不是用户桌面截图。真实桌面合成、完整键盘/输入法、多屏/DPI 和 Windows 11 尚未人工验收。本次不覆盖正式安装，不操作正式学习数据，未重跑安装/卸载生命周期。

升级前从旧版托盘退出，再安装 `小词窗-Setup-x64-v1.5.8.exe`。已有全透明设置升级后默认浅色字，不再绘制阴影。
