using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vcrmb.Core;
using Vcrmb.Desktop;

// 使用本测试进程自己的离屏 HWND 检查系统裁剪区域；不向用户桌面发送鼠标或键盘输入。
internal static class NativeLayoutTests
{
    internal static void Run(string artifacts)
    {
        List<object> measurements = new List<object>();
        List<string> failures = new List<string>();
        foreach (string mode in new[] { "frosted", "clear", "solid" })
        {
            PracticeSurface surface = new PracticeSurface();
            surface.SetMeaning("n. 水圈；大气中的水汽");
            surface.Example.Text = "All the water of the earth's surface is included in the ______.";
            Window window = new Window { Content = surface, Width = 360, MinHeight = 96, MaxHeight = 390,
                SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000,
                Background = Brushes.Transparent, FontFamily = new FontFamily("Microsoft YaHei UI"), UseLayoutRounding = true };
            WindowBackdrop backdrop = new WindowBackdrop(window, surface);
            window.SourceInitialized += delegate { backdrop.Apply(new AppSettings { Backdrop = mode }); };
            try
            {
                window.Show(); Drain();
                Check(window, surface, mode, "initial", measurements, failures);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    surface.ShowFeedback("hydrosphere", false); Drain();
                    Check(window, surface, mode, "hint-" + repeat, measurements, failures);
                    surface.ShowFeedback("", false); Drain();
                    Check(window, surface, mode, "hidden-" + repeat, measurements, failures);
                }
                window.Width = 320; surface.Meaning.FontSize = 19; surface.Example.FontSize = 18; surface.Answer.FontSize = 20;
                surface.ShowFeedback("program / programme", false); Drain();
                Check(window, surface, mode, "narrow-large-font-hint", measurements, failures, 320);
                window.Width = 560; Drain();
                Check(window, surface, mode, "wide-hint", measurements, failures, 560);
                window.Hide(); surface.ShowFeedback("", false); Drain();
                surface.ShowFeedback("hydrosphere", false); window.Show(); Drain();
                Check(window, surface, mode, "reshow-with-hint", measurements, failures, 560);
            }
            finally { window.Close(); Drain(); }
        }
        File.WriteAllText(Path.Combine(artifacts, "native-layout-results.json"),
            new JavaScriptSerializer().Serialize(new { utc = DateTime.UtcNow.ToString("o"), measurements = measurements, failures = failures }), Encoding.UTF8);
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
    }

    private static void Check(Window window, PracticeSurface surface, string mode, string phase, List<object> measurements, List<string> failures, int expectedWidth = 360)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        NativeMethods.Rect bounds, clip;
        if (!NativeMethods.GetWindowRect(handle, out bounds)) throw new InvalidOperationException("Cannot read test HWND bounds.");
        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        int kind;
        try
        {
            kind = GetWindowRgn(handle, region);
            if (kind == 0 || GetRgnBox(region, out clip) == 0) throw new InvalidOperationException("Cannot read test HWND region.");
        }
        finally { DeleteObject(region); }
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        double scale = NativeMethods.GetDpiForWindow(handle) / 96.0;
        bool hintVisible = surface.Hint.Text.Length > 0;
        Rect hintEnd = hintVisible ? surface.Hint.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward) : Rect.Empty;
        double feedbackBottom = hintEnd.IsEmpty ? 0 : surface.Meaning.TranslatePoint(hintEnd.BottomRight, surface).Y * scale;
        bool fits = clip.Right >= width && clip.Bottom >= height && clip.Right <= width + 1 && clip.Bottom <= height + 1;
        bool hintFits = !hintVisible || (!hintEnd.IsEmpty && hintEnd.Height > 10 && feedbackBottom > 0 && feedbackBottom <= clip.Bottom - 1);
        bool widthFits = Math.Abs(width - expectedWidth * scale) <= 1;
        measurements.Add(new { mode = mode, phase = phase, width = width, height = height, regionWidth = clip.Right,
            regionHeight = clip.Bottom, feedbackBottom = feedbackBottom, feedbackVisible = hintVisible,
            fits = fits, hintFits = hintFits, expectedWidth = expectedWidth, widthFits = widthFits });
        if (!fits) failures.Add(mode + "/" + phase + " HWND=" + width + "x" + height + " region=" + clip.Right + "x" + clip.Bottom);
        if (!hintFits) failures.Add(mode + "/" + phase + " hint clipped at " + feedbackBottom + " / " + clip.Bottom);
        if (!widthFits) failures.Add(mode + "/" + phase + " width changed unexpectedly: " + width + " expected " + expectedWidth * scale);
    }

    private static void Drain()
    {
        DispatcherFrame frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, (Action)delegate { frame.Continue = false; });
        Dispatcher.PushFrame(frame);
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int GetRgnBox(IntPtr region, out NativeMethods.Rect bounds);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr region);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
}
