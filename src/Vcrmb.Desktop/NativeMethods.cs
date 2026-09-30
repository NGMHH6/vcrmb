using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Vcrmb.Core;

namespace Vcrmb.Desktop
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, ForegroundChanged callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        internal delegate void ForegroundChanged(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr argument);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumWindow(IntPtr window, IntPtr argument);
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }

        internal static bool IsWorkWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;
            StringBuilder name = new StringBuilder(80); GetClassName(window, name, name.Capacity);
            return !new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "Progman", "WorkerW" }.Contains(name.ToString());
        }

        internal static void CloseFileDialogs()
        {
            // 只枚举本程序 UI 线程的通用对话框，不触碰其他应用窗口。
            EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr argument)
            {
                StringBuilder className = new StringBuilder(80);
                GetClassName(window, className, className.Capacity);
                if (className.ToString() == "#32770") PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        internal static void CapturePosition(Window window, AppSettings settings)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            Rect bounds;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out bounds)) return;
            Forms.Screen screen = Forms.Screen.FromHandle(handle);
            double scale = Math.Max(96, GetDpiForWindow(handle)) / 96.0;
            settings.Monitor = screen.DeviceName; settings.HasPosition = true;
            settings.Left = window.Left; settings.Top = window.Top;
            settings.EdgeRight = (screen.WorkingArea.Right - bounds.Right) / scale;
            settings.EdgeBottom = (screen.WorkingArea.Bottom - bounds.Bottom) / scale;
            settings.Width = window.Width;
        }

        internal static void Place(Window window, AppSettings settings)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            Forms.Screen screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor)
                ?? Forms.Screen.PrimaryScreen;
            Rect bounds; if (!GetWindowRect(handle, out bounds)) return;
            double scale = Math.Max(96, GetDpiForWindow(handle)) / 96.0;
            int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
            int right = settings.HasPosition ? (int)(Math.Max(0, settings.EdgeRight) * scale) : 12;
            int bottom = settings.HasPosition ? (int)(Math.Max(0, settings.EdgeBottom) * scale) : 12;
            int x = Math.Max(screen.WorkingArea.Left, Math.Min(screen.WorkingArea.Right - width,
                screen.WorkingArea.Right - width - right));
            int y = Math.Max(screen.WorkingArea.Top, Math.Min(screen.WorkingArea.Bottom - height,
                screen.WorkingArea.Bottom - height - bottom));
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015); // 不改尺寸、层级和输入焦点。
        }
    }

    internal sealed class HotkeyManager : IDisposable
    {
        private readonly IntPtr handle;
        private int currentId;
        private string current;
        private bool suspended;
        internal string ActiveShortcut { get { return current; } }
        internal HotkeyManager(IntPtr handle) { this.handle = handle; }

        internal string Set(string text)
        {
            ShortcutGesture gesture = ShortcutRules.Parse(text, true);
            string normalized = gesture.Text;
            if (normalized == current && !suspended) return normalized;
            int id = currentId == 1 ? 2 : 1;
            if (!NativeMethods.RegisterHotKey(handle, id, gesture.Modifiers | 0x4000, gesture.VirtualKey))
                throw new InvalidOperationException("快捷键 " + normalized + " 被占用或无法注册，请换一个组合。" +
                    (currentId != 0 && !suspended ? "原快捷键保持有效。" : "可从托盘打开设置或显示 / 隐藏练习窗。"));
            if (currentId != 0 && !suspended) NativeMethods.UnregisterHotKey(handle, currentId);
            currentId = id; current = normalized; suspended = false;
            return normalized;
        }

        // 录入框聚焦时暂时释放全局键，避免录入原组合时把设置窗口隐藏。
        internal void Suspend() { if (currentId != 0 && !suspended) NativeMethods.UnregisterHotKey(handle, currentId); suspended = true; }
        internal void Resume() { if (suspended && current != null) Set(current); else suspended = false; }
        internal bool Matches(IntPtr id) { return !suspended && currentId != 0 && id.ToInt32() == currentId; }
        public void Dispose() { if (currentId != 0 && !suspended) NativeMethods.UnregisterHotKey(handle, currentId); currentId = 0; current = null; suspended = false; }
    }
}
