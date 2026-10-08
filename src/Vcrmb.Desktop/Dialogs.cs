using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Vcrmb.Core;

namespace Vcrmb.Desktop
{
    internal static class Ui
    {
        internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(35, 48, 54));
        internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(105, 119, 126));
        internal static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(28, 108, 97));
        internal static readonly Brush Line = new SolidColorBrush(Color.FromRgb(206, 218, 220));
        internal static TextBlock Text(string text, double size)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) };
        }
        internal static Button Button(string text, Action action)
        {
            Button button = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 3, 8, 3), MinHeight = 28 };
            button.Click += delegate { action(); }; return button;
        }
        internal static void WindowStyle(Window window, string title, int width, int height)
        {
            window.Title = title; window.Width = width; window.Height = height;
            window.Icon = AppBrand.Image;
            window.MinWidth = width - 80; window.MinHeight = height - 80;
            window.MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 24);
            window.MinHeight = Math.Min(window.MinHeight, window.MaxHeight);
            window.Height = Math.Min(window.Height, window.MaxHeight);
            window.Background = Brushes.White; window.FontFamily = new FontFamily("Microsoft YaHei UI");
            window.FontSize = 13; window.ShowInTaskbar = Program.UiTest; window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape && !(System.Windows.Input.Keyboard.FocusedElement is ShortcutRecorder))
                { window.Close(); e.Handled = true; }
            };
        }
    }

    internal sealed class WordRecordView
    {
        public string Word { get; set; }
        public string Meaning { get; set; }
        public long Correct { get; set; }
        public long Errors { get; set; }
        public double? Accuracy { get; set; }
    }

    internal sealed class SettingsWindow : Window
    {
        private readonly TabControl tabs;
        private readonly TabItem recordsTab;
        private readonly DataGrid records;
        private readonly TextBox search;
        private readonly TextBlock empty;
        private readonly TextBlock status;
        private readonly Func<List<WordStatistics>> readStatistics;
        private readonly Dictionary<int, Word> words;

        internal SettingsWindow(AppSettings settings, VocabularyFile library, Action<AppSettings, bool> apply,
            Action<bool> captureChanged, Func<List<WordStatistics>> readStatistics, Func<Window, string> backup,
            Func<Window, bool> restore, Action exit, string practiceProgress = "")
        {
            this.readStatistics = readStatistics; words = library.Entries.ToDictionary(w => w.Id);
            Ui.WindowStyle(this, "设置 · 小词窗", 650, 620);
            // 切回工作软件也要恢复显隐键；不能仅依赖录入框的逻辑焦点变化。
            Deactivated += delegate { captureChanged(false); };
            Activated += delegate
            { if (System.Windows.Input.Keyboard.FocusedElement is ShortcutRecorder) captureChanged(true); };
            Grid root = new Grid { Margin = new Thickness(20, 16, 20, 16) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new Image { Source = AppBrand.Image, Width = 29, Height = 29, Margin = new Thickness(0, 0, 10, 0) });
            title.Children.Add(Ui.Text("小词窗", 20)); root.Children.Add(title);
            tabs = new TabControl { Margin = new Thickness(0, 8, 0, 8) }; Grid.SetRow(tabs, 1); root.Children.Add(tabs);
            StackPanel panel = new StackPanel { Margin = new Thickness(14, 8, 14, 8) };
            tabs.Items.Add(new TabItem { Header = "练习", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            status = Ui.Text("切换章节或组号保留各组进度。重练只重置所选组，不清空累计次数。", 11); status.MinHeight = 32;
            status.MaxHeight = 58; status.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(status, 2); root.Children.Add(status);
            Action<string> report = delegate(string message) { status.Text = message; status.ToolTip = message; status.Foreground = Ui.Muted; };

            panel.Children.Add(Ui.Text("学习章节", 12));
            ComboBox chapter = new ComboBox { MinHeight = 30, Margin = new Thickness(0, 0, 0, 10) };
            List<string> chapters = new List<string> { "全部章节" }; chapters.AddRange(library.Entries.Select(w => w.Chapter).Distinct());
            chapter.ItemsSource = chapters; chapter.SelectedItem = string.IsNullOrEmpty(settings.Chapter) ? chapters[0] : settings.Chapter;
            System.Windows.Automation.AutomationProperties.SetName(chapter, "学习章节");
            panel.Children.Add(chapter);
            if (!string.IsNullOrEmpty(practiceProgress)) panel.Children.Add(Ui.Text(practiceProgress, 11));
            GroupPracticeOptions groups = new GroupPracticeOptions(library, settings); panel.Children.Add(groups);
            chapter.SelectionChanged += delegate { groups.SetChapter(chapter.SelectedIndex <= 0 ? "" : (string)chapter.SelectedItem); };
            Button restartGroup = Ui.Button("跳转并从头练习该组", delegate { }); panel.Children.Add(restartGroup);
            StackPanel choices = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 10) };
            choices.Children.Add(Ui.Text("字号 ", 12));
            ComboBox font = new ComboBox { ItemsSource = new[] { 12, 14, 16, 18 }, SelectedItem = settings.FontSize, Width = 62, Margin = new Thickness(0, 0, 20, 0) };
            choices.Children.Add(font); choices.Children.Add(Ui.Text("宽度 ", 12));
            ComboBox width = new ComboBox { ItemsSource = new[] { 320, 360, 400, 440, 480, 560 }, SelectedItem = (int)settings.Width, Width = 75 };
            choices.Children.Add(width); panel.Children.Add(choices);
            CheckBox top = new CheckBox { Content = "保持窗口置顶", IsChecked = settings.Topmost, Margin = new Thickness(0, 4, 0, 8) };
            CheckBox locked = new CheckBox { Content = "锁定窗口位置", IsChecked = settings.PositionLocked, Margin = new Thickness(0, 4, 0, 8) };
            panel.Children.Add(top); panel.Children.Add(locked);

            StackPanel appearance = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
            tabs.Items.Add(new TabItem { Header = "外观", Content = new ScrollViewer { Content = appearance, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            appearance.Children.Add(Ui.Text("悬浮窗背景", 12));
            ComboBox background = new ComboBox { ItemsSource = new[] { "磨砂玻璃", "全透明", "纯色" },
                SelectedIndex = settings.Backdrop == "clear" ? 1 : settings.Backdrop == "solid" ? 2 : 0, MinHeight = 30,
                Margin = new Thickness(0, 0, 0, 14) };
            System.Windows.Automation.AutomationProperties.SetName(background, "悬浮窗背景"); appearance.Children.Add(background);
            CheckBox dark = new CheckBox { Content = "深色外观（磨砂玻璃 / 纯色）", IsChecked = settings.DarkAppearance,
                Visibility = background.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible,
                Margin = new Thickness(0, 3, 0, 15) }; appearance.Children.Add(dark);
            CheckBox caretBlink = new CheckBox { Content = "光标闪动", IsChecked = settings.CaretBlinkEnabled,
                Margin = new Thickness(0, 3, 0, 6) };
            System.Windows.Automation.AutomationProperties.SetName(caretBlink, "光标闪动"); appearance.Children.Add(caretBlink);
            appearance.Children.Add(Ui.Text("默认关闭，输入时光标常亮；开启后跟随系统闪烁设置。", 11));
            TextBlock density = Ui.Text("磨砂浓度 " + (settings.BackdropOpacity * 100).ToString("0") + "%", 12); appearance.Children.Add(density);
            Slider opacity = new Slider { Minimum = 20, Maximum = 90, Value = settings.BackdropOpacity * 100,
                TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 10), IsEnabled = background.SelectedIndex == 0 };
            System.Windows.Automation.AutomationProperties.SetName(opacity, "磨砂浓度"); appearance.Children.Add(opacity);
            opacity.ValueChanged += delegate { density.Text = "磨砂浓度 " + opacity.Value.ToString("0") + "%"; };
            background.SelectionChanged += delegate
            {
                opacity.IsEnabled = background.SelectedIndex == 0;
                dark.Visibility = background.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
            };
            appearance.Children.Add(Ui.Text("全透明时自动识别后方画面的明暗，深色背景用浅色字，浅色背景用深色字，无需手动切换。磨砂玻璃和纯色可通过深色外观切换配色。", 12));
            appearance.Children.Add(Ui.Text("悬浮窗没有图标和工具栏，拖动中文释义可移动。输入区没有方框，主动提示或回车答错时显示答案。", 12));

            StackPanel shortcuts = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
            tabs.Items.Add(new TabItem { Header = "快捷键", Content = new ScrollViewer { Content = shortcuts, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            shortcuts.Children.Add(Ui.Text("点击录入框后直接按键，保存后生效。", 12));
            ShortcutRecorder hotkey = AddShortcut(shortcuts, "全局显隐", settings.Hotkey, true, captureChanged, report);
            ShortcutRecorder hide = AddShortcut(shortcuts, "窗口隐藏", settings.HideShortcut, false, captureChanged, report);
            ShortcutRecorder hint = AddShortcut(shortcuts, "提示", settings.HintShortcut, false, captureChanged, report);
            ShortcutRecorder skip = AddShortcut(shortcuts, "跳过 / 下一组", settings.SkipShortcut, false, captureChanged, report);
            ShortcutRecorder restartShortcut = AddShortcut(shortcuts, "重练当前组", settings.RestartGroupShortcut, false, captureChanged, report);
            shortcuts.Children.Add(Ui.Text("只有全局显隐在其他软件中生效。其余快捷键在练习窗内使用；提示会直接显示完整答案。", 11));
            shortcuts.Children.Add(Ui.Text("重练从本组第一个词开始，清空本轮进度和输入，保留累计正确、错误次数。", 11));
            shortcuts.Children.Add(Ui.Text("Enter 提交拼写 · Ctrl+, 打开设置 · Ctrl+S 查看学习记录。退出程序可在设置页或托盘完成。", 11));
            shortcuts.Children.Add(Ui.Text("录入时暂停全局显隐，离开录入框即恢复。Enter 提交、Ctrl+S 学习记录和 Ctrl+, 设置保持固定。", 11));
            shortcuts.Children.Add(Ui.Button("恢复默认快捷键", delegate
            {
                AppSettings defaults = new AppSettings(); hotkey.Text = defaults.Hotkey; hide.Text = defaults.HideShortcut;
                hint.Text = defaults.HintShortcut; skip.Text = defaults.SkipShortcut; restartShortcut.Text = defaults.RestartGroupShortcut;
                report("已恢复默认快捷键，保存后生效。");
            }));

            Grid recordPanel = new Grid { Margin = new Thickness(14, 10, 14, 8) };
            recordPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            recordPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            recordPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            recordPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            recordsTab = new TabItem { Header = "学习记录", Content = recordPanel }; tabs.Items.Add(recordsTab);
            recordPanel.Children.Add(Ui.Text("累计记录 · 正确率 = 正确次数 ÷（正确次数 + 错误次数）\n输入正确即计一次；按 Enter 提交错误才计错，提示和跳过不计次。", 11));
            search = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 4, 0, 10), ToolTip = "按单词或中文搜索" };
            System.Windows.Automation.AutomationProperties.SetName(search, "搜索学习记录");
            Grid.SetRow(search, 1); recordPanel.Children.Add(search);
            records = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                CanUserReorderColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Background = Brushes.White,
                RowBackground = Brushes.White, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 248, 247)),
                EnableRowVirtualization = true, RowHeight = 31, SelectionMode = DataGridSelectionMode.Single };
            Style wordStyle = new Style(typeof(TextBlock));
            wordStyle.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding("Meaning")));
            wordStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            records.Columns.Add(new DataGridTextColumn { Header = "单词", Binding = new Binding("Word"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 150, ElementStyle = wordStyle });
            records.Columns.Add(new DataGridTextColumn { Header = "正确次数", Binding = new Binding("Correct"), Width = 88 });
            records.Columns.Add(new DataGridTextColumn { Header = "错误次数", Binding = new Binding("Errors"), Width = 88 });
            records.Columns.Add(new DataGridTextColumn { Header = "正确率", Binding = new Binding("Accuracy") { StringFormat = "P1", TargetNullValue = "—" }, Width = 88 });
            Grid.SetRow(records, 2); recordPanel.Children.Add(records);
            empty = Ui.Text("", 13); empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center; empty.IsHitTestVisible = false;
            Grid.SetRow(empty, 2); recordPanel.Children.Add(empty);
            StackPanel dataButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            dataButtons.Children.Add(Ui.Button("备份学习记录…", delegate { Run(delegate { string message = backup(this); if (message.Length > 0) report(message); }); }));
            dataButtons.Children.Add(Ui.Button("恢复学习记录…", delegate
            {
                // 恢复也会替换设置，关闭旧表单以免用户随后保存过期值。
                Run(delegate { if (restore(this)) Close(); });
            }));
            Grid.SetRow(dataButtons, 3); recordPanel.Children.Add(dataButtons);
            search.TextChanged += delegate { Run(RefreshRecords); };
            tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (e.Source == tabs && tabs.SelectedItem == recordsTab) Run(RefreshRecords);
            };

            DockPanel footer = new DockPanel { LastChildFill = false };
            Button exitButton = Ui.Button("退出程序", exit); DockPanel.SetDock(exitButton, Dock.Left); footer.Children.Add(exitButton);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Ui.Button("取消", Close));
            Action<bool> save = delegate(bool restart)
            {
                Run(delegate
                {
                    AppSettings result = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<AppSettings>(
                        new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(settings));
                    result.Chapter = chapter.SelectedIndex <= 0 ? "" : (string)chapter.SelectedItem;
                    groups.ApplyTo(result);
                    result.ReviewOnly = false; result.Topmost = top.IsChecked == true;
                    result.PositionLocked = locked.IsChecked == true; result.Hotkey = hotkey.Text;
                    result.HideShortcut = hide.Text; result.HintShortcut = hint.Text; result.SkipShortcut = skip.Text;
                    result.RestartGroupShortcut = restartShortcut.Text;
                    result.FontSize = Convert.ToInt32(font.SelectedItem ?? new AppSettings().FontSize); result.Width = Convert.ToDouble(width.SelectedItem ?? 360);
                    result.Backdrop = background.SelectedIndex == 1 ? "clear" : background.SelectedIndex == 2 ? "solid" : "frosted";
                    result.BackdropOpacity = opacity.Value / 100; result.DarkAppearance = dark.IsChecked == true;
                    result.CaretBlinkEnabled = caretBlink.IsChecked == true;
                    LearningStore.ValidateSettings(result);
                    apply(result, restart); DialogResult = true;
                });
            };
            restartGroup.Click += delegate { save(true); };
            buttons.Children.Add(Ui.Button("保存设置", delegate { save(false); }));
            DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); Grid.SetRow(footer, 3); root.Children.Add(footer);
        }

        internal void ShowLearningRecords()
        {
            tabs.SelectedItem = recordsTab;
        }

        private void RefreshRecords()
        {
            string query = search.Text.Trim();
            List<WordRecordView> items = readStatistics().Select(s => new WordRecordView {
                Word = string.Join(" / ", words[s.WordId].Answers), Meaning = words[s.WordId].Meaning,
                Correct = s.Correct, Errors = s.Errors, Accuracy = s.Accuracy
            }).Where(w => query.Length == 0 || w.Word.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                w.Meaning.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            records.ItemsSource = items; empty.Text = query.Length == 0 ? "还没有作答记录。" : "没有匹配的学习记录。";
            empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Run(Action operation)
        {
            try { operation(); }
            catch (Exception ex) { Program.Log(ex); status.Text = ex.Message; status.ToolTip = ex.Message; status.Foreground = Brushes.Firebrick; }
        }

        private static ShortcutRecorder AddShortcut(Panel panel, string label, string value, bool global,
            Action<bool> captureChanged, Action<string> report)
        {
            panel.Children.Add(Ui.Text(label, 12));
            ShortcutRecorder recorder = new ShortcutRecorder(label, value, global, captureChanged, report)
                { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(recorder); return recorder;
        }
    }
}
