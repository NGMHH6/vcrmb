using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace Vcrmb.Desktop
{
    // 迟滞与连续两次确认避免临界灰色、视频或鼠标经过时反复闪色。
    internal sealed class TextContrastDecision
    {
        internal bool DarkBackground { get; private set; }
        private bool initialized;
        private int pending;
        internal TextContrastDecision() { DarkBackground = true; }
        internal bool Observe(double luminance)
        {
            if (double.IsNaN(luminance)) { pending = 0; return false; }
            bool next = initialized ? (DarkBackground ? luminance < 0.24 : luminance <= 0.17) : luminance < 0.20;
            if (!initialized) { initialized = true; DarkBackground = next; return true; }
            if (next == DarkBackground) { pending = 0; return false; }
            if (++pending < 2) return false;
            DarkBackground = next; pending = 0; return true;
        }
        internal void ResetPending() { pending = 0; }
    }

    // 只在内存中读取悬浮窗覆盖的小块屏幕，不隐藏窗口，不保存或上传画面。
    // 用自身控件的透明度掩码剔除文字、光标、选区和状态线，避免采到自己的字色。
    internal sealed class DesktopBackgroundSampler : IDisposable
    {
        private Drawing.Bitmap screen;
        private RenderTargetBitmap foreground;
        private byte[] colors;
        private byte[] mask;
        private int width;
        private int height;
        private double scaleX;
        private double scaleY;
        internal string Failure { get; private set; }
        private static readonly double[] Linear = Enumerable.Range(0, 256).Select(v =>
            v <= 10 ? v / 255.0 / 12.92 : Math.Pow((v / 255.0 + 0.055) / 1.055, 2.4)).ToArray();

        internal double[] Read(PracticeSurface surface)
        {
            Failure = null;
            double[] result = { double.NaN, double.NaN, double.NaN, double.NaN };
            PresentationSource source = PresentationSource.FromVisual(surface);
            if (source == null || source.CompositionTarget == null || !surface.IsMeasureValid || !surface.IsArrangeValid) return result;
            Point origin = surface.PointToScreen(new Point());
            Matrix transform = source.CompositionTarget.TransformToDevice;
            int left = (int)Math.Floor(origin.X), top = (int)Math.Floor(origin.Y);
            int w = (int)Math.Ceiling(surface.ActualWidth * transform.M11), h = (int)Math.Ceiling(surface.ActualHeight * transform.M22);
            Drawing.Rectangle[] monitors = Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray();
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || (long)w * h > 16000000 ||
                !monitors.Any(m => m.IntersectsWith(new Drawing.Rectangle(left, top, w, h)))) return result;
            try
            {
                Prepare(w, h, transform.M11, transform.M22);
                foreground.Clear(); foreground.Render(surface); foreground.CopyPixels(mask, width * 4, 0);
                IntPtr desktop = GetDC(IntPtr.Zero);
                if (desktop == IntPtr.Zero) { Failure = "无法读取桌面颜色"; return result; }
                try
                {
                    using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(screen))
                    {
                        IntPtr destination = graphics.GetHdc();
                        try
                        {
                            if (!BitBlt(destination, 0, 0, width, height, desktop, left, top, 0x40CC0020))
                            { Failure = "桌面颜色采样失败"; return result; } // SRCCOPY | CAPTUREBLT
                        }
                        finally { graphics.ReleaseHdc(destination); }
                    }
                }
                finally { ReleaseDC(IntPtr.Zero, desktop); }
                Imaging.BitmapData pixels = screen.LockBits(new Drawing.Rectangle(0, 0, width, height),
                    Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppRgb);
                try
                {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(IntPtr.Add(pixels.Scan0, y * pixels.Stride), colors, y * width * 4, width * 4);
                }
                finally { screen.UnlockBits(pixels); }
                FrameworkElement[] regions = surface.ContrastRegions;
                for (int i = 0; i < regions.Length; i++)
                {
                    FrameworkElement region = regions[i];
                    if (!region.IsVisible || region.ActualWidth <= 0 || region.ActualHeight <= 0) continue;
                    Point point = region.TranslatePoint(new Point(), surface);
                    Int32Rect bounds = new Int32Rect((int)(point.X * scaleX), (int)(point.Y * scaleY),
                        (int)Math.Ceiling(region.ActualWidth * scaleX), (int)Math.Ceiling(region.ActualHeight * scaleY));
                    result[i] = Measure(colors, mask, width, height, bounds, Math.Max(1, (int)Math.Ceiling(scaleX * 1.5)),
                        Math.Max(3, (int)Math.Round(scaleX * 6)), (x, y) => monitors.Any(m => m.Contains(left + x, top + y)));
                }
            }
            catch (ExternalException ex) { Failure = "桌面颜色暂不可用：" + ex.Message; }
            return result;
        }

        internal static double Measure(byte[] pixels, byte[] alphaMask, int w, int h, Int32Rect region,
            int margin, int step, Func<int, int, bool> visible = null)
        {
            int[] histogram = new int[256]; int count = 0;
            for (int y = Math.Max(margin, region.Y); y < Math.Min(h - margin, region.Y + region.Height); y += step)
            for (int x = Math.Max(margin, region.X); x < Math.Min(w - margin, region.X + region.Width); x += step)
            {
                if (visible != null && !visible(x, y)) continue;
                bool covered = false;
                for (int dy = -margin; dy <= margin && !covered; dy++)
                for (int dx = -margin; dx <= margin; dx++)
                    if (alphaMask[((y + dy) * w + x + dx) * 4 + 3] > 4) { covered = true; break; }
                if (covered) continue;
                int offset = (y * w + x) * 4;
                double value = 0.2126 * Linear[pixels[offset + 2]] + 0.7152 * Linear[pixels[offset + 1]] + 0.0722 * Linear[pixels[offset]];
                histogram[(int)Math.Round(value * 255)]++; count++;
            }
            if (count < 12) return double.NaN;
            int remaining = (count + 1) / 2;
            for (int i = 0; i < histogram.Length; i++) { remaining -= histogram[i]; if (remaining <= 0) return i / 255.0; }
            return double.NaN;
        }

        private void Prepare(int w, int h, double sx, double sy)
        {
            if (screen != null && w == width && h == height && sx == scaleX && sy == scaleY) return;
            Dispose(); width = w; height = h; scaleX = sx; scaleY = sy;
            screen = new Drawing.Bitmap(w, h, Imaging.PixelFormat.Format32bppRgb);
            foreground = new RenderTargetBitmap(w, h, 96 * sx, 96 * sy, PixelFormats.Pbgra32);
            colors = new byte[w * h * 4]; mask = new byte[colors.Length];
        }
        public void Dispose()
        { if (screen != null) screen.Dispose(); screen = null; foreground = null; colors = mask = null; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    }

    internal sealed class AutomaticTextContrast : IDisposable
    {
        private readonly Window window;
        private readonly PracticeSurface surface;
        private readonly DesktopBackgroundSampler sampler = new DesktopBackgroundSampler();
        private readonly TextContrastDecision[] decisions = Enumerable.Range(0, 4).Select(i => new TextContrastDecision()).ToArray();
        private readonly DispatcherTimer timer;
        private bool enabled;
        private bool disposed;
        internal bool Running { get { return timer.IsEnabled; } }
        internal string Failure { get { return sampler.Failure; } }
        internal double[] Luminances { get; private set; }

        internal AutomaticTextContrast(Window window, PracticeSurface surface)
        {
            this.window = window; this.surface = surface;
            timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += Tick;
            window.IsVisibleChanged += VisibilityChanged; window.StateChanged += StateChanged;
        }
        internal void SetEnabled(bool value)
        {
            if (disposed) return;
            enabled = value;
            if (enabled) ApplyColors();
            UpdateTimer();
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { UpdateTimer(); }
        private void StateChanged(object sender, EventArgs e) { UpdateTimer(); }
        private void UpdateTimer()
        {
            bool run = enabled && window.IsVisible && window.WindowState != WindowState.Minimized && !SystemParameters.HighContrast;
            if (run) timer.Start();
            else
            {
                timer.Stop(); sampler.Dispose();
                foreach (TextContrastDecision decision in decisions) decision.ResetPending();
            }
        }
        private void Tick(object sender, EventArgs e)
        {
            if (disposed) return;
            UpdateTimer(); if (!timer.IsEnabled) return;
            Luminances = sampler.Read(surface); bool changed = false;
            for (int i = 0; i < decisions.Length; i++) changed |= decisions[i].Observe(Luminances[i]);
            if (changed) ApplyColors();
        }
        private void ApplyColors()
        { surface.SetAutomaticContrast(decisions[0].DarkBackground, decisions[1].DarkBackground,
            decisions[2].DarkBackground, decisions[3].DarkBackground); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; timer.Stop(); timer.Tick -= Tick;
            window.IsVisibleChanged -= VisibilityChanged; window.StateChanged -= StateChanged; sampler.Dispose();
        }
    }
}
