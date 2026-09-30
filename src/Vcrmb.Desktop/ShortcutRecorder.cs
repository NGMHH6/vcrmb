using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Vcrmb.Core;

namespace Vcrmb.Desktop
{
    internal sealed class ShortcutRecorder : TextBox
    {
        internal ShortcutRecorder(string name, string value, bool global, Action<bool> captureChanged, Action<string> report)
        {
            Text = value; IsReadOnly = true; Padding = new Thickness(8, 6, 8, 6); MinHeight = 34;
            ToolTip = "点击后直接按下组合键，按 Tab 或点击其他位置完成录入。保存后生效。";
            AutomationProperties.SetName(this, name + "快捷键");
            InputMethod.SetIsInputMethodEnabled(this, false);
            GotKeyboardFocus += delegate { captureChanged(true); SelectAll(); report("正在录入“" + name + "”，按 Tab 或点击其他位置结束。"); };
            LostKeyboardFocus += delegate { captureChanged(false); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Tab) return;
                e.Handled = true;
                if (e.IsRepeat || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                    key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return;
                try
                {
                    ModifierKeys modifiers = Keyboard.Modifiers;
                    if ((modifiers & ModifierKeys.Windows) != 0) throw new ArgumentException("请使用 Ctrl、Alt、Shift 组合。");
                    string text = ((modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "") +
                        ((modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "") +
                        ((modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "") + key;
                    Text = ShortcutRules.Parse(text, global).Text; SelectAll();
                    report("已录入 " + Text + "；保存后生效。按 Tab 或点击其他位置结束录入。");
                }
                catch (ArgumentException ex) { report(ex.Message); }
            };
        }
    }
}
