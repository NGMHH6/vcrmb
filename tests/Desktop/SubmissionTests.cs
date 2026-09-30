using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vcrmb.Core;
using Vcrmb.Desktop;

// 只向本进程窗口派发 WPF 事件，不显示或激活窗口，不向用户桌面注入按键。
internal static class SubmissionTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void RunAll(Action<string, Action> run, string output)
    {
        run("Enter reveals every answer on error, preserves the draft, and still submits correct short variants", delegate
        {
            string directory = Path.GetFullPath(Path.Combine(output, "submission-ui", Guid.NewGuid().ToString("N")));
            VocabularyFile library = VocabularyLoader.LoadBundled(); Word word = library.Entries.First(w => w.Answers.Contains("program"));
            string database = Path.Combine(directory, "learning.sqlite");
            using (LearningStore seed = new LearningStore(database))
                seed.SaveSettings(new AppSettings { GroupSize = 1, GroupNumber = word.Id, Backdrop = "solid", Topmost = false });
            string previousDirectory = Program.DataDirectory; bool previousUiTest = Program.UiTest;
            MainWindow window = null;
            using (HwndSource source = new HwndSource(new HwndSourceParameters("Vcrmb submission test")
                { WindowStyle = 0, Width = 1, Height = 1 }))
            try
            {
                Program.DataDirectory = directory; Program.UiTest = false;
                window = new MainWindow(directory); PracticeSurface surface = (PracticeSurface)window.Content;
                surface.Answer.Text = "progr";
                using (LearningStore saved = new LearningStore(database)) Assert(saved.Statistics().Count == 0, "Typing counted an error.");
                Enter(window, source);
                string expected = string.Join(" / ", word.Answers);
                Assert(surface.Hint.Text == expected && surface.Hint.Parent == surface.Meaning && surface.Feedback.Text == "" &&
                    surface.Answer.Text == "progr" && !surface.Answer.IsReadOnly &&
                    surface.InputRow.BorderThickness.Bottom == 1, "Wrong Enter did not reveal answers while preserving editable input.");
                using (LearningStore saved = new LearningStore(database))
                    Assert(saved.Statistics().Single().Errors == 1 && saved.Statistics().Single().Correct == 0 &&
                        saved.LoadActive().HintUsed && saved.LoadActive().Input == "progr", "Wrong Enter did not save exactly one error with its hint.");
                Render(surface, Path.Combine(output, "appearance-preview", "enter-error-hint.png"));
                surface.Answer.Text = "progra"; Drain(650);
                Assert(surface.Hint.Text == expected && surface.InputRow.BorderThickness.Bottom == 0,
                    "Editing hid the answer or kept stale error styling.");
                Dispose(window); window = new MainWindow(directory); surface = (PracticeSurface)window.Content;
                Assert(surface.Answer.Text == "progra" && surface.Hint.Text == expected, "Reopening lost the input or automatic hint.");
                surface.Answer.Text = " "; Enter(window, source);
                using (LearningStore saved = new LearningStore(database)) Assert(saved.Statistics().Single().Errors == 1, "Blank Enter counted an error.");
                surface.Answer.Text = "program"; Enter(window, source); Enter(window, source);
                using (LearningStore saved = new LearningStore(database))
                {
                    WordStatistics count = saved.Statistics().Single();
                    Assert(count.Correct == 1 && count.Errors == 1 && saved.LoadActive() == null,
                        "Correct short variant failed, or repeated submission counted twice.");
                }
                Drain(250);
                Assert(surface.InputRow.Visibility == Visibility.Collapsed && surface.Hint.Text == "" && surface.Feedback.Text == "",
                    "Successful Enter did not advance and clear the old hint.");
                Assert(!File.Exists(Path.Combine(directory, "error.log")), "The submission flow wrote an error log.");
            }
            finally
            {
                Dispose(window); Program.DataDirectory = previousDirectory; Program.UiTest = previousUiTest;
            }
        });
        run("restart key resets active and completed groups preserves totals and cancels pending advancement", delegate
        {
            string directory = Path.GetFullPath(Path.Combine(output, "restart-ui", Guid.NewGuid().ToString("N")));
            string database = Path.Combine(directory, "learning.sqlite");
            VocabularyFile library = VocabularyLoader.LoadBundled(); DateTime now = DateTime.UtcNow;
            AppSettings selected = new AppSettings { GroupSize = 2, Backdrop = "solid", Topmost = false };
            using (LearningStore seed = new LearningStore(database))
            {
                LearningSession session = new LearningSession(seed, library); session.Next(selected, now);
                session.SaveInput(session.Word.Answers[0]); session.Complete(true, now); session.Next(selected, now); session.SaveInput("other group draft");
                selected.GroupNumber = 2; session.SwitchScope(selected, now);
                session.SaveInput(session.Word.Answers[0]); session.Complete(true, now); session.Next(selected, now);
                session.SaveInput("wrong"); session.Error();
            }
            string previousDirectory = Program.DataDirectory; bool previousUiTest = Program.UiTest; MainWindow window = null;
            using (HwndSource source = new HwndSource(new HwndSourceParameters("Vcrmb restart test") { WindowStyle = 0, Width = 1, Height = 1 }))
            try
            {
                Program.DataDirectory = directory; Program.UiTest = false;
                window = new MainWindow(directory); PracticeSurface surface = (PracticeSurface)window.Content;
                Assert(surface.Answer.Text == "wrong" && surface.Hint.Text.Length > 0, "Restart fixture did not resume its hinted draft.");
                Assert(Press(window, source, Key.F3), "Default restart key was not handled.");
                Assert(surface.Answer.Text == "" && surface.Hint.Text == "" && !surface.Answer.IsReadOnly, "Restart retained the previous input or hint.");
                using (LearningStore saved = new LearningStore(database))
                {
                    Assert(saved.LoadActive().WordId == 3 && saved.LoadSettings().GroupNumber == 2 &&
                        saved.LoadPracticeGroup(selected).CompletedWordIds.Length == 0, "Restart did not begin at the current group's first word.");
                    Assert(saved.Statistics().Sum(s => s.Correct) == 2 && saved.Statistics().Sum(s => s.Errors) == 1 &&
                        saved.Suspended().Single(r => r.WordId == 2).Input == "other group draft", "Restart changed totals or another group's draft.");
                }
                surface.Answer.Text = library.Entries.Single(w => w.Id == 3).Answers[0]; Enter(window, source);
                Press(window, source, Key.F3); Drain(350);
                using (LearningStore saved = new LearningStore(database))
                    Assert(saved.LoadActive().WordId == 3 && saved.LoadPracticeGroup(selected).CompletedWordIds.Length == 0,
                        "A stale next-question timer advanced the restarted group.");
                foreach (int wordId in new[] { 3, 4 })
                {
                    surface.Answer.Text = library.Entries.Single(w => w.Id == wordId).Answers[0]; Enter(window, source); Drain(250);
                }
                Assert(surface.InputRow.Visibility == Visibility.Collapsed && surface.Example.Text.Contains("F3 重练本组"),
                    "Completed group did not show the restart shortcut.");
                Press(window, source, Key.F3);
                Assert(surface.InputRow.Visibility == Visibility.Visible && surface.Answer.Text == "", "Cannot restart a completed group.");
                using (LearningStore saved = new LearningStore(database))
                {
                    Assert(saved.LoadActive().WordId == 3 && saved.Statistics().Sum(s => s.Correct) == 5 && saved.Statistics().Sum(s => s.Errors) == 1,
                        "Restarting completion changed the selected group or cumulative counts.");
                    selected.RestartGroupShortcut = "F6"; saved.SaveSettings(selected);
                }
                Dispose(window); window = new MainWindow(directory); surface = (PracticeSurface)window.Content;
                surface.Answer.Text = "new draft";
                Assert(!Press(window, source, Key.F3) && surface.Answer.Text == "new draft", "Old restart shortcut stayed active after changing the binding.");
                Assert(Press(window, source, Key.F6) && surface.Answer.Text == "", "Saved custom restart shortcut did not work after reopening.");
                using (LearningStore saved = new LearningStore(database))
                    Assert(saved.LoadActive().WordId == 3 && saved.Statistics().Sum(s => s.Correct) == 5 && saved.Statistics().Sum(s => s.Errors) == 1,
                        "Custom shortcut changed cumulative counts.");
                Assert(!File.Exists(Path.Combine(directory, "error.log")), "Restarting a group wrote an unexpected error log.");
            }
            finally { Dispose(window); Program.DataDirectory = previousDirectory; Program.UiTest = previousUiTest; }
        });
    }

    private static void Enter(MainWindow window, HwndSource source)
    {
        Assert(Press(window, source, Key.Enter), "Enter did not reach the submit handler.");
    }

    private static bool Press(MainWindow window, HwndSource source, Key keyValue)
    {
        KeyEventArgs key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, keyValue)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        window.RaiseEvent(key); return key.Handled;
    }

    private static void Dispose(MainWindow window)
    {
        if (window == null) return;
        window.DisposeResources();
        // 正常关闭会隐藏到托盘；测试资源已释放，此处关闭本进程窗口。
        window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; };
        window.Close();
    }

    private static void Drain(int milliseconds)
    {
        DispatcherFrame frame = new DispatcherFrame(); DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void Render(PracticeSurface surface, string path)
    {
        surface.Width = 360; surface.Background = new SolidColorBrush(Color.FromRgb(244, 248, 246));
        surface.Measure(new Size(360, double.PositiveInfinity)); surface.Arrange(new Rect(new Point(), surface.DesiredSize)); surface.UpdateLayout();
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface); PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (Stream stream = File.Create(path)) encoder.Save(stream);
    }
}
