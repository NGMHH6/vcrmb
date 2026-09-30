using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vcrmb.Core;
using Vcrmb.Desktop;

// 检查本进程的控件及离屏 HWND，不向桌面窗口发送输入，也不替代系统合成效果的实机验收。
internal static class PresentationTests
{
    private static int failed;
    private static string output;
    private static readonly List<object> results = new List<object>();
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); results.Add(new { name = name, passed = true }); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); results.Add(new { name = name, passed = false, error = ex.ToString() }); }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.Combine(args[0], "appearance-preview"); Directory.CreateDirectory(output);
        Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Run("native clip follows hint expansion and collapse in every background mode", delegate { NativeLayoutTests.Run(args[0]); });
        Run("transparent input retains editing selection and hit geometry at minimum width", delegate
        {
            PracticeSurface surface = Surface(); surface.Width = 320;
            surface.Answer.Text = "carbondioxide"; Layout(surface);
            surface.Answer.Select(6, 0); surface.Answer.SelectedText = " ";
            Assert(surface.Answer.Text == "carbon dioxide", "Borderless template lost text editing.");
            surface.Answer.SelectAll(); Assert(surface.Answer.SelectedText == "carbon dioxide", "Selection broken.");
            Assert(surface.Answer.Template.FindName("PART_ContentHost", surface.Answer) is ScrollViewer, "Missing text host.");
            Point center = surface.Answer.TranslatePoint(new Point(surface.Answer.ActualWidth / 2, 15), surface);
            // 离屏控件没有可见 HWND，InputHitTest 会过滤不可见根；这里检查实际视觉命中几何。
            HitTestResult hitResult = VisualTreeHelper.HitTest(surface, center);
            DependencyObject hit = hitResult == null ? null : hitResult.VisualHit;
            bool inInput = false;
            while (hit != null) { if (hit == surface.Answer) { inInput = true; break; } hit = VisualTreeHelper.GetParent(hit); }
            Assert(inInput, "Transparent input lost its visual hit region.");
            Assert(surface.Answer.BorderThickness == new Thickness(0) && surface.Answer.ActualWidth > 240, "Input has unexpected chrome or clipping.");
        });
        Run("long cloze large fonts and answer variants stay inside the floating layout", delegate
        {
            PracticeSurface surface = Surface(); surface.Width = 320;
            surface.SetMeaning(string.Concat(Enumerable.Repeat("中文释义较长时仍应可读。", 6))); surface.Meaning.FontSize = 19;
            surface.Example.Text = string.Concat(Enumerable.Repeat("A long example keeps its ______ hidden while the text can be scrolled. ", 8));
            surface.Example.FontSize = 18; surface.Answer.FontSize = 20;
            surface.ShowFeedback("program / programme", false); Layout(surface);
            Assert(surface.DesiredSize.Height < 390 && surface.Answer.ActualHeight >= 30, "Content exceeds compact window bounds.");
            Assert(surface.Answer.TranslatePoint(new Point(0, surface.Answer.ActualHeight), surface).Y <= surface.ActualHeight, "Input clipped below window.");
            surface.MarkAnswer(false); Render(surface, "long-error.png");
            surface.ClearStatus(); Layout(surface);
            Assert(Find<Button>(surface) == null && Find<Image>(surface) == null, "Floating window still contains a toolbar button or logo.");
            Assert(surface.Answer.ActualWidth > 240, "Input area narrowed unexpectedly.");
            surface.ShowFeedback("操作未完成：测试保存失败", true); Layout(surface);
            Assert(surface.Hint.Text == "" && surface.Feedback.Text == "操作未完成：测试保存失败" &&
                surface.Feedback.TranslatePoint(new Point(), surface).Y >= surface.InputRow.TranslatePoint(new Point(), surface).Y,
                "Operational errors were lost or appended as answer text.");
            surface.ShowFeedback("", false); surface.SetMeaning("n. 下一题"); Layout(surface);
            Assert(surface.Meaning.Text == "n. 下一题" && surface.Hint.Text == "" && surface.Feedback.Text == "", "Previous hint leaked into the next meaning.");
        });
        Run("light dark clear and inline hints render without a bottom answer row", delegate
        {
            PracticeSurface surface = Surface(); surface.Width = 360; Layout(surface);
            double cleanHeight = surface.ActualHeight;
            Render(surface, "light.png");
            surface.Answer.Text = "atmos"; surface.ShowFeedback("atmosphere", false); Layout(surface);
            Assert(Math.Abs(surface.ActualHeight - cleanHeight) < 1 && surface.Hint.Text == "atmosphere" &&
                surface.Hint.Parent == surface.Meaning && surface.Feedback.Text == "", "Short hint was not appended inline to the meaning.");
            Assert(surface.Hint.FontSize == surface.Meaning.FontSize, "Inline answer did not inherit the meaning font size.");
            Render(surface, "hint.png"); surface.ShowFeedback("", false); Layout(surface);
            Assert(Math.Abs(surface.ActualHeight - cleanHeight) < 1, "Hidden prompt still reserves empty space.");
            surface.SetTheme(true, false); surface.Background = new SolidColorBrush(Color.FromRgb(28, 36, 34));
            Render(surface, "dark.png");
            surface.SetTheme(false, false); surface.Background = new SolidColorBrush(Color.FromArgb(1, 244, 248, 246));
            Render(surface, "clear.png");
            Assert(surface.Opacity == 1 && surface.Answer.Opacity == 1 && surface.Meaning.Opacity == 1, "Transparency faded foreground text.");
            surface.SetTheme(false, true); Assert(surface.Answer.Foreground == SystemColors.WindowTextBrush, "High contrast not respected.");
            surface = Surface(); surface.Width = 360;
            surface.SetMeaning("n. 水圈；大气中的水汽");
            surface.Example.Text = "All the water of the earth's surface is included in the ______";
            surface.ShowFeedback("hydrosphere", false); Render(surface, "hint-regression.png");
        });
        Run("settings appearance controls render and preserve all existing settings tabs", delegate
        {
            SettingsWindow settings = new SettingsWindow(new AppSettings(), VocabularyLoader.LoadBundled(), delegate { }, delegate { },
                delegate { return new List<WordStatistics>(); }, delegate { return ""; }, delegate { return false; }, delegate { });
            FrameworkElement content = (FrameworkElement)settings.Content; content.Width = 606; content.Height = 548;
            Layout(content); TabControl tabs = Find<TabControl>(content);
            Assert(tabs.Items.Cast<TabItem>().Select(t => (string)t.Header).SequenceEqual(new[] { "练习", "外观", "快捷键", "学习记录" }), "Settings tab lost.");
            tabs.SelectedIndex = 1; Layout(content); settings.Content = null;
            content.Width = double.NaN; content.Height = double.NaN;
            Border preview = new Border { Child = content, Background = Brushes.White, Width = 650, Height = 585 };
            Render(preview, "settings.png"); settings.Close();
        });
        GroupSettingsTests.RunAll(Run, args[0]);
        SubmissionTests.RunAll(Run, args[0]);
        CaretTests.RunAll(Run, args[0]);
        File.WriteAllText(Path.Combine(args[0], "presentation-test-results.json"), new JavaScriptSerializer().Serialize(new {
            utc = DateTime.UtcNow.ToString("o"), passed = results.Count - failed, failed = failed, tests = results }), Encoding.UTF8);
        Console.WriteLine("Presentation result: " + (results.Count - failed) + " passed, " + failed + " failed.");
        if (failed == 0) PrepareSmokeProfiles(args[0]);
        app.Shutdown(); return failed == 0 ? 0 : 1;
    }
    private static void PrepareSmokeProfiles(string artifacts)
    {
        string root = Path.Combine(artifacts, "appearance-smoke", Guid.NewGuid().ToString("N"));
        Dictionary<string, string> profiles = new Dictionary<string, string>();
        foreach (string mode in new[] { "frosted", "clear", "solid" })
        {
            string directory = Path.GetFullPath(Path.Combine(root, mode));
            using (LearningStore store = new LearningStore(Path.Combine(directory, "learning.sqlite")))
                store.SaveSettings(new AppSettings { Backdrop = mode, DarkAppearance = mode == "clear" });
            profiles.Add(mode, directory);
        }
        File.WriteAllText(Path.Combine(artifacts, "appearance-smoke-profiles.json"), new JavaScriptSerializer().Serialize(profiles), Encoding.UTF8);
    }
    private static PracticeSurface Surface()
    {
        PracticeSurface surface = new PracticeSurface()
            { Background = new SolidColorBrush(Color.FromRgb(244, 248, 246)) };
        System.Windows.Documents.TextElement.SetFontFamily(surface, new FontFamily("Microsoft YaHei UI"));
        surface.SetMeaning("n. 大气层；氛围");
        surface.Example.Text = "The approaching examination created a tense ______ on the campus.";
        return surface;
    }
    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(element.Width, double.IsNaN(element.Height) ? double.PositiveInfinity : element.Height));
        element.Arrange(new Rect(new Point(), element.DesiredSize)); element.UpdateLayout();
    }
    private static T Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            T typed = child as T; if (typed != null) return typed;
            T nested = Find<T>(child); if (nested != null) return nested;
        }
        return null;
    }
    private static void Render(FrameworkElement element, string name)
    {
        Layout(element);
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (Stream stream = File.Create(Path.Combine(output, name))) encoder.Save(stream);
    }
}
