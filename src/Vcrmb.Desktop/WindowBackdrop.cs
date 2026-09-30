using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vcrmb.Core;

namespace Vcrmb.Desktop
{
    // Windows 10 的 Accent Policy 接口与 TranslucentTB 同类；不修改任务栏或系统透明设置。
    // 接口拒绝或桌面合成关闭时使用实色，不把文字一起降低透明度。
    internal sealed class WindowBackdrop : IDisposable
    {
        private const int AccentPolicyAttribute = 19;
        private readonly Window window;
        private readonly PracticeSurface surface;
        private IntPtr handle;
        private HwndSource nativeSource;
        private DispatcherOperation regionUpdate;
        private bool updatingRegion;
        private int regionWidth;
        private int regionHeight;
        private double regionScale;
        internal string EffectiveMode { get; private set; }
        internal string Failure { get; private set; }

        internal WindowBackdrop(Window window, PracticeSurface surface)
        {
            this.window = window; this.surface = surface;
            window.Closed += OnClosed;
        }

        internal void Apply(AppSettings settings)
        {
            handle = new WindowInteropHelper(window).Handle;
            bool highContrast = SystemParameters.HighContrast;
            surface.SetTheme(settings.DarkAppearance, highContrast);
            Color tint = settings.DarkAppearance ? Color.FromRgb(28, 36, 34) : Color.FromRgb(244, 248, 246);
            Failure = null; EffectiveMode = "solid";
            surface.Background = highContrast ? SystemColors.WindowBrush : new SolidColorBrush(tint);
            if (handle == IntPtr.Zero) return;
            HwndSource source = HwndSource.FromHwnd(handle);
            if (source != nativeSource)
            {
                if (nativeSource != null && !nativeSource.IsDisposed) nativeSource.RemoveHook(WindowMessage);
                nativeSource = source;
                if (nativeSource != null) nativeSource.AddHook(WindowMessage);
                regionWidth = regionHeight = 0;
            }
            window.Background = Brushes.Transparent;
            if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            bool composed;
            if (DwmIsCompositionEnabled(out composed) < 0 || !composed || highContrast || settings.Backdrop == "solid")
            {
                SetAccent(0, 0); SetMargins(false); ScheduleRegionUpdate(); return;
            }
            if (!SetMargins(true)) { Failure = "无法启用桌面透明合成"; ScheduleRegionUpdate(); return; }
            byte opacity = (byte)Math.Round(settings.BackdropOpacity * 255);
            uint color = ((uint)Math.Max((byte)1, opacity) << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
            if (settings.Backdrop == "clear")
            {
                if (SetAccent(2, 0)) { EffectiveMode = "clear"; surface.Background = new SolidColorBrush(Color.FromArgb(1, tint.R, tint.G, tint.B)); }
            }
            else if (SetAccent(4, color))
            {
                EffectiveMode = "acrylic"; surface.Background = Brushes.Transparent;
            }
            else if (SetAccent(3, 0))
            {
                EffectiveMode = "blur"; surface.Background = new SolidColorBrush(Color.FromArgb(opacity, tint.R, tint.G, tint.B));
            }
            if (EffectiveMode == "solid")
            {
                Failure = "系统未接受透明效果，已使用纯色背景"; SetAccent(0, 0); SetMargins(false);
            }
            ScheduleRegionUpdate();
        }

        internal void BeginDrag(AppSettings settings)
        {
            // Windows 10 的 Acrylic 在拖动循环中可能卡顿；移动期间使用轻量模糊，松手恢复。
            if (EffectiveMode != "acrylic" || !SetAccent(3, 0)) return;
            byte opacity = (byte)Math.Round(settings.BackdropOpacity * 255);
            surface.Background = new SolidColorBrush(settings.DarkAppearance ? Color.FromArgb(opacity, 28, 36, 34) : Color.FromArgb(opacity, 244, 248, 246));
        }

        private IntPtr WindowMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // SizeChanged 发生时 HWND 可能仍是上一轮尺寸。原生窗口完成调整后再同步圆角裁剪。
            if (message == 0x0047 || message == 0x02E0) ScheduleRegionUpdate(); // WM_WINDOWPOSCHANGED / WM_DPICHANGED
            return IntPtr.Zero;
        }

        private void ScheduleRegionUpdate()
        {
            if (handle == IntPtr.Zero || updatingRegion || regionUpdate != null) return;
            // 先让 WPF 完成本轮原生尺寸消息，避免 SetWindowRgn 的同步消息打断宽度和布局更新。
            regionUpdate = window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
            {
                regionUpdate = null; UpdateRegion();
            });
        }

        private void UpdateRegion()
        {
            if (handle == IntPtr.Zero || updatingRegion) return;
            NativeMethods.Rect rectangle;
            if (!NativeMethods.GetWindowRect(handle, out rectangle)) return;
            int width = rectangle.Right - rectangle.Left, height = rectangle.Bottom - rectangle.Top;
            double scale = Math.Max(96, NativeMethods.GetDpiForWindow(handle)) / 96.0;
            if (width <= 0 || height <= 0 || (width == regionWidth && height == regionHeight && scale == regionScale)) return;
            IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, (int)(20 * scale), (int)(20 * scale));
            if (region == IntPtr.Zero) return;
            // SetWindowRgn 自身也同步发送窗口位置消息，防止重入反复设置同一个区域。
            updatingRegion = true;
            try
            {
                if (SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
                else { regionWidth = width; regionHeight = height; regionScale = scale; } // 成功后由系统持有区域句柄。
            }
            finally { updatingRegion = false; }
        }

        private void OnClosed(object sender, EventArgs e) { Dispose(); }

        public void Dispose()
        {
            window.Closed -= OnClosed;
            if (regionUpdate != null) { regionUpdate.Abort(); regionUpdate = null; }
            if (nativeSource != null && !nativeSource.IsDisposed) nativeSource.RemoveHook(WindowMessage);
            nativeSource = null; handle = IntPtr.Zero;
        }

        private bool SetMargins(bool extend)
        {
            Margins margins = new Margins { Left = extend ? -1 : 0, Right = extend ? -1 : 0, Top = extend ? -1 : 0, Bottom = extend ? -1 : 0 };
            return DwmExtendFrameIntoClientArea(handle, ref margins) >= 0;
        }
        private bool SetAccent(int state, uint color)
        {
            AccentPolicy policy = new AccentPolicy { State = state, Flags = state == 4 ? 0 : 2, Color = color };
            int size = Marshal.SizeOf(typeof(AccentPolicy)); IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, memory, false);
                CompositionAttribute data = new CompositionAttribute { Attribute = AccentPolicyAttribute, Data = memory, Size = (IntPtr)size };
                try { return SetWindowCompositionAttribute(handle, ref data); }
                catch (EntryPointNotFoundException) { return false; }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State; public int Flags; public uint Color; public int Animation; }
        [StructLayout(LayoutKind.Sequential)] private struct CompositionAttribute { public int Attribute; public IntPtr Data; public IntPtr Size; }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowCompositionAttribute(IntPtr window, ref CompositionAttribute data);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr item);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    }
}
