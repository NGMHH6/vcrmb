# 小词窗 1.4 界面与提示修复验证

本次按要求去掉悬浮窗 Logo 和整行工具栏，修复提示答案被裁切的问题。EXE、托盘及设置页继续使用应用图标；词库、学习记录和自定义快捷键保持原有行为。

## 界面和操作

- 练习窗只显示中文释义、遮词例句、透明无边框输入区；按提示快捷键后显示完整答案。
- 移除提示、跳过、设置、隐藏及前缀答案确认按钮，不保留空工具栏。拖动中文释义移动窗口，锁定位置时禁止拖动。
- 默认 F1 提示、F2 跳过、Esc 隐藏；Ctrl+Alt+Shift+W 全局显隐。上述四组保留自定义能力。
- Ctrl+, 打开设置，Ctrl+S 打开学习记录，Enter 提交。前缀变体的短答案仍需 Enter，完整长答案自动切题。
- 托盘保留显示、设置、学习记录和退出入口。无到期题时，F2 可提前复习。

## 提示缺字的原因与修复

在修复前新增了使用真实离屏 HWND 的回归检查，复现截图同类故障：原窗口为 360×139，显示 hydrosphere 后原生窗口已变为 360×159，圆角区域却仍为 360×139。收起提示时情况相反，裁剪区域也落后一轮。三个背景模式均失败，记录保存在 `artifacts/native-layout-before-v1.4.json`。

根因是原代码在 WPF `SizeChanged` 中读取原生窗口大小；该事件发生时 HWND 可能还没有完成尺寸更新。原生裁剪限制超出区域的绘制，因此答案行仅露出最上方少数笔画。[Microsoft SetWindowRgn 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn)说明了裁剪和区域句柄的系统所有权。

现在监听原生窗口完成尺寸变化的消息，再通过 Dispatcher 合并并安排裁剪更新，等 WPF 处理完当前窗口消息后读取最终尺寸。避免在消息处理中同步设置区域，打断宽度更新；同时防止 `SetWindowRgn` 自身产生的消息造成重入。关闭窗口时撤销队列操作并移除消息钩子。定位也延后到实际尺寸落定，以保持任务栏上方的边距。[Microsoft WM_WINDOWPOSCHANGED 文档](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-windowposchanged)描述了该通知的时机。

## 本次检查

环境为 Windows 10 22H2 x64、.NET Framework 4.8 WPF、96 DPI。系统 C# 编译器启用 `/warn:4 /warnaserror+`。

- `build.ps1 -Test`：27 项核心/真实 SQLite 检查及 5 项界面检查通过。词库、判词、快捷键配置、累计次数、迁移、备份恢复等既有检查全部通过。
- 原生裁剪回归：三个模式各 8 个状态，共 24 个状态。覆盖提示展开/收起两轮、320 宽和大字号、560 宽、隐藏后带提示重新显示。直接比较 `GetWindowRect` 与 `GetWindowRgn`，同时检查完整答案行底部和期望宽度，全部一致。
- 默认字号的新布局为 360×119，提示后为 360×139；提示文本底部 130，未越过裁剪边界。长文本保持内部滚动。
- 组件检查：无悬浮窗 Button 或 Image、透明输入命中、编辑和选择、长例句、明暗配色、设置四页签。离屏图片保存在 `artifacts/appearance-preview`，已查看截图同词条 `hint-regression.png`、长文本/错误态和设置页。
- 实际应用使用独立数据目录启动，控件树确认释义、遮词例句、拼写输入存在，悬浮窗图标和按钮已移除。启动状态记录显示原生 Acrylic 合成请求成功，窗口 360×119。没有使用正式学习数据。

原始结果：`artifacts/test-results.json`、`artifacts/presentation-test-results.json`、`artifacts/native-layout-results.json`。本次测试实例已停止。

## 尚未验证的范围

桌面截图连续返回 `FrameArrived timed out` 和 `window capture timed out`，无法取得系统实时透明效果截图。尝试 F1 后观察到设置窗口正在录入快捷键，未取得可归因于本次按键的提示状态，故不把该动作记为通过。完整快捷键端到端操作、鼠标拖动、多屏/DPI 切换以及 Windows 11 视觉尚未验收。离屏控件渲染和原生区域测量不等同于这些人工视觉检查。

1.2 / 1.3 升级至 1.4 不迁移数据库，继续使用现有学习数据和设置。先退出旧实例，再运行新版文件夹中的 Vcrmb.exe。保留所有旧包；没有提交、推送或改动正式学习库。
