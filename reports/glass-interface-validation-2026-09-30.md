# 小词窗 1.3 — 图标与透明悬浮窗验证

日期：2026-09-30。环境：Windows 10 22H2 x64 / 10.0.19045、PowerShell 7.6.5、现有 .NET Framework 4.8 系列运行时。没有 Git 仓库，不提交或推送。仅使用隔离测试数据。

## 已实现

- 自行绘制“窗口 + W”矢量 logo，墨绿色底、白色 W 与浅绿窗口横线。SVG 为唯一几何源，构建自动导出 PNG 和包含 16、20、24、32、40、48、64、128、256 像素帧的 ICO。
- 图标应用到 EXE 的原生图标资源、WPF 窗口、托盘、悬浮窗和设置页；运行时从嵌入资源加载。
- 新增外观页：磨砂玻璃、全透明、纯色；磨砂浓度 20%–90%，默认 65%；支持深浅配色。
- 原生合成只处理背景，文字和输入内容不使用窗口整体 Opacity 降低清晰度。
- 输入区域删除白底和方框，使用真正的 TextBox 内容宿主保留编辑、选择、滚动和输入法功能。
- 顶部集中灯泡提示、跳过、设置、隐藏按钮，名称和快捷键通过悬停提示提供；移除底栏快捷键文字、常驻输入说明和空白提示区。
- 空输入不弹出催促文字；错误使用红色 × 与细线；正确短暂显示 ✓ 后切题；前缀变体通过 ↵ 按钮或 Enter 确认。
- 主动提示才展开完整答案行；保存等异常仍按需报告，避免静默丢失记录。
- 原有固定词库、累计正确/错误统计、备份恢复及快捷键功能保留。学习库结构仍为 v2；旧外观字段缺省使用默认磨砂。

## 实现依据与兼容处理

背景使用 Windows 桌面合成和 Accent Policy，接口含义参考 [TranslucentTB 的窗口合成实现](https://github.com/TranslucentTB/TranslucentTB/blob/release/TranslucentTB/taskbar/taskbarattributeworker.cpp)。WPF 透明画布与玻璃区域处理参考 [Microsoft 的 WPF DWM 文档](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/extend-glass-frame-into-a-wpf-application)。拖动时临时采用轻量模糊以减少 Windows 10 Acrylic 的拖动开销，该行为也见于 [FluentWPF 的窗口处理](https://github.com/sourcechord/FluentWPF/blob/master/FluentWPF/AcrylicWindow.cs)。

实现为本项目的小型原生接口封装，没有新增运行依赖或操作系统设置修改。Accent Policy 并非跨版本稳定的公开 WPF 契约，因此检查返回值：Acrylic 不可用时尝试普通模糊，再回到实色；全透明失败回实色；系统高对比度或桌面合成关闭时使用系统可读背景。窗口圆角区域随尺寸与 DPI 更新，原生区域对象交给系统或失败时及时释放。

## 自动检查

命令：`./build.ps1 -Test`，启用 `/warn:4 /warnaserror+`。

**27 项核心/真实 SQLite 检查通过**：

沿用固定 3674 词条、判词、完整答案提示、快捷键、累计计数、迁移、备份恢复、事务回滚和异常退出的检查；增加外观设置重启保存、原学习状态不受影响、旧设置缺字段默认值、无效模式/NaN/Infinity/越界浓度拒绝。

**4 项 WPF 组件检查通过**：

1. 最小 320 宽度下，无边框 TextBox 保留插入、选择及视觉命中区域，内容宿主完整。
2. 大字号、长释义、长例句、答案变体和前缀确认按钮不会把输入区裁出悬浮窗边界。
3. 深浅外观、透明背景和展开/收起提示行正常测量与渲染；移除提示后不残留空白行；前景不使用整体透明度；高对比度使用系统文字色。
4. 四个设置页签保留，外观页和底部保存/退出按钮在组件渲染中完整显示。

首次离屏命中测试用 InputHitTest，因未连接 HWND 的控件 IsVisible 为 false 被过滤；改用视觉树几何命中检查后通过。该测试只证明本进程控件布局和几何，不冒充实际桌面鼠标点击。

已查看 `artifacts/appearance-preview` 中 light、dark、clear、hint、long-error、settings 六张组件 PNG；logo PNG 也已查看。组件预览是程序控件离屏渲染，不包含操作系统的实时背景模糊。

## 实机检查

在独立目录 `artifacts/ui-v1.3-20260930-153945` 启动编译出的程序：

- 测试进程 PID 6460，原生控件树包含新图标、四个操作按钮、中文、遮词例句和输入；已移除常驻操作说明。
- 系统接受 Acrylic 请求：`effectiveBackground=acrylic`，`backdropFailure=null`。
- 当前屏幕 96 DPI，窗口 360 × 142，输入框边框 0，反馈行不可见。
- 使用 computer-use 的可访问性写入方法向实际输入控件写入 `atmos`；控件树与 ui-state.json 均确认该值。没有向正式学习库写入测试输入。
- 两次桌面截图失败，分别返回 `FrameArrived timed out: timed out waiting on channel`、`window capture timed out: timed out waiting on channel`。没有把 API 成功当作实机画面已核验。
- 测试结束仅停止本次隔离进程。

另外用三个独立外观配置分别启动程序，均 360 × 142、无输入边框、无常驻反馈、没有 error.log：

| 请求模式 | 系统返回的有效模式 |
|---|---|
| frosted | acrylic |
| clear | clear |
| solid | solid |

原始结果保存于 `artifacts/appearance-smoke-results.json`，测试进程全部已停止。

## 尚未验收

- 实际桌面上的模糊强度、透明背景和圆角像素效果。
- 鼠标拖动、点击图标、实际键盘/输入法及保存外观设置的完整交互回归。
- Windows 11、多显示器、不同 DPI、锁屏恢复与系统高对比度实机效果。

上面的组件渲染、原生接口返回值和输入控件写入不替代这些验证。

## 发行

`build.ps1 -Test -Package` 创建独立 v1.3.0 运行目录和 ZIP，保留 1.2 等旧包。运行文件由白名单复制，不包含测试 EXE、个人数据库、日志或外部词库。图标嵌入 EXE，词库继续嵌入核心 DLL。SHA256.json 用于校验。

先退出旧实例再运行新版；同一数据目录的单实例机制可能使再次启动只唤回旧程序。1.2 → 1.3 没有学习库结构迁移；1.0 / 1.1 仍使用已验证的 v1 → v2 自动备份迁移。
