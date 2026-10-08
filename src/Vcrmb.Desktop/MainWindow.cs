using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Vcrmb.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Vcrmb.Desktop
{
    internal sealed class MainWindow : Window
    {
        private readonly string dataDirectory;
        private readonly LearningStore store;
        private readonly VocabularyFile library;
        private LearningSession session;
        private AppSettings settings;
        private readonly TextBlock meaning;
        private readonly TextBlock example;
        private readonly TextBox answer;
        private readonly TextBlock feedback;
        private readonly PracticeSurface surface;
        private readonly WindowBackdrop backdrop;
        private bool feedbackIsHint;
        private readonly DispatcherTimer checkTimer;
        private readonly DispatcherTimer saveTimer;
        private readonly DispatcherTimer nextTimer;
        private readonly DispatcherTimer diagnosticTimer;
        private readonly Forms.NotifyIcon tray;
        private readonly Drawing.Icon trayIcon;
        private HotkeyManager hotkeys;
        private HwndSource windowSource;
        private SettingsWindow settingsWindow;
        private IntPtr foregroundHook;
        private readonly NativeMethods.ForegroundChanged foregroundChanged;
        private IntPtr previousWindow;
        private bool rendering;
        private bool composing;
        private bool exiting;
        private bool disposed;
        private bool dragging;
        private Key lastTypingKey;

        internal MainWindow(string directory)
        {
            dataDirectory = directory;
            library = VocabularyLoader.LoadBundled();
            store = new LearningStore(Path.Combine(directory, "learning.sqlite"));
            settings = store.LoadSettings(); session = new LearningSession(store, library);
            session.Next(settings, DateTime.UtcNow);
            previousWindow = NativeMethods.GetForegroundWindow();

            Title = "小词窗"; Icon = AppBrand.Image; Width = settings.Width; MinWidth = 320; MaxWidth = 560;
            SizeToContent = SizeToContent.Height; MaxHeight = 390; MinHeight = 96;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = Program.UiTest; ShowActivated = false; Topmost = settings.Topmost;
            Background = Brushes.Transparent; FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            surface = new PracticeSurface();
            Content = surface; backdrop = new WindowBackdrop(this, surface); backdrop.Apply(settings);
            meaning = surface.Meaning; example = surface.Example; answer = surface.Answer; feedback = surface.Feedback;
            surface.DragHandle.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (settings.PositionLocked || e.LeftButton != MouseButtonState.Pressed) return;
                dragging = true; backdrop.BeginDrag(settings);
                try { DragMove(); }
                finally { dragging = false; backdrop.Apply(settings); Guard(SavePosition); }
            };

            checkTimer = Timer(240, delegate { checkTimer.Stop(); AutoCheck(); });
            saveTimer = Timer(500, delegate { saveTimer.Stop(); Guard(SaveCurrent); });
            nextTimer = Timer(180, delegate
            {
                if (IsActive && lastTypingKey != Key.None && Keyboard.IsKeyDown(lastTypingKey)) return;
                nextTimer.Stop(); Guard(Advance);
            });
            diagnosticTimer = Timer(1000, SaveDiagnosticState);
            if (Program.UiTest) diagnosticTimer.Start();
            answer.TextChanged += delegate
            {
                if (rendering || answer.IsReadOnly) return;
                checkTimer.Stop();
                if (!composing) checkTimer.Start();
                saveTimer.Stop(); saveTimer.Start();
                surface.ClearStatus();
                if (!feedbackIsHint) SetFeedback("", false);
            };
            TextCompositionManager.AddPreviewTextInputStartHandler(answer, delegate { composing = true; checkTimer.Stop(); });
            answer.PreviewTextInput += delegate
            {
                composing = false;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)delegate
                { if (!answer.IsReadOnly) { checkTimer.Stop(); checkTimer.Start(); } });
            };
            DataObject.AddPastingHandler(answer, delegate(object sender, DataObjectPastingEventArgs e)
            {
                if (!HasActiveRound) { e.CancelCommand(); return; }
                try { SaveCurrent(); session.Paste(); }
                catch (Exception ex) { e.CancelCommand(); ShowError(ex); }
            });
            PreviewKeyDown += OnKeyDown;
            Activated += delegate
            {
                if (HasActiveRound && !answer.IsReadOnly && !composing && AnswerRules.IsCorrect(session.Word, answer.Text)) checkTimer.Start();
            };
            Deactivated += delegate { checkTimer.Stop(); Guard(SaveCurrent); };
            SourceInitialized += delegate
            {
                windowSource = (HwndSource)PresentationSource.FromVisual(this); windowSource.AddHook(WindowMessage);
                hotkeys = new HotkeyManager(new WindowInteropHelper(this).Handle);
                Guard(delegate { settings.Hotkey = hotkeys.Set(settings.Hotkey); });
                backdrop.Apply(settings); NativeMethods.Place(this, settings);
            };
            SizeChanged += delegate
            {
                // WPF 布局事件早于 HWND 调整，定位也要等原生尺寸落定，避免提示展开后压到任务栏。
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
                { if (!disposed && !dragging && IsLoaded) NativeMethods.Place(this, settings); });
            };
            Loaded += delegate { NativeMethods.Place(this, settings); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!exiting) { e.Cancel = true; HideAll(); }
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            foregroundChanged = delegate(IntPtr hook, uint kind, IntPtr window, int objectId, int child, uint thread, uint time)
            {
                if (NativeMethods.IsWorkWindow(window)) previousWindow = window;
            };
            // 只跟踪外部前台窗口的句柄，用于隐藏后回到工作窗口；不监听普通键盘输入。
            foregroundHook = NativeMethods.SetWinEventHook(3, 3, IntPtr.Zero, foregroundChanged, 0, 0, 2);
            trayIcon = AppBrand.TrayIcon(); tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "小词窗 · " + settings.Hotkey + " 显隐", Visible = true };
            Forms.ContextMenuStrip trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("显示 / 隐藏", null, delegate { Toggle(); });
            trayMenu.Items.Add("学习记录", null, delegate { OpenSettings(true); });
            trayMenu.Items.Add("设置", null, delegate { OpenSettings(); });
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("退出", null, delegate { ExitApplication(); });
            tray.ContextMenuStrip = trayMenu; tray.DoubleClick += delegate { RevealWindow(); };
            RenderRound();
        }

        private bool HasActiveRound { get { return session.Current != null && session.Current.Outcome == "active"; } }
        private void SaveDiagnosticState()
        {
            // 仅显式测试模式输出状态，并显示任务栏按钮，便于桌面自动化工具发现窗口。
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
                visible = IsVisible, active = IsActive, topmost = Topmost, wordId = session.Current == null ? 0 : session.Current.WordId,
                input = answer.Text, feedback = surface.Hint.Text.Length > 0 ? surface.Hint.Text : feedback.Text, width = ActualWidth, height = ActualHeight,
                dpi = NativeMethods.GetDpiForWindow(new WindowInteropHelper(this).Handle), hotkey = settings.Hotkey,
                positionLocked = settings.PositionLocked, left = Left, top = Top,
                hintUsed = session.Current != null && session.Current.HintUsed,
                background = settings.Backdrop, effectiveBackground = backdrop.EffectiveMode, backdropFailure = backdrop.Failure,
                backgroundOpacity = settings.BackdropOpacity, darkAppearance = settings.DarkAppearance,
                automaticTextRunning = backdrop.AutomaticText.Running, automaticTextFailure = backdrop.AutomaticText.Failure,
                feedbackVisible = feedback.IsVisible || (surface.Hint.Text.Length > 0 && meaning.IsVisible), inputBorder = answer.BorderThickness.ToString(),
                pendingConfirmation = HasActiveRound && AnswerRules.IsCorrect(session.Word, answer.Text)
                    && !AnswerRules.CanAutoComplete(session.Word, answer.Text),
                groupSize = settings.GroupSize, groupNumber = settings.GroupNumber, groupCount = session.GroupCount,
                groupTotal = session.GroupState == null ? 0 : session.GroupState.WordIds.Length,
                groupCompleted = session.GroupState == null ? 0 : session.GroupState.CompletedWordIds.Length
            });
            File.WriteAllText(Path.Combine(dataDirectory, "ui-state.json"), json, Encoding.UTF8);
        }
        private static DispatcherTimer Timer(int ms, Action tick)
        {
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            timer.Tick += delegate { tick(); }; return timer;
        }
        private void SaveCurrent()
        {
            if (HasActiveRound) session.SaveInput(answer.Text);
        }

        private void SavePosition()
        {
            NativeMethods.CapturePosition(this, settings); store.SaveSettings(settings);
        }

        private void RenderRound()
        {
            rendering = true;
            try
            {
                checkTimer.Stop(); saveTimer.Stop(); nextTimer.Stop(); composing = false;
                answer.IsReadOnly = false; surface.ClearStatus();
                meaning.FontSize = settings.FontSize + 1; example.FontSize = settings.FontSize; answer.FontSize = settings.FontSize + 2;
                surface.Answer.CaretBlinkEnabled = settings.CaretBlinkEnabled;
                Width = settings.Width; Topmost = settings.Topmost;
                surface.DragHandle.Cursor = settings.PositionLocked ? Cursors.Arrow : Cursors.SizeAll;
                if (session.Current == null)
                {
                    surface.SetMeaning("第 " + settings.GroupNumber + " / " + session.GroupCount + " 组" +
                        (settings.ReviewOnly && session.GroupState.WordIds.Length == 0 ? "暂无到期词" : "已完成"));
                    example.Text = "按 " + settings.SkipShortcut + (settings.GroupNumber < session.GroupCount ? " 继续下一组" : " 从第 1 组重新开始") +
                        "，" + settings.RestartGroupShortcut + " 重练本组；Ctrl+, 打开设置。";
                    surface.InputRow.Visibility = Visibility.Collapsed;
                    SetFeedback("", false); return;
                }
                surface.SetMeaning(session.Word.PartOfSpeech + " " + session.Word.Meaning);
                example.Text = session.Word.Cloze;
                surface.InputRow.Visibility = Visibility.Visible; answer.Text = session.Current.Input ?? ""; answer.CaretIndex = answer.Text.Length;
                feedbackIsHint = session.Current.HintUsed;
                SetFeedback(feedbackIsHint ? string.Join(" / ", session.Word.Answers) : "", false);
                if (IsActive && IsVisible) answer.Focus();
            }
            finally { rendering = false; }
        }

        private void SetFeedback(string text, bool error)
        {
            surface.ShowFeedback(text, error);
        }

        private void ShowError(Exception exception)
        {
            Program.Log(exception); checkTimer.Stop(); nextTimer.Stop(); answer.IsReadOnly = false;
            feedbackIsHint = false; SetFeedback("操作未完成：" + exception.Message, true);
        }

        private void Guard(Action operation)
        {
            try { operation(); } catch (Exception exception) { ShowError(exception); }
        }

        private void AutoCheck()
        {
            if (!HasActiveRound || composing || !IsActive || !IsVisible || answer.IsReadOnly) return;
            if (AnswerRules.CanAutoComplete(session.Word, answer.Text)) Submit();
        }

        private void Submit()
        {
            if (!HasActiveRound || composing || answer.IsReadOnly) return;
            Guard(delegate
            {
                if (string.IsNullOrWhiteSpace(answer.Text)) { answer.Focus(); return; }
                SaveCurrent();
                if (AnswerRules.IsCorrect(session.Word, answer.Text))
                {
                    if (!session.Complete(true, DateTime.UtcNow)) return;
                    answer.IsReadOnly = true; surface.MarkAnswer(true);
                    nextTimer.Start();
                }
                else
                {
                    session.Error(); surface.MarkAnswer(false); ShowAnswer();
                }
            });
        }

        private void Advance()
        {
            session.Next(settings, DateTime.UtcNow); RenderRound();
        }

        private void Hint()
        {
            if (!HasActiveRound || answer.IsReadOnly) return;
            Guard(delegate
            {
                SaveCurrent(); session.Hint();
                ShowAnswer();
            });
        }

        private void ShowAnswer()
        {
            feedbackIsHint = true; SetFeedback(string.Join(" / ", session.Word.Answers), false); answer.Focus();
        }

        private void Skip()
        {
            Guard(delegate
            {
                if (session.Current == null) { settings = session.NextGroup(settings, DateTime.UtcNow); RenderRound(); return; }
                if (!HasActiveRound) return;
                SaveCurrent(); session.Complete(false, DateTime.UtcNow); Advance();
            });
        }

        private void RestartGroup()
        {
            Guard(delegate
            {
                SaveCurrent(); session.SwitchScope(settings, DateTime.UtcNow, true);
                // 重练同设置页共用事务；重绘同时取消答对后的延迟切题，避免新一轮跳过首词。
                RenderRound(); if (IsActive && IsVisible) answer.Focus();
            });
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.ImeProcessed) return;
            if (composing) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key), modifiers = (uint)Keyboard.Modifiers;
            bool hide = ShortcutRules.Parse(settings.HideShortcut, false).Matches(virtualKey, modifiers);
            bool hint = ShortcutRules.Parse(settings.HintShortcut, false).Matches(virtualKey, modifiers);
            bool skip = ShortcutRules.Parse(settings.SkipShortcut, false).Matches(virtualKey, modifiers);
            bool restart = ShortcutRules.Parse(settings.RestartGroupShortcut, false).Matches(virtualKey, modifiers);
            if (e.IsRepeat && (key == Key.Enter || hide || hint || skip || restart)) { e.Handled = true; return; }
            if (key >= Key.A && key <= Key.Z) lastTypingKey = key;
            if (hide) { HideAll(); e.Handled = true; }
            else if (hint) { Hint(); e.Handled = true; }
            else if (skip) { Skip(); e.Handled = true; }
            else if (restart) { RestartGroup(); e.Handled = true; }
            else if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                lastTypingKey = Key.Enter;
                Submit(); e.Handled = true;
            }
            else if (key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { OpenSettings(true); e.Handled = true; }
            else if (key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control) { OpenSettings(); e.Handled = true; }
        }

        private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0312 && hotkeys != null && hotkeys.Matches(wParam)) { Toggle(); handled = true; }
            if (message == 0x031E || message == 0x031A || message == 0x001A)
                Dispatcher.BeginInvoke((Action)delegate { if (!disposed) backdrop.Apply(settings); });
            return IntPtr.Zero;
        }

        private void Toggle() { if (IsVisible || settingsWindow != null) HideAll(); else RevealWindow(); }

        internal void RevealWindow()
        {
            if (disposed || exiting) return;
            if (session.Current != null && session.Current.Outcome != "active") Guard(Advance);
            Show(); WindowState = WindowState.Normal; NativeMethods.Place(this, settings);
            Activate(); if (answer.Visibility == Visibility.Visible) { answer.Focus(); answer.CaretIndex = answer.Text.Length; }
            if (HasActiveRound && AnswerRules.IsCorrect(session.Word, answer.Text)) checkTimer.Start();
        }

        private void HideAll()
        {
            if (disposed) return;
            Guard(SaveCurrent); Guard(SavePosition); checkTimer.Stop(); saveTimer.Stop();
            tray.ContextMenuStrip.Close();
            NativeMethods.CloseFileDialogs();
            foreach (Window owned in OwnedWindows.Cast<Window>().ToArray()) owned.Close();
            Hide();
            if (NativeMethods.IsWindow(previousWindow)) NativeMethods.SetForegroundWindow(previousWindow);
        }

        private void OpenSettings()
        {
            OpenSettings(false);
        }

        private void OpenSettings(bool records)
        {
            Guard(delegate
            {
                if (settingsWindow != null)
                {
                    if (records) settingsWindow.ShowLearningRecords();
                    settingsWindow.Activate(); return;
                }
                SaveCurrent();
                settingsWindow = new SettingsWindow(settings, library, ApplySettings, delegate(bool recording)
                { Guard(delegate { if (recording) hotkeys.Suspend(); else hotkeys.Resume(); }); },
                    store.Statistics, Backup, Restore, ExitApplication,
                    "当前第 " + settings.GroupNumber + " / " + session.GroupCount + " 组，已练 " + session.GroupState.CompletedWordIds.Length +
                    " / " + session.GroupState.WordIds.Length + " 词。") { Owner = this };
                if (records) settingsWindow.ShowLearningRecords();
                try { settingsWindow.ShowDialog(); }
                finally { settingsWindow = null; if (!disposed) hotkeys.Resume(); }
            });
        }

        private void ApplySettings(AppSettings candidate, bool restartGroup)
        {
            SaveCurrent(); LearningStore.ValidateSettings(candidate); ShortcutRules.Validate(candidate);
            candidate.HideShortcut = ShortcutRules.Parse(candidate.HideShortcut, false).Text;
            candidate.HintShortcut = ShortcutRules.Parse(candidate.HintShortcut, false).Text;
            candidate.SkipShortcut = ShortcutRules.Parse(candidate.SkipShortcut, false).Text;
            candidate.RestartGroupShortcut = ShortcutRules.Parse(candidate.RestartGroupShortcut, false).Text;
            string previous = hotkeys.ActiveShortcut;
            candidate.Hotkey = hotkeys.Set(candidate.Hotkey);
            bool scopeChanged = candidate.Chapter != settings.Chapter || candidate.ReviewOnly != settings.ReviewOnly ||
                candidate.GroupSize != settings.GroupSize || candidate.GroupNumber != settings.GroupNumber;
            try
            {
                if (scopeChanged || restartGroup) session.SwitchScope(candidate, DateTime.UtcNow, restartGroup);
                else store.SaveSettings(candidate);
            }
            catch { if (string.IsNullOrEmpty(previous)) hotkeys.Dispose(); else hotkeys.Set(previous); throw; }
            settings = candidate; tray.Text = "小词窗 · " + settings.Hotkey + " 显隐";
            if (!scopeChanged && !restartGroup && session.Current != null && session.Current.Outcome != "active") session.Next(settings, DateTime.UtcNow);
            RenderRound(); backdrop.Apply(settings); NativeMethods.Place(this, settings);
        }

        private string Backup(Window owner)
        {
            SaveCurrent();
            SaveFileDialog dialog = new SaveFileDialog { Filter = "小词窗备份 (*.sqlite)|*.sqlite",
                FileName = "小词窗-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sqlite" };
            if (dialog.ShowDialog(owner) != true) return "";
            store.Backup(dialog.FileName); return "备份已保存：" + dialog.FileName;
        }

        private bool Restore(Window owner)
        {
            SaveCurrent(); OpenFileDialog picker = new OpenFileDialog { Filter = "小词窗备份 (*.sqlite)|*.sqlite", CheckFileExists = true };
            if (picker.ShowDialog(owner) != true) return false;
            if (MessageBox.Show(owner, "恢复后将使用备份中的学习记录和设置；当前数据会先保留一份备份。", "恢复学习记录",
                MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return false;
            string safety = store.Restore(picker.FileName);
            settings = store.LoadSettings(); session = new LearningSession(store, library); session.Next(settings, DateTime.UtcNow);
            string hotkeyWarning = "";
            try { settings.Hotkey = hotkeys.Set(settings.Hotkey); }
            catch (Exception ex)
            {
                Program.Log(ex);
                if (!string.IsNullOrEmpty(hotkeys.ActiveShortcut))
                {
                    settings.Hotkey = hotkeys.ActiveShortcut; store.SaveSettings(settings);
                    hotkeyWarning = "快捷键冲突，继续使用 " + settings.Hotkey + "。";
                }
                else hotkeyWarning = "快捷键未注册，请打开设置重新配置。";
            }
            RenderRound(); backdrop.Apply(settings); NativeMethods.Place(this, settings);
            tray.Text = "小词窗 · " + settings.Hotkey + " 显隐";
            if (hotkeyWarning.Length > 0) { feedbackIsHint = false; SetFeedback(hotkeyWarning, true); }
            MessageBox.Show(owner, "已恢复。原有记录的备份：\n" + safety +
                (hotkeyWarning.Length == 0 ? "" : "\n" + hotkeyWarning), "恢复完成", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        private void OnDisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke((Action)delegate { if (!disposed) NativeMethods.Place(this, settings); });
        }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) Dispatcher.BeginInvoke((Action)HideAll);
        }

        private void ExitApplication()
        {
            try { SaveCurrent(); SavePosition(); }
            catch (Exception ex)
            {
                Program.Log(ex);
                if (MessageBox.Show((Window)settingsWindow ?? this, "保存失败：" + ex.Message + "\n仍要退出吗？未保存输入将丢失。", "小词窗", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            exiting = true; Application.Current.Shutdown();
        }

        internal void DisposeResources()
        {
            if (disposed) return; disposed = true;
            checkTimer.Stop(); saveTimer.Stop(); nextTimer.Stop(); diagnosticTimer.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            if (foregroundHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(foregroundHook);
            if (hotkeys != null) hotkeys.Dispose();
            if (windowSource != null) windowSource.RemoveHook(WindowMessage);
            backdrop.Dispose();
            tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); trayIcon.Dispose(); store.Dispose();
        }
    }
}
