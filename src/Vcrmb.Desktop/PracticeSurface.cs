using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Vcrmb.Desktop
{
    // 练习面的布局和状态外观独立于数据保存，透明模式仍保留完整的输入命中区域。
    internal sealed class PracticeSurface : Border
    {
        internal readonly TextBlock Meaning;
        internal readonly TextBlock Example;
        internal readonly TextBox Answer;
        internal readonly TextBlock Feedback;
        internal readonly Border InputRow;
        internal readonly FrameworkElement DragHandle;
        private readonly ScrollViewer feedbackHost;
        private readonly TextBlock status;
        private Brush ink;
        private Brush muted;
        private Brush accent;
        private Brush error;

        internal PracticeSurface()
        {
            CornerRadius = new CornerRadius(10); SnapsToDevicePixels = true;
            StackPanel body = new StackPanel { Margin = new Thickness(14, 10, 14, 8) }; Child = body;
            Meaning = Ui.Text("", 14); Meaning.FontWeight = FontWeights.SemiBold; Meaning.Margin = new Thickness(0, 1, 0, 5);
            // 释义本身兼作移动区域，不再占用一行显示图标或工具栏；输入和滚动条仍正常交互。
            Meaning.Background = Brushes.Transparent; Meaning.Cursor = Cursors.SizeAll; DragHandle = Meaning;
            body.Children.Add(Scroll(Meaning, 64));
            Example = Ui.Text("", 13); Example.FontFamily = new FontFamily("Segoe UI"); Example.Margin = new Thickness(0, 0, 0, 5);
            body.Children.Add(Scroll(Example, 116));
            Grid input = new Grid(); input.ColumnDefinitions.Add(new ColumnDefinition());
            input.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Answer = new TextBox { FontFamily = new FontFamily("Segoe UI"), FontSize = 16, MinHeight = 31,
                Padding = new Thickness(0, 3, 2, 3), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                MaxLength = 80, VerticalContentAlignment = VerticalAlignment.Center, AllowDrop = false, FocusVisualStyle = null };
            // 使用真正的 TextBox 内容宿主，保留光标、选择、输入法和滚动，不依赖系统输入框边框。
            FrameworkElementFactory host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            FrameworkElementFactory inputBackground = new FrameworkElementFactory(typeof(Border));
            inputBackground.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            inputBackground.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            inputBackground.AppendChild(host);
            Answer.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = inputBackground };
            AutomationProperties.SetName(Answer, "拼写答案");
            InputMethod.SetPreferredImeState(Answer, InputMethodState.Off); SpellCheck.SetIsEnabled(Answer, false);
            input.Children.Add(Answer);
            status = new TextBlock { FontSize = 14, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed }; Grid.SetColumn(status, 1); input.Children.Add(status);
            InputRow = new Border { Child = input, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 1, 0, 0) }; body.Children.Add(InputRow);
            Feedback = Ui.Text("", 12); Feedback.Margin = new Thickness(0, 4, 0, 1);
            feedbackHost = Scroll(Feedback, 55); feedbackHost.Visibility = Visibility.Collapsed; body.Children.Add(feedbackHost);
            SetTheme(false, false);
        }

        private static ScrollViewer Scroll(UIElement content, int maximum)
        {
            return new ScrollViewer { Content = content, MaxHeight = maximum, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        }

        internal void SetTheme(bool dark, bool highContrast)
        {
            ink = highContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#F3F7F6" : "#202C2A");
            muted = highContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#D0DAD7" : "#52615D");
            accent = highContrast ? SystemColors.HighlightBrush : Brush(dark ? "#A7E8CE" : "#126651");
            error = highContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#FFABAD" : "#AD303A");
            Meaning.Foreground = ink; Example.Foreground = muted; Answer.Foreground = ink; Answer.CaretBrush = ink;
            Answer.SelectionBrush = accent; Answer.SelectionOpacity = 0.3;
            Feedback.Foreground = accent;
            ClearStatus();
        }

        internal void ClearStatus()
        {
            InputRow.BorderThickness = new Thickness(0); Answer.Foreground = ink;
            status.Visibility = Visibility.Collapsed;
            AutomationProperties.SetHelpText(Answer, "");
        }

        internal void MarkAnswer(bool correct)
        {
            status.Text = correct ? "✓" : "×"; status.Foreground = correct ? accent : error;
            status.Visibility = Visibility.Visible; status.ToolTip = correct ? "正确" : "拼写不正确，请修改后重试";
            AutomationProperties.SetHelpText(Answer, (string)status.ToolTip);
            InputRow.BorderBrush = correct ? accent : error; InputRow.BorderThickness = new Thickness(0, 0, 0, 1);
        }

        internal void ShowFeedback(string text, bool failed)
        {
            Feedback.Text = text; Feedback.Foreground = failed ? error : accent;
            feedbackHost.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromInvariantString(color); }
    }
}
