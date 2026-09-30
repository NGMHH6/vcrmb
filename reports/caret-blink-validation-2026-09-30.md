# 小词窗 1.5.4 光标闪动配置验证

设置 → 外观新增“光标闪动”，默认关闭。关闭时悬浮窗输入框显示常亮光标；开启时使用 WPF 原有光标，遵循系统闪烁设置。保存即生效，重启及备份恢复保留选择；没有此字段的旧版设置默认关闭。只影响悬浮窗，学习记录及系统光标设置不变。

## 实现与依据

保留真正的 WPF TextBox 内容宿主、编辑、选择和输入法处理。常亮模式把原光标画笔设为透明，在输入框内根据实际插入位置绘制不接收点击的光标；文字选择、失焦、隐藏和只读时收起。主题更新同步文字及光标颜色，调整字号或宽度后会将插入点带回可见区域。开启闪动时恢复 WPF 原光标。

绘制使用官方 [CaretBrush](https://learn.microsoft.com/dotnet/api/system.windows.controls.primitives.textboxbase.caretbrush) 与 [GetRectFromCharacterIndex](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.textbox.getrectfromcharacterindex) API。未调用系统级 [SetCaretBlinkTime](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcaretblinktime)，该函数会影响其他应用。无新增依赖、数据库结构迁移或全局设置变更。

## 当前验证

- Windows 10 22H2 x64、.NET Framework 4.8 构建通过，编译警告视为错误，程序版本 1.5.4.0。
- 40 项核心/真实 SQLite 检查、11 项 WPF 检查通过。
- 新设置及旧版缺字段设置均默认不闪动；开启、关闭、重新打开和恢复备份均保留正确选择，草稿及累计错误次数保持。
- 设置页真实保存回调分别验证开启和关闭，已查看 `artifacts/appearance-preview/settings-caret.png`，配置项和说明完整可见。
- 光标测试在独立且不切换到前台的测试桌面中运行，使用 WPF 实际键盘焦点，未向用户桌面注入按键。8 次、每次间隔 150 毫秒的完整输入区像素采样一致，常亮光标持续可见。
- 空输入、光标位于首/中/尾、选中与替换、长文本滚动、字号/宽度改变、深色/高对比度、只读、失焦和隐藏检查通过。开启闪动后常亮层隐藏，绘制交回 WPF；关闭后恢复常亮层。
- 已查看 `artifacts/appearance-preview/steady-caret.png`，光标位于输入末尾，文字、光标及布局没有裁切。它是控件渲染，不是桌面截图。
- 原有正确自动判对、回车答错显示答案、分组、设置及三个背景模式的原生窗口裁剪回归继续通过。

机器可读结果：`artifacts/test-results.json`、`artifacts/presentation-test-results.json`。`build.ps1 -Test -Installer` 生成便携 ZIP 与 EXE 安装包，发行文件使用白名单及 SHA-256 校验。

## 验证边界

尚未完成真实键盘/输入法的全流程人工验收、多屏/DPI、Windows 11 和当前桌面合成效果验证。原生闪烁由 WPF 及系统设置控制，本次验证模式交接，未宣称验证所有系统闪烁频率。

未覆盖正式安装或使用正式学习数据，安装/卸载生命周期回归本次未重跑；1.5.1 的 19 项安装检查保留为历史结果。安装脚本未改，新安装包仍未签名。升级前先从旧版托盘退出，再运行 `小词窗-Setup-x64-v1.5.4.exe`，原有学习记录保留。
