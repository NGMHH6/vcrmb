using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Vcrmb.Desktop
{
    // 仅替换光标绘制，文字编辑、选择、输入法和滚动仍由原生 WPF TextBox 处理。
    // 不调用 SetCaretBlinkTime，避免更改其他应用的系统光标设置。
    internal sealed class PracticeTextBox : TextBox
    {
        private Canvas caretLayer;
        private Border steadyCaret;
        private ScrollViewer contentHost;
        private bool caretBlinkEnabled;

        internal bool CaretBlinkEnabled
        {
            get { return caretBlinkEnabled; }
            set { caretBlinkEnabled = value; ApplyCaretMode(); }
        }

        internal PracticeTextBox()
        {
            FrameworkElementFactory host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            FrameworkElementFactory layer = new FrameworkElementFactory(typeof(Canvas), "PART_SteadyCaretLayer");
            layer.SetValue(IsHitTestVisibleProperty, false); layer.SetValue(ClipToBoundsProperty, true);
            FrameworkElementFactory caret = new FrameworkElementFactory(typeof(Border), "PART_SteadyCaret");
            caret.SetValue(VisibilityProperty, Visibility.Collapsed); caret.SetValue(SnapsToDevicePixelsProperty, true);
            layer.AppendChild(caret);
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(Grid));
            content.AppendChild(host); content.AppendChild(layer);
            FrameworkElementFactory background = new FrameworkElementFactory(typeof(Border));
            background.SetValue(BackgroundProperty, Brushes.Transparent);
            background.SetValue(PaddingProperty, new TemplateBindingExtension(PaddingProperty));
            background.AppendChild(content);
            Template = new ControlTemplate(typeof(PracticeTextBox)) { VisualTree = background };
            CaretBrush = Brushes.Transparent;
            SelectionChanged += delegate { UpdateSteadyCaret(); };
            LayoutUpdated += delegate { UpdateSteadyCaret(); };
        }

        public override void OnApplyTemplate()
        {
            if (contentHost != null) contentHost.ScrollChanged -= OnScrollChanged;
            base.OnApplyTemplate();
            contentHost = GetTemplateChild("PART_ContentHost") as ScrollViewer;
            caretLayer = GetTemplateChild("PART_SteadyCaretLayer") as Canvas;
            steadyCaret = GetTemplateChild("PART_SteadyCaret") as Border;
            if (contentHost != null) contentHost.ScrollChanged += OnScrollChanged;
            ApplyCaretMode();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ForegroundProperty) ApplyCaretMode();
            else if (e.Property == IsKeyboardFocusedProperty || e.Property == IsReadOnlyProperty ||
                e.Property == IsVisibleProperty || e.Property == IsEnabledProperty) UpdateSteadyCaret();
        }

        private void ApplyCaretMode()
        {
            CaretBrush = caretBlinkEnabled ? Foreground : Brushes.Transparent;
            if (steadyCaret != null) steadyCaret.Background = Foreground;
            UpdateSteadyCaret();
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e) { UpdateSteadyCaret(); }

        private void UpdateSteadyCaret()
        {
            if (steadyCaret == null || caretLayer == null) return;
            if (caretBlinkEnabled || !IsKeyboardFocused || !IsVisible || !IsEnabled || IsReadOnly || SelectionLength > 0)
            { steadyCaret.Visibility = Visibility.Collapsed; return; }
            // 等待 TextBox 完成排版后再取实际插入位置，涵盖空输入、末尾、字号及水平滚动。
            if (!IsMeasureValid || !IsArrangeValid || caretLayer.ActualWidth <= 0) return;
            Rect rectangle = GetRectFromCharacterIndex(CaretIndex);
            if (rectangle.IsEmpty || rectangle.Height <= 0)
            { steadyCaret.Visibility = Visibility.Collapsed; return; }
            Point point = TranslatePoint(rectangle.TopLeft, caretLayer);
            double width = Math.Max(1, SystemParameters.CaretWidth);
            if (point.X < 0 || point.X > caretLayer.ActualWidth + 0.5)
            {
                // 改字号或窗口宽度时，WPF 可能保留旧滚动偏移，需把仍在输入的插入点带回视区。
                double delta = point.X < 0 ? point.X : point.X - caretLayer.ActualWidth + width;
                double offset = Math.Min(contentHost.ScrollableWidth, Math.Max(0, HorizontalOffset + delta));
                if (Math.Abs(offset - HorizontalOffset) > 0.5) ScrollToHorizontalOffset(offset);
                steadyCaret.Visibility = Visibility.Collapsed; return;
            }
            steadyCaret.Width = width; steadyCaret.Height = rectangle.Height;
            Canvas.SetLeft(steadyCaret, Math.Max(0, Math.Min(point.X, caretLayer.ActualWidth - width)));
            Canvas.SetTop(steadyCaret, point.Y);
            steadyCaret.Visibility = Visibility.Visible;
        }
    }
}
