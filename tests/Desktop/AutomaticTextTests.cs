using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Vcrmb.Core;
using Vcrmb.Desktop;

internal static class AutomaticTextTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void RunAll(Action<string, Action> run)
    {
        run("automatic text samples exclude foreground pixels and distinguish separate background regions", delegate
        {
            const int width = 120, height = 60;
            byte[] pixels = new byte[width * height * 4], mask = new byte[pixels.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                byte background = y < height / 2 ? (byte)0 : (byte)255;
                // 自身文字故意占据大部分图像；忽略这些像素后仍应读到真正背景。
                if (x < 80) { background = (byte)(255 - background); mask[offset + 3] = 255; }
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = background;
            }
            double top = DesktopBackgroundSampler.Measure(pixels, mask, width, height, new Int32Rect(0, 0, width, 29), 2, 3);
            double bottom = DesktopBackgroundSampler.Measure(pixels, mask, width, height, new Int32Rect(0, 31, width, 29), 2, 3);
            Assert(top < 0.01 && bottom > 0.99, "Foreground contamination or whole-window averaging chose the wrong text tone.");
            Assert(double.IsNaN(DesktopBackgroundSampler.Measure(pixels, mask, width, height,
                new Int32Rect(0, 0, width, height), 2, 3, (x, y) => false)), "Unavailable monitor pixels became a valid background.");
        });
        run("automatic text hysteresis rejects one-frame changes and sampling failures break pending transitions", delegate
        {
            TextContrastDecision decision = new TextContrastDecision();
            Assert(decision.Observe(0.01) && decision.DarkBackground, "Initial dark background was not detected.");
            Assert(!decision.Observe(0.9) && decision.DarkBackground, "A single bright frame switched text.");
            decision.Observe(double.NaN);
            Assert(!decision.Observe(0.9), "A capture failure did not break pending confirmation.");
            Assert(decision.Observe(0.9) && !decision.DarkBackground, "Stable bright background did not switch text.");
            foreach (double gray in new[] { 0.18, 0.21, 0.19, 0.23 })
                Assert(!decision.Observe(gray) && !decision.DarkBackground, "Boundary gray caused flicker.");
            decision.Observe(0.01);
            Assert(decision.Observe(0.01) && decision.DarkBackground, "Stable dark background did not restore light text.");
        });
        run("automatic text recoloring preserves input selection hints and answer status without effects", delegate
        {
            PracticeSurface surface = new PracticeSurface();
            surface.SetMeaning("n. 大气层"); surface.Answer.Text = "atmos"; surface.Answer.Select(1, 3);
            surface.ShowFeedback("atmosphere", false); surface.MarkAnswer(false);
            string help = AutomationProperties.GetHelpText(surface.Answer);
            surface.SetAutomaticContrast(true, false, true, false);
            Assert(surface.Answer.Text == "atmos" && surface.Answer.SelectedText == "tmo" && surface.Hint.Text == "atmosphere",
                "A palette update changed the active answer or hint.");
            Assert(surface.InputRow.BorderThickness.Bottom == 1 && AutomationProperties.GetHelpText(surface.Answer) == help,
                "Changing colors cleared the wrong-answer indication.");
            Assert(((SolidColorBrush)surface.Meaning.Foreground).Color.R > 230 &&
                ((SolidColorBrush)surface.Example.Foreground).Color.R < 80, "Separate background regions share one wrong palette.");
            Assert(surface.Effect == null && surface.Child.Effect == null && surface.Meaning.Effect == null &&
                surface.Example.Effect == null && surface.Answer.Effect == null, "Text effects returned.");
            surface.ShowFeedback("测试保存失败", true); surface.SetAutomaticContrast(false, true, false, true);
            Assert(surface.Feedback.Text == "测试保存失败" && surface.InputRow.BorderThickness.Bottom == 1, "Operational errors disappeared.");
            surface.SetTheme(false, true);
            Assert(surface.Answer.Foreground == SystemColors.WindowTextBrush, "High contrast did not override automatic colors.");
        });
        run("automatic text sampling stops for hidden solid and closed windows", delegate
        {
            PracticeSurface surface = new PracticeSurface(); surface.SetMeaning("n. 大气层");
            Window window = new Window { Content = surface, Width = 360, SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
            WindowBackdrop backdrop = new WindowBackdrop(window, surface);
            try
            {
                window.Show(); backdrop.Apply(new AppSettings { Backdrop = "clear" });
                Assert(backdrop.AutomaticText.Running == (backdrop.EffectiveMode == "clear"), "Sampling did not follow actual transparency support.");
                window.Hide(); Assert(!backdrop.AutomaticText.Running, "Hidden window still samples the screen.");
                window.Show(); backdrop.Apply(new AppSettings { Backdrop = "solid" });
                Assert(!backdrop.AutomaticText.Running, "Solid mode still samples the screen.");
            }
            finally { window.Close(); backdrop.Dispose(); }
            Assert(!backdrop.AutomaticText.Running, "Closing the window left the sampler running.");
        });
    }
}
