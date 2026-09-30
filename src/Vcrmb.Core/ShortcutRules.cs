using System;
using System.Collections.Generic;
using System.Linq;

namespace Vcrmb.Core
{
    public sealed class ShortcutGesture
    {
        public string Text { get; internal set; }
        public uint Modifiers { get; internal set; }
        public uint VirtualKey { get; internal set; }
        public bool Matches(uint key, uint modifiers) { return key == VirtualKey && modifiers == Modifiers; }
    }

    /// <summary>设置、按键录入和 Windows 注册共用同一规则，避免显示的组合与实际触发不一致。</summary>
    public static class ShortcutRules
    {
        public static ShortcutGesture Parse(string text, bool global)
        {
            string[] parts = (text ?? "").Split('+').Select(p => p.Trim()).ToArray();
            uint modifiers = 0;
            foreach (string part in parts.Take(parts.Length - 1))
            {
                uint flag;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": flag = 2; break;
                    case "alt": flag = 1; break;
                    case "shift": flag = 4; break;
                    default: throw new ArgumentException("修饰键支持 Ctrl、Alt、Shift。");
                }
                if ((modifiers & flag) != 0) throw new ArgumentException("同一个修饰键不能重复。");
                modifiers |= flag;
            }
            string key = parts.Last().ToUpperInvariant();
            if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key.Substring(1);
            uint virtualKey;
            int function;
            bool isFunction = key.StartsWith("F", StringComparison.Ordinal) && int.TryParse(key.Substring(1), out function);
            if (key.Length == 1 && ((key[0] >= 'A' && key[0] <= 'Z') || (key[0] >= '0' && key[0] <= '9')))
                virtualKey = key[0];
            else if (isFunction && int.TryParse(key.Substring(1), out function) && function >= 1 && function <= 11)
            { virtualKey = (uint)(0x70 + function - 1); key = "F" + function; }
            else if (key == "ESC" || key == "ESCAPE") { key = "Esc"; virtualKey = 0x1b; }
            else if (key == "SPACE") { key = "Space"; virtualKey = 0x20; }
            else if (key == "ENTER" || key == "RETURN") { key = "Enter"; virtualKey = 0x0d; }
            else if (key == "," || key == "OEMCOMMA") { key = ","; virtualKey = 0xbc; }
            else throw new ArgumentException("支持字母、数字、F1–F11、Esc、Space；F12 为系统保留键。");

            if (global && modifiers == 0) throw new ArgumentException("全局显隐键需要包含 Ctrl、Alt 或 Shift。");
            // Shift+字母和普通字符仍供拼写输入；窗口快捷键不能吞掉它们。
            if ((modifiers & 3) == 0 && !isFunction && !(virtualKey == 0x1b && modifiers == 0))
                throw new ArgumentException("字符快捷键需要包含 Ctrl 或 Alt；也可使用 F1–F11 或 Esc。");
            string normalized = ((modifiers & 2) != 0 ? "Ctrl+" : "") + ((modifiers & 1) != 0 ? "Alt+" : "") +
                ((modifiers & 4) != 0 ? "Shift+" : "") + key;
            if (new[] { "Ctrl+A", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+S", "Ctrl+,", "Alt+F4", "Ctrl+Shift+Esc" }.Contains(normalized))
                throw new ArgumentException(normalized + " 已用于文字编辑、检查答案、设置、统计或系统操作，请换一个组合。");
            return new ShortcutGesture { Text = normalized, Modifiers = modifiers, VirtualKey = virtualKey };
        }

        public static void Validate(AppSettings settings)
        {
            string[] names = { "全局显隐", "窗口隐藏", "提示答案", "跳过", "重练当前组" };
            string[] values = { settings.Hotkey, settings.HideShortcut, settings.HintShortcut, settings.SkipShortcut, settings.RestartGroupShortcut };
            var occupied = new Dictionary<string, string>();
            for (int i = 0; i < values.Length; i++)
            {
                string normalized = Parse(values[i], i == 0).Text;
                string previous;
                if (occupied.TryGetValue(normalized, out previous))
                    throw new ArgumentException(names[i] + "与" + previous + "不能使用同一个快捷键（" + normalized + "）。");
                occupied.Add(normalized, names[i]);
            }
        }

        // 仅供旧设置缺少重练键时补默认值；优先 F3，避开四个已有组合，不覆盖用户原配置。
        internal static string ChooseRestartShortcut(AppSettings settings)
        {
            string[] existing = { settings.Hotkey, settings.HideShortcut, settings.HintShortcut, settings.SkipShortcut };
            var occupied = new HashSet<string>(existing.Select((value, index) => Parse(value, index == 0).Text));
            return Enumerable.Range(3, 9).Select(number => "F" + number).First(key => !occupied.Contains(key));
        }
    }
}
