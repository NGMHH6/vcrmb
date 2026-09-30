using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vcrmb.Core;
using Vcrmb.Desktop;

internal static class GroupSettingsTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void RunAll(Action<string, Action> run, string output)
    {
        run("group settings refresh chapter and size boundaries and reject invalid counts", delegate
        {
            VocabularyFile book = VocabularyLoader.LoadBundled();
            GroupPracticeOptions options = new GroupPracticeOptions(book, new AppSettings { GroupNumber = 100 });
            Assert(options.GroupInput.Items.Count == 184 && ((PracticeGroup)options.GroupInput.SelectedItem).Number == 100, "Stored group not selected.");
            options.SetChapter(book.Entries[241].Chapter);
            Assert(options.GroupInput.Items.Count == 7 && options.GroupInput.SelectedIndex == 0, "Chapter did not reset group range.");
            options.CountInput.Text = "50";
            Assert(options.GroupInput.Items.Count == 3 && ((PracticeGroup)options.GroupInput.Items[2]).Words.Count == 30, "Size not reflected.");
            foreach (string invalid in new[] { "", "0", "-1", "2.5", "abc", "3675" })
            {
                options.CountInput.Text = invalid; bool rejected = false;
                try { options.ApplyTo(new AppSettings()); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected && !options.GroupInput.IsEnabled, "Invalid count accepted: " + invalid);
            }
            options.CountInput.Text = "25"; options.GroupInput.SelectedIndex = 3;
            AppSettings result = new AppSettings(); options.ApplyTo(result);
            Assert(result.GroupSize == 25 && result.GroupNumber == 4, "Stale selection submitted.");
        });
        run("practice settings omit review controls and save legacy settings as normal group practice", delegate
        {
            bool applied = false;
            AppSettings legacy = new AppSettings { ReviewOnly = true, ReviewDays = new[] { 2, 5, 9 } };
            SettingsWindow settings = new SettingsWindow(legacy, VocabularyLoader.LoadBundled(), delegate(AppSettings result, bool restart)
            {
                Assert(!result.ReviewOnly && result.GroupSize == 20 && result.GroupNumber == 1 && !restart,
                    "Saving settings kept the hidden review filter or changed group selection.");
                applied = true;
            }, delegate { }, delegate { return new System.Collections.Generic.List<WordStatistics>(); },
                delegate { return ""; }, delegate { return false; }, delegate { });
            ShowSettings(settings, delegate
            {
                AssertNoReviewControls(settings);
                Render(settings, Path.Combine(output, "appearance-preview", "settings-practice-clean.png"));
                FindButton(settings, "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(applied, "Settings without interval input cannot be saved.");
            });
        });
        run("appearance checkbox defaults off and saves both caret blinking choices", delegate
        {
            AppSettings current = new AppSettings();
            foreach (bool enabled in new[] { true, false })
            {
                bool applied = false;
                SettingsWindow settings = new SettingsWindow(current, VocabularyLoader.LoadBundled(), delegate(AppSettings result, bool restart)
                { current = result; applied = true; }, delegate { },
                    delegate { return new System.Collections.Generic.List<WordStatistics>(); },
                    delegate { return ""; }, delegate { return false; }, delegate { });
                ShowSettings(settings, delegate
                {
                    TabControl tabs = Find<TabControl>(settings); tabs.SelectedIndex = 1; settings.UpdateLayout();
                    StackPanel appearance = (StackPanel)((ScrollViewer)((TabItem)tabs.Items[1]).Content).Content;
                    CheckBox option = appearance.Children.OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "光标闪动");
                    Assert(option.IsChecked == current.CaretBlinkEnabled, "Checkbox did not reflect the stored default or preference.");
                    if (enabled) Render(settings, Path.Combine(output, "appearance-preview", "settings-caret.png"));
                    option.IsChecked = enabled;
                    FindButton(settings, "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(applied && current.CaretBlinkEnabled == enabled, "Caret preference was not passed to the save callback.");
                });
            }
        });
        run("restart shortcut recorder rejects conflicts restores F3 and saves a custom key", delegate
        {
            string previousDirectory = Program.DataDirectory;
            Program.DataDirectory = Path.GetFullPath(Path.Combine(output, "restart-settings-ui", Guid.NewGuid().ToString("N")));
            try
            {
                AppSettings current = new AppSettings(); bool applied = false;
                SettingsWindow settings = new SettingsWindow(current, VocabularyLoader.LoadBundled(), delegate(AppSettings result, bool restart)
                { current = result; applied = true; }, delegate { },
                    delegate { return new System.Collections.Generic.List<WordStatistics>(); },
                    delegate { return ""; }, delegate { return false; }, delegate { });
                ShowSettings(settings, delegate
                {
                    TabControl tabs = Find<TabControl>(settings); tabs.SelectedIndex = 2; settings.UpdateLayout();
                    StackPanel shortcuts = (StackPanel)((ScrollViewer)((TabItem)tabs.Items[2]).Content).Content;
                    ShortcutRecorder recorder = shortcuts.Children.OfType<ShortcutRecorder>()
                        .Single(r => AutomationProperties.GetName(r) == "重练当前组快捷键");
                    Assert(recorder.Text == "F3", "Restart recorder is missing its default.");
                    Render(settings, Path.Combine(output, "appearance-preview", "settings-shortcuts-restart.png"));
                    KeyEventArgs conflict = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings), Environment.TickCount, Key.F2)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    recorder.RaiseEvent(conflict); Assert(conflict.Handled && recorder.Text == "F2", "Recorder did not capture the key event.");
                    FindButton(settings, "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(!applied && settings.IsVisible && current.RestartGroupShortcut == "F3", "A conflicting restart shortcut was saved.");
                    FindButton(settings, "恢复默认快捷键").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(recorder.Text == "F3", "Reset defaults omitted the restart shortcut.");
                    recorder.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings), Environment.TickCount, Key.F6)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    FindButton(settings, "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(applied && current.RestartGroupShortcut == "F6", "Custom restart shortcut did not reach the save callback.");
                });
            }
            finally { Program.DataDirectory = previousDirectory; }
        });
        run("native settings dialog saves a group jump and its restart button preserves totals", delegate
        {
            VocabularyFile book = VocabularyLoader.LoadBundled(); DateTime now = DateTime.UtcNow;
            string directory = Path.GetFullPath(Path.Combine(output, "group-settings-ui", Guid.NewGuid().ToString("N")));
            Program.DataDirectory = directory; bool applied = false, restarted = false;
            using (LearningStore store = new LearningStore(Path.Combine(directory, "learning.sqlite")))
            {
                AppSettings current = new AppSettings(); LearningSession session = new LearningSession(store, book); session.Next(current, now);
                Action<AppSettings, bool> apply = delegate(AppSettings candidate, bool restart)
                { session.SwitchScope(candidate, now, restart); current = candidate; applied = true; restarted = restart; };
                SettingsWindow settings = new SettingsWindow(current, book, apply, delegate { }, store.Statistics,
                    delegate { return ""; }, delegate { return false; }, delegate { }, "当前第 1 / 184 组，已练 0 / 20 词。");
                ShowSettings(settings, delegate
                {
                    GroupPracticeOptions options = Find<GroupPracticeOptions>(settings);
                    Assert(options != null && AutomationProperties.GetName(options.CountInput) == "每组练习词数", "Group controls not reachable.");
                    options.CountInput.Text = "12"; options.GroupInput.SelectedIndex = 3;
                    Render(settings, Path.Combine(output, "appearance-preview", "settings-practice.png"));
                    Button save = FindButton(settings, "保存设置"); Assert(save != null, "Save button missing.");
                    options.CountInput.Text = "0"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(!applied && settings.IsVisible && session.Word.Id == 1, "Invalid form changed the live group.");
                    options.CountInput.Text = "12"; options.GroupInput.SelectedIndex = 3;
                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(applied && !restarted && current.GroupSize == 12 && current.GroupNumber == 4 && session.Word.Id == 37, "Save did not jump to requested group.");
                });
                session.SaveInput(session.Word.Answers[0]); session.Complete(true, now); session.Next(current, now); session.SaveInput("unfinished");
                settings = new SettingsWindow(current, book, apply, delegate { }, store.Statistics,
                    delegate { return ""; }, delegate { return false; }, delegate { });
                ShowSettings(settings, delegate
                {
                    Button restart = FindButton(settings, "跳转并从头练习该组"); Assert(restart != null, "Restart button missing.");
                    restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(restarted && session.Word.Id == 37 && session.Current.Input == "" && session.GroupState.CompletedWordIds.Length == 0, "Restart did not reset selected group.");
                    Assert(store.Statistics().Single().Correct == 1, "Restart cleared cumulative counts.");
                });
                string smokeDirectory = Path.Combine(directory, "smoke"); Directory.CreateDirectory(smokeDirectory);
                store.Backup(Path.Combine(smokeDirectory, "learning.sqlite"));
                File.WriteAllText(Path.Combine(output, "group-smoke-profile.json"), new JavaScriptSerializer().Serialize(new { directory = smokeDirectory }), Encoding.UTF8);
            }
        });
    }
    // 只操作本测试进程创建的离屏对话框，走真实保存回调和 SQLite；不注入系统输入。
    private static void ShowSettings(SettingsWindow window, Action action)
    {
        Exception failure = null;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -10000; window.Top = -10000;
        window.ShowActivated = false; window.ShowInTaskbar = false;
        window.Loaded += delegate
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)delegate
            {
                try { action(); } catch (Exception ex) { failure = ex; }
                finally { if (window.IsVisible) window.Close(); }
            });
        };
        window.ShowDialog(); if (failure != null) throw failure;
    }
    private static T Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i); T typed = child as T;
            if (typed != null) return typed; T nested = Find<T>(child); if (nested != null) return nested;
        }
        return null;
    }
    private static void AssertNoReviewControls(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            TextBlock text = child as TextBlock; CheckBox checkbox = child as CheckBox;
            Assert(AutomationProperties.GetName(child) != "复习间隔" &&
                (text == null || !text.Text.Contains("复习")) &&
                (checkbox == null || !Convert.ToString(checkbox.Content).Contains("复习")), "Removed review control is still visible.");
            AssertNoReviewControls(child);
        }
    }
    private static Button FindButton(DependencyObject parent, string content)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i); Button button = child as Button;
            if (button != null && Convert.ToString(button.Content) == content) return button;
            Button nested = FindButton(child, content); if (nested != null) return nested;
        }
        return null;
    }
    private static void Render(FrameworkElement content, string path)
    {
        content.UpdateLayout();
        FrameworkElement root = (FrameworkElement)((Window)content).Content;
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right),
            (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (Stream stream = File.Create(path)) encoder.Save(stream);
    }
}
