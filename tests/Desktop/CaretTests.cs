using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vcrmb.Desktop;

// 只操作本进程的离屏窗口，不注入系统按键，也不更改系统光标参数。
internal static class CaretTests
{
    private static string stage;
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void RunAll(Action<string, Action> run, string output)
    {
        run("steady caret stays visible and follows editing scrolling theme focus and blink mode", delegate
        {
            OnTestDesktop(delegate
            {
            stage = "constructing controls";
            PracticeSurface surface = new PracticeSurface { Background = new SolidColorBrush(Color.FromRgb(244, 248, 246)) };
            surface.SetMeaning("n. 大气层；氛围"); surface.Example.Text = "The approaching examination created a tense ______ on the campus.";
            TextBox other = new TextBox(); StackPanel content = new StackPanel(); content.Children.Add(surface); content.Children.Add(other);
            Window window = new Window { Content = content, Width = 360, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000,
                UseLayoutRounding = true };
            try
            {
                stage = "showing window";
                window.Show(); Drain(40); PracticeTextBox answer = surface.Answer;
                stage = "focusing input";
                answer.Focus(); Drain(40);
                Assert(answer.IsKeyboardFocused, "Test input did not receive WPF keyboard focus.");
                Assert(NativeMethods.GetForegroundWindow() != new WindowInteropHelper(window).Handle, "The offscreen test took foreground focus.");
                Border caret = (Border)answer.Template.FindName("PART_SteadyCaret", answer);
                Assert(!answer.CaretBlinkEnabled && answer.CaretBrush == Brushes.Transparent, "Default input still uses a blinking caret.");
                CheckCaret(answer, caret);
                answer.Text = "atmos"; answer.CaretIndex = answer.Text.Length; Drain(40); CheckCaret(answer, caret);
                string preview = Path.Combine(output, "appearance-preview", "steady-caret.png");
                stage = "rendering static frames";
                byte[] first = Render(surface, preview);
                for (int frame = 0; frame < 8; frame++)
                {
                    Drain(150); CheckCaret(answer, caret);
                    Assert(first.SequenceEqual(Render(surface, null)), "Static caret frame changed during the blink interval.");
                }
                foreach (int position in new[] { 0, 2, answer.Text.Length })
                { answer.CaretIndex = position; Drain(30); CheckCaret(answer, caret); }
                answer.SelectAll(); Drain(30);
                Assert(caret.Visibility == Visibility.Collapsed && answer.SelectedText == "atmos", "Selection left a stray steady caret.");
                answer.SelectedText = "breez"; answer.CaretIndex = answer.Text.Length; Drain(30); CheckCaret(answer, caret);
                Assert(answer.Text == "breez", "Caret customization broke replacement editing.");
                stage = "scrolling long input";
                answer.Text = new string('w', 60); answer.CaretIndex = answer.Text.Length; Drain(40);
                Assert(answer.HorizontalOffset > 0, "Long input did not scroll."); CheckCaret(answer, caret);
                answer.FontSize = 20; window.Width = 320; Drain(40); CheckCaret(answer, caret);
                stage = "changing theme and mode";
                surface.SetTheme(true, false); Drain(30);
                Assert(caret.Background == answer.Foreground && answer.CaretBrush == Brushes.Transparent, "Theme reset the static caret color or mode.");
                surface.SetTheme(false, true); Drain(30); CheckCaret(answer, caret);
                Assert(caret.Background == SystemColors.WindowTextBrush, "High contrast caret color was not preserved.");
                answer.CaretBlinkEnabled = true; Drain(30);
                Assert(caret.Visibility == Visibility.Collapsed && answer.CaretBrush == answer.Foreground,
                    "Enabling blinking did not return caret drawing to WPF.");
                answer.CaretBlinkEnabled = false; Drain(30); CheckCaret(answer, caret);
                answer.IsReadOnly = true; Drain(30); Assert(caret.Visibility == Visibility.Collapsed, "Read-only input retained a caret.");
                answer.IsReadOnly = false; Drain(30); CheckCaret(answer, caret);
                other.Focus(); Drain(30); Assert(caret.Visibility == Visibility.Collapsed, "Unfocused input retained a caret.");
                answer.Focus(); Drain(30); CheckCaret(answer, caret);
                window.Hide(); Drain(30); Assert(caret.Visibility == Visibility.Collapsed, "Hidden input retained a caret.");
                Assert(NativeMethods.GetForegroundWindow() != new WindowInteropHelper(window).Handle, "The caret test took foreground focus.");
            }
            finally { stage = "closing window"; window.Close(); }
            });
        });
    }

    // 焦点测试使用独立、从不切换到前台的桌面，用户输入不会进入测试窗口。
    private static void OnTestDesktop(Action action)
    {
        foreach (Brush brush in new[] { Ui.Ink, Ui.Muted, Ui.Accent, Ui.Line }) if (!brush.IsFrozen) brush.Freeze();
        IntPtr desktop = CreateDesktop("VcrmbCaretTest-" + Guid.NewGuid().ToString("N"), IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        Exception failure = null;
        Thread thread = new Thread(delegate()
        {
            try
            {
                stage = "attaching test desktop";
                if (!SetThreadDesktop(desktop)) throw new Win32Exception(Marshal.GetLastWin32Error());
                action();
            }
            catch (Exception ex) { failure = ex; }
            finally { stage = "finished"; }
        });
        thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
            // WPF 的共享资源可能回到应用 Dispatcher 取值，等待期间继续处理主线程消息。
            DispatcherFrame frame = new DispatcherFrame(); DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += delegate
            { if (!thread.IsAlive || DateTime.UtcNow >= deadline) { timer.Stop(); frame.Continue = false; } };
            timer.Start(); Dispatcher.PushFrame(frame);
            if (!thread.Join(0)) throw new TimeoutException("Isolated caret test did not finish: " + stage);
            if (failure != null) throw new InvalidOperationException("Isolated caret test failed.", failure);
        }
        finally { CloseDesktop(desktop); }
    }

    private static void CheckCaret(PracticeTextBox answer, Border caret)
    {
        Assert(caret.Visibility == Visibility.Visible && caret.ActualHeight > 8 && caret.ActualWidth >= 1,
            "Steady caret is missing or has no visible size: visibility=" + caret.Visibility + ", size=" + caret.ActualWidth + "x" + caret.ActualHeight +
            ", index=" + answer.CaretIndex + "/" + answer.Text.Length + ", focus=" + answer.IsKeyboardFocused +
            ", layout=" + answer.IsMeasureValid + "/" + answer.IsArrangeValid + ", rect=" + answer.GetRectFromCharacterIndex(answer.CaretIndex));
        Rect expected = answer.GetRectFromCharacterIndex(answer.CaretIndex);
        Point actual = caret.TranslatePoint(new Point(), answer);
        Assert(!expected.IsEmpty && Math.Abs(actual.X - expected.X) <= caret.ActualWidth + 1 && Math.Abs(actual.Y - expected.Y) <= 1,
            "Steady caret does not follow the actual text insertion position.");
        Assert(actual.X >= 0 && actual.X + caret.ActualWidth <= answer.ActualWidth + 1, "Steady caret is outside the input viewport.");
    }

    private static byte[] Render(FrameworkElement surface, string path)
    {
        surface.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight),
            96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        if (path != null)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (Stream stream = File.Create(path)) encoder.Save(stream);
        }
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
    }

    private static void Drain(int milliseconds)
    {
        DispatcherFrame frame = new DispatcherFrame(); DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr attributes);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);
}
