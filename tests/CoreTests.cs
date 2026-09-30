using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Vcrmb.Core;

internal static class CoreTests
{
    private static int failed;
    private static readonly List<object> results = new List<object>();
    private static string output;
    private static VocabularyFile library;

    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception) { rejected = true; }
        Assert(rejected, "Expected operation to reject invalid input.");
    }
    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); results.Add(new { name = name, passed = true }); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); results.Add(new { name = name, passed = false, error = ex.ToString() }); }
    }
    private static string NewPath(string name)
    {
        string directory = Path.Combine(output, "test-data-v12", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return Path.Combine(directory, name);
    }
    private static LearningStore Store() { return new LearningStore(NewPath("learning.sqlite")); }
    private static Word Find(string word) { return library.Entries.First(w => w.Answers.Contains(word)); }
    private static WordStatistics Count(LearningStore store, Word word) { return store.Statistics().Single(s => s.WordId == word.Id); }
    private static SQLiteConnection Connect(string path)
    {
        var builder = new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false };
        SQLiteConnection connection = new SQLiteConnection(builder.ToString()); connection.Open(); return connection;
    }
    private static object Sql(string path, string sql, params object[] values)
    {
        using (SQLiteConnection connection = Connect(path))
        using (SQLiteCommand command = new SQLiteCommand(sql, connection))
        {
            for (int i = 0; i < values.Length; i++) command.Parameters.AddWithValue("@p" + i, values[i]);
            return command.ExecuteScalar();
        }
    }
    private static long Number(string path, string sql) { return Convert.ToInt64(Sql(path, sql)); }

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--crash-writer") return CrashWriter(args);
        output = args[0]; Directory.CreateDirectory(output);
        Run("embedded fixed vocabulary validates all 3674 entries and 22 chapters", delegate
        {
            library = VocabularyLoader.LoadBundled();
            Assert(library.Entries.Select(w => w.Id).SequenceEqual(Enumerable.Range(1, 3674)), "Fixed entries changed.");
            Assert(library.Entries.Select(w => w.Chapter).Distinct().Count() == 22, "Chapter count changed.");
            Assert(library.Entries.All(w => w.MaskedForms.Length > 0), "Unmasked sentence.");
            Assert(library.Entries.Where(w => new[] { 356, 939, 3100, 3141 }.Contains(w.Id))
                .All(w => w.ExampleOrigin == "locally-authored" && w.Cloze.Contains("______")), "Missing examples were not authored.");
            Assert(library.SourceCommit == "5cef573933663c4673c6e0093f1df04e68018b1a", "Source version drift.");
        });
        Run("external vocabulary in working directory cannot replace the embedded book", delegate
        {
            string previous = Environment.CurrentDirectory, path = NewPath("vocabulary.json");
            try
            {
                File.WriteAllText(path, "{broken replacement}", Encoding.UTF8);
                Environment.CurrentDirectory = Path.GetDirectoryName(path);
                VocabularyFile loaded = VocabularyLoader.LoadBundled();
                Assert(loaded.Entries.Count == 3674 && loaded.Entries[0].Answers.SequenceEqual(library.Entries[0].Answers), "External override used.");
            }
            finally { Environment.CurrentDirectory = previous; }
        });
        Run("variants phrases width and whitespace normalize without accepting wrong words", delegate
        {
            Assert(AnswerRules.IsCorrect(Find("jeopardise"), " Jeopardize "), "Variant rejected.");
            Assert(AnswerRules.IsCorrect(Find("carbon dioxide"), "ＣＡＲＢＯＮ  dioxide"), "Phrase normalization failed.");
            Assert(!AnswerRules.IsCorrect(Find("carbon dioxide"), "carbondioxide"), "Removed meaningful space.");
            Assert(!AnswerRules.IsCorrect(Find("up-to-date"), "uptodate"), "Removed meaningful hyphens.");
            Assert(!AnswerRules.IsCorrect(Find("breeze"), "breez"), "Wrong spelling accepted.");
        });
        Run("short prefix variants wait for Enter while full variants auto-complete", delegate
        {
            Assert(AnswerRules.IsCorrect(Find("program"), "program"), "Short answer invalid.");
            Assert(!AnswerRules.CanAutoComplete(Find("program"), "program"), "Premature transition.");
            Assert(AnswerRules.CanAutoComplete(Find("program"), "programme"), "Long answer blocked.");
            Assert(AnswerRules.CanAutoComplete(Find("breeze"), "breeze"), "Normal word blocked.");
        });
        Run("hint gives all answers without completing input or counting an attempt", delegate
        {
            Word word = Find("program");
            Assert(AnswerRules.Hint(word) == "答案：" + string.Join(" / ", word.Answers), "Hint omitted a variant.");
            using (LearningStore store = Store())
            {
                store.Start(word); LearningSession session = new LearningSession(store, library);
                session.SaveInput("pro"); session.Hint(); session.Hint();
                Round recovered = store.LoadActive();
                Assert(recovered.HintUsed && recovered.Input == "pro" && store.Sequence == 0, "Hint changed input or completed round.");
                Assert(store.Statistics().Count == 0, "Hint counted an attempt.");
                session.SaveInput("program"); session.Complete(true, DateTime.UtcNow);
                Assert(Count(store, word).Correct == 1 && Count(store, word).Errors == 0 && store.Progress()[word.Id].NeedsReview,
                    "Assisted correct answer not counted or lost its review schedule.");
            }
        });
        Run("shortcuts normalize aliases and compare exact modifiers", delegate
        {
            ShortcutGesture gesture = ShortcutRules.Parse("shift + control + alt + q", true);
            Assert(gesture.Text == "Ctrl+Alt+Shift+Q" && gesture.Matches(0x51, 7), "Modifier mismatch.");
            Assert(!gesture.Matches(0x51, 3), "Extra modifier was ignored.");
            Assert(ShortcutRules.Parse("ctrl+d8", true).Text == "Ctrl+8", "Digit not normalized.");
            Assert(ShortcutRules.Parse("F01", false).Text == "F1", "Function key not normalized.");
            Assert(ShortcutRules.Parse("Escape", false).Text == "Esc", "Escape not normalized.");
            Assert(ShortcutRules.Parse("Alt+H", false).Matches(0x48, 1), "Alt shortcut unsupported.");
        });
        Run("shortcut collisions and editor-reserved keys are rejected", delegate
        {
            ShortcutRules.Validate(new AppSettings());
            Reject(delegate { ShortcutRules.Validate(new AppSettings { HintShortcut = "F2" }); });
            Reject(delegate { ShortcutRules.Validate(new AppSettings { HintShortcut = "Ctrl+Alt+Shift+W" }); });
            foreach (string conflict in new[] { "F1", "F2", "Esc", "Ctrl+Alt+Shift+W", "Ctrl+," })
                Reject(delegate { ShortcutRules.Validate(new AppSettings { RestartGroupShortcut = conflict }); });
            foreach (string invalid in new[] { "A", "Shift+A", "Ctrl+V", "Ctrl+S", "Ctrl+,", "Alt+F4", "F12", "Ctrl+Ctrl+Q" })
                Reject(delegate { ShortcutRules.Parse(invalid, false); });
            Reject(delegate { ShortcutRules.Parse("F8", true); });
        });
        Run("custom shortcuts persist and legacy settings retain preferences with missing-field defaults", delegate
        {
            string path;
            using (LearningStore store = Store())
            {
                path = store.DatabasePath; store.Start(Find("breeze"));
                store.SaveSettings(new AppSettings { Hotkey = "Ctrl+Alt+Q", HintShortcut = "F6", SkipShortcut = "Ctrl+J", HideShortcut = "F8", RestartGroupShortcut = "Ctrl+R" });
            }
            using (LearningStore store = new LearningStore(path))
            {
                AppSettings settings = store.LoadSettings();
                Assert(settings.Hotkey == "Ctrl+Alt+Q" && settings.HintShortcut == "F6" && settings.SkipShortcut == "Ctrl+J" && settings.HideShortcut == "F8", "Shortcut configuration lost.");
                Assert(settings.RestartGroupShortcut == "Ctrl+R", "Custom restart shortcut did not survive reopening.");
                Sql(path, "UPDATE settings SET value=@p0 WHERE key='app'", "{\"Hotkey\":\"Ctrl+Alt+Q\",\"FontSize\":16,\"Width\":400,\"FluentGoal\":4}");
                settings = store.LoadSettings();
                Assert(settings.Hotkey == "Ctrl+Alt+Q" && settings.FontSize == 16 && settings.Width == 400 && settings.HintShortcut == "F1" && settings.SkipShortcut == "F2" && settings.HideShortcut == "Esc", "Old preferences not preserved.");
                Assert(settings.Backdrop == "frosted" && settings.BackdropOpacity == 0.65 && !settings.DarkAppearance, "Old settings did not receive safe appearance defaults.");
                Assert(!settings.CaretBlinkEnabled, "Legacy settings should default to a steady caret.");
                Assert(settings.RestartGroupShortcut == "F3", "Legacy settings did not receive the default restart shortcut.");
                Assert(store.LoadActive().WordId == Find("breeze").Id, "Settings affected pending input.");
            }
        });
        Run("legacy restart default avoids occupied keys and survives backup restore without changing existing shortcuts", delegate
        {
            using (LearningStore store = Store())
            {
                store.SaveSettings(new AppSettings());
                Sql(store.DatabasePath, "UPDATE settings SET value=@p0 WHERE key='app'",
                    "{\"Hotkey\":\"Ctrl+Alt+Q\",\"HintShortcut\":\"F3\",\"SkipShortcut\":\"F4\",\"HideShortcut\":\"F5\"}");
                string legacyBackup = store.DatabasePath + ".legacy"; store.Backup(legacyBackup);
                AppSettings settings = store.LoadSettings();
                Assert(settings.RestartGroupShortcut == "F6" && settings.HintShortcut == "F3" && settings.SkipShortcut == "F4" &&
                    settings.HideShortcut == "F5" && settings.Hotkey == "Ctrl+Alt+Q", "Restart default replaced an existing shortcut.");
                store.SaveSettings(new AppSettings()); store.Restore(legacyBackup); settings = store.LoadSettings();
                Assert(settings.RestartGroupShortcut == "F6" && settings.HintShortcut == "F3", "Legacy backup could not restore occupied-key defaults.");
                settings.RestartGroupShortcut = "Ctrl+R"; store.SaveSettings(settings);
                string customBackup = store.DatabasePath + ".custom"; store.Backup(customBackup);
                settings.RestartGroupShortcut = "F7"; store.SaveSettings(settings); store.Restore(customBackup);
                Assert(store.LoadSettings().RestartGroupShortcut == "Ctrl+R", "Restore lost a custom restart shortcut.");
                settings.RestartGroupShortcut = "F3"; Reject(delegate { store.SaveSettings(settings); });
                Assert(store.LoadSettings().RestartGroupShortcut == "Ctrl+R", "Conflicting restart shortcut replaced valid settings.");
            }
        });
        Run("legacy review-only settings and restored backups resume the full group without losing records", delegate
        {
            string path, backup;
            DateTime now = DateTime.UtcNow;
            using (LearningStore store = Store())
            {
                path = store.DatabasePath; backup = path + ".backup";
                AppSettings settings = new AppSettings { GroupSize = 12, GroupNumber = 4 };
                LearningSession session = new LearningSession(store, library); session.Next(settings, now);
                session.SaveInput(session.Word.Answers[0]); session.Complete(true, now); session.Next(settings, now);
                session.SaveInput("draft"); session.Error();
                settings.ReviewOnly = true; settings.ReviewDays = new[] { 2, 5, 9 };
                store.SaveSettings(settings); store.Backup(backup);
            }
            using (LearningStore store = new LearningStore(path))
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1) store.Restore(backup);
                    AppSettings settings = store.LoadSettings();
                    Assert(!settings.ReviewOnly && settings.GroupSize == 12 && settings.GroupNumber == 4,
                        "Legacy review filter is still active or the selected group changed.");
                    LearningSession session = new LearningSession(store, library); session.Next(settings, now);
                    Assert(session.Word.Id == 38 && session.Current.Input == "draft" && session.Current.Errors == 1 &&
                        session.GroupState.WordIds.Length == 12 && session.GroupState.CompletedWordIds.SequenceEqual(new[] { 37 }),
                        "Full group, progress or unfinished input changed.");
                    Assert(store.Statistics().Single(s => s.WordId == 37).Correct == 1 &&
                        store.Statistics().Single(s => s.WordId == 38).Errors == 1, "Correct or error totals changed.");
                }
            }
        });
        Run("appearance settings survive restart and reject unknown modes or invalid opacity", delegate
        {
            using (LearningStore store = Store())
            {
                store.Start(Find("breeze"));
                foreach (string mode in new[] { "frosted", "clear", "solid" })
                {
                    store.SaveSettings(new AppSettings { Backdrop = mode, BackdropOpacity = 0.8, DarkAppearance = true });
                    using (LearningStore reopened = new LearningStore(store.DatabasePath))
                    {
                        AppSettings restored = reopened.LoadSettings();
                        Assert(restored.Backdrop == mode && restored.BackdropOpacity == 0.8 && restored.DarkAppearance, "Appearance preference lost.");
                        Assert(reopened.LoadActive().WordId == Find("breeze").Id, "Appearance changed learning progress.");
                    }
                }
                Reject(delegate { store.SaveSettings(new AppSettings { Backdrop = "unknown" }); });
                foreach (double value in new[] { double.NaN, double.PositiveInfinity, -1.0, 1.01 })
                    Reject(delegate { store.SaveSettings(new AppSettings { BackdropOpacity = value }); });
                Assert(store.LoadSettings().Backdrop == "solid", "Invalid preferences replaced valid settings.");
            }
        });
        Run("caret blinking defaults off and survives settings reload backup and restore", delegate
        {
            using (LearningStore store = Store())
            {
                AppSettings settings = store.LoadSettings();
                Assert(!settings.CaretBlinkEnabled, "New settings enable blinking by default.");
                store.Start(Find("breeze")); LearningSession session = new LearningSession(store, library);
                session.SaveInput("breez"); session.Error();
                settings.CaretBlinkEnabled = true; store.SaveSettings(settings);
                string backup = store.DatabasePath + ".backup"; store.Backup(backup);
                using (LearningStore reopened = new LearningStore(store.DatabasePath))
                    Assert(reopened.LoadSettings().CaretBlinkEnabled, "Blinking preference did not survive reopening.");
                settings.CaretBlinkEnabled = false; store.SaveSettings(settings);
                Assert(!store.LoadSettings().CaretBlinkEnabled, "Cannot turn blinking back off.");
                store.Restore(backup);
                Assert(store.LoadSettings().CaretBlinkEnabled && store.LoadActive().Input == "breez" &&
                    store.Statistics().Single().Errors == 1, "Restore lost the caret preference or changed learning data.");
            }
        });
        Run("successful completion is atomic and idempotent with no completed history row", delegate
        {
            using (LearningStore store = Store())
            {
                Assert(store.SqliteVersion == "3.53.4", "Unexpected SQLite version.");
                Round round = store.Start(Find("breeze")); round.Input = "breeze";
                Assert(store.Finish(round, true, DateTime.UtcNow), "Completion failed.");
                Assert(!store.Finish(round, true, DateTime.UtcNow), "Completed twice.");
                WordStatistics count = Count(store, Find("breeze"));
                Assert(count.Correct == 1 && count.Errors == 0 && count.Accuracy == 1 && store.Sequence == 1, "Incorrect totals.");
                Assert(store.LoadActive() == null && Number(store.DatabasePath, "SELECT count(*) FROM pending") == 0, "Finished history retained.");
            }
        });
        Run("wrong then correct counts one of each and yields 50 percent", delegate
        {
            using (LearningStore store = Store())
            {
                Word word = Find("breeze"); store.Start(word); LearningSession session = new LearningSession(store, library);
                session.SaveInput("breez"); session.Error();
                Assert(Count(store, word).Errors == 1 && Count(store, word).Correct == 0, "Unfinished error not counted immediately.");
                Assert(session.Current.HintUsed && store.LoadActive().HintUsed && store.LoadActive().Input == "breez",
                    "Error submission did not preserve its input and automatic hint.");
                session.SaveInput("breeze"); Assert(session.Complete(true, DateTime.UtcNow), "Retype failed.");
                WordStatistics count = Count(store, word);
                Assert(count.Correct == 1 && count.Errors == 1 && count.Accuracy == 0.5, "Wrong retry ratio.");
            }
        });
        Run("multiple explicit errors accumulate but draft edits and paste do not count", delegate
        {
            using (LearningStore store = Store())
            {
                Word word = Find("breeze"); store.Start(word); LearningSession session = new LearningSession(store, library);
                session.SaveInput("b"); session.SaveInput("br"); session.Paste(); session.Hint();
                Assert(store.Statistics().Count == 0, "Draft or assistance counted.");
                session.Error(); session.Error(); session.SaveInput("breeze"); session.Complete(true, DateTime.UtcNow);
                WordStatistics count = Count(store, word);
                Assert(count.Correct == 1 && count.Errors == 2 && Math.Abs(count.Accuracy.Value - 1.0 / 3) < 0.000001, "Submissions lost.");
            }
        });
        Run("skip and unsubmitted wrong text do not affect counters", delegate
        {
            using (LearningStore store = Store())
            {
                store.Start(Find("breeze")); LearningSession session = new LearningSession(store, library);
                session.SaveInput("wrong");
                Assert(!session.Complete(true, DateTime.UtcNow), "Wrong answer accepted.");
                Assert(session.Complete(false, DateTime.UtcNow), "Skip failed.");
                Assert(store.Statistics().Count == 0 && store.LoadActive() == null, "Skip counted as answer.");
                Assert(new WordStatistics().Accuracy == null, "Unanswered accuracy should be empty.");
            }
        });
        Run("errors hint paste and unfinished input survive restart without duplicate counts", delegate
        {
            string path;
            using (LearningStore store = Store())
            {
                path = store.DatabasePath; store.Start(Find("breeze")); LearningSession session = new LearningSession(store, library);
                session.SaveInput("breez"); session.Error(); session.Hint(); session.Paste();
            }
            using (LearningStore store = new LearningStore(path))
            {
                LearningSession session = new LearningSession(store, library);
                Assert(session.Current.Errors == 1 && session.Current.HintUsed && session.Current.Pasted && session.Current.Input == "breez", "Pending state lost.");
                Assert(Count(store, Find("breeze")).Errors == 1, "Restart duplicated error.");
                session.SaveInput("breeze"); session.Complete(true, DateTime.UtcNow);
                Assert(Count(store, Find("breeze")).Correct == 1 && Count(store, Find("breeze")).Errors == 1, "Completion recounted errors.");
            }
        });
        Run("retrying the same committed error candidate is idempotent", delegate
        {
            using (LearningStore store = Store())
            {
                Round candidate = store.Start(Find("breeze")); candidate.Input = "br"; candidate.Errors = 1; candidate.HintUsed = true;
                store.RecordError(candidate); store.RecordError(candidate);
                Assert(Count(store, Find("breeze")).Errors == 1 && store.LoadActive().Errors == 1 && store.LoadActive().HintUsed,
                    "Duplicate error incremented or automatic hint lost.");
            }
        });
        Run("failed error transaction rolls back pending input and counters", delegate
        {
            using (LearningStore store = Store())
            {
                Round round = store.Start(Find("breeze")); round.Input = "b"; store.Save(round);
                Round invalid = round.Copy(); invalid.Input = new string('x', 81); invalid.Errors = 1;
                Reject(delegate { store.RecordError(invalid); });
                Assert(store.LoadActive().Input == "b" && store.LoadActive().Errors == 0 && store.Statistics().Count == 0, "Failed transaction leaked.");
                invalid = round.Copy(); invalid.Errors = 2;
                Reject(delegate { store.RecordError(invalid); });
                Assert(store.Statistics().Count == 0, "Skipped error number accepted.");
            }
        });
        Run("failed error counter update rolls back the automatic hint and pending error together", delegate
        {
            using (LearningStore store = Store())
            {
                Word word = Find("breeze"); store.Start(word); LearningSession session = new LearningSession(store, library);
                session.SaveInput("breez");
                Sql(store.DatabasePath, "CREATE TRIGGER fail_error BEFORE INSERT ON word_counts BEGIN SELECT RAISE(ABORT,'counter write failed'); END");
                try
                {
                    Reject(delegate { session.Error(); });
                    Assert(!session.Current.HintUsed && session.Current.Errors == 0 && !store.LoadActive().HintUsed &&
                        store.LoadActive().Errors == 0 && store.LoadActive().Input == "breez" && store.Statistics().Count == 0,
                        "Failed submission left a hint, error count or changed input behind.");
                }
                finally { Sql(store.DatabasePath, "DROP TRIGGER fail_error"); }
                session.Error();
                Assert(store.LoadActive().HintUsed && store.LoadActive().Errors == 1 && Count(store, word).Errors == 1,
                    "Retry failed to save the hint and error exactly once.");
            }
        });
        Run("chapter switching preserves hinted input and cumulative counters", delegate
        {
            using (LearningStore store = Store())
            {
                Word word = Find("breeze"); store.Start(word); LearningSession session = new LearningSession(store, library);
                session.SaveInput("br"); session.Error(); session.Hint();
                string other = library.Entries.Select(w => w.Chapter).Distinct().First(c => c != word.Chapter);
                session.SwitchScope(new AppSettings { Chapter = other }, DateTime.UtcNow);
                Assert(session.Word.Chapter == other, "Chapter not applied.");
                session.SwitchScope(new AppSettings { Chapter = word.Chapter,
                    GroupNumber = PracticeGroups.Create(library, word.Chapter, 20).Single(g => g.Words.Any(w => w.Id == word.Id)).Number }, DateTime.UtcNow);
                Assert(session.Word.Id == word.Id && session.Current.HintUsed && session.Current.Input == "br", "Suspended work lost.");
                Assert(Count(store, word).Errors == 1 && store.Sequence == 0, "Scope change counted an attempt.");
            }
        });
        Run("review-only suspends new input and normal practice resumes it", delegate
        {
            using (LearningStore store = Store())
            {
                store.Start(Find("breeze")); LearningSession session = new LearningSession(store, library);
                session.SaveInput("br"); session.Hint();
                AppSettings grouped = store.LoadSettings(); grouped.ReviewOnly = true;
                session.SwitchScope(grouped, DateTime.UtcNow);
                Assert(session.Current == null, "New word entered review-only.");
                grouped.ReviewOnly = false; session.SwitchScope(grouped, DateTime.UtcNow);
                Assert(session.Word.Id == Find("breeze").Id && session.Current.Input == "br" && session.Current.HintUsed, "Input not resumed.");
            }
        });
        Run("review intervals remain configurable without changing cumulative statistics", delegate
        {
            using (LearningStore store = Store())
            {
                DateTime now = DateTime.UtcNow; Word word = Find("breeze");
                store.SaveSettings(new AppSettings { ReviewDays = new[] { 2, 5, 9 } });
                Round first = store.Start(word); first.Input = "breeze"; store.Finish(first, true, now);
                Assert(store.Progress()[word.Id].DueUtc == now.AddDays(2), "First interval ignored.");
                Round second = store.Start(word); second.Input = "breeze"; store.Finish(second, true, now.AddDays(3));
                Assert(store.Progress()[word.Id].DueUtc == now.AddDays(8) && Count(store, word).Correct == 2, "Interval or count changed.");
                Reject(delegate { store.SaveSettings(new AppSettings { ReviewDays = new[] { 7, 1 } }); });
                Assert(store.LoadSettings().ReviewDays.SequenceEqual(new[] { 2, 5, 9 }), "Invalid settings persisted.");
            }
        });
        Run("v1 migration aggregates across dates once and preserves backup pending settings and review", delegate
        {
            string path = LegacyFixture();
            using (LearningStore store = new LearningStore(path))
            {
                AssertMigrated(store);
                Assert(File.Exists(store.UpgradeBackupPath) && store.ValidateBackup(store.UpgradeBackupPath) == 1, "Pre-upgrade backup missing.");
                Assert(Number(store.UpgradeBackupPath, "SELECT count(*) FROM rounds") == 6, "Backup was converted or lost rounds.");
                Assert(store.LoadSettings().Hotkey == "Ctrl+Alt+Q" && store.LoadSettings().FontSize == 16, "Preferences lost.");
                Assert(store.LoadSettings().HintShortcut == "F1", "Legacy shortcut defaults missing.");
                Assert(store.Progress()[Find("carbon dioxide").Id].Streak == 1, "Review state lost.");
                Assert(Number(path, "SELECT count(*) FROM sqlite_master WHERE name IN ('rounds','events','progress','activity')") == 0, "Dated history retained.");
                Assert(Number(path, "SELECT count(*) FROM pragma_table_info('word_counts')") == 3, "Unexpected statistics fields.");
                Assert(Number(path, "SELECT count(*) FROM pragma_table_info('pending') WHERE name IN ('started_utc','ended_utc','local_day','timezone_id','active_ms')") == 0, "Pending keeps learning dates.");
            }
            using (LearningStore store = new LearningStore(path))
            {
                AssertMigrated(store); Assert(store.UpgradeBackupPath == null, "Migration repeated.");
                LearningSession session = new LearningSession(store, library);
                session.SaveInput("breeze"); session.Complete(true, DateTime.UtcNow);
                Assert(Count(store, Find("breeze")).Correct == 3 && Count(store, Find("breeze")).Errors == 5, "Pending errors duplicated on completion.");
            }
        });
        Run("failed legacy migration rolls back schema and retains the original history", delegate
        {
            string path = LegacyFixture(); Sql(path, "CREATE TABLE word_counts(dummy TEXT)");
            Reject(delegate { using (LearningStore store = new LearningStore(path)) { } });
            Assert(Number(path, "PRAGMA user_version") == 1 && Number(path, "SELECT count(*) FROM rounds") == 6, "Failed upgrade mutated history.");
            Assert(Number(path, "SELECT count(*) FROM sqlite_master WHERE name IN ('pending','review_state','meta')") == 0, "Partial schema committed.");
        });
        Run("legacy unknown vocabulary IDs are rejected before migration", delegate
        {
            string path = LegacyFixture(); Sql(path, "UPDATE rounds SET word_id=999999 WHERE id='b1'");
            Reject(delegate { using (LearningStore store = new LearningStore(path)) { } });
            Assert(Number(path, "PRAGMA user_version") == 1 && Number(path, "SELECT count(*) FROM rounds") == 6, "Invalid old book damaged original.");
        });
        Run("v2 backup restores counts input and preferences while retaining a recovery copy", delegate
        {
            using (LearningStore store = Store())
            {
                Word word = Find("breeze"); store.Start(word); LearningSession session = new LearningSession(store, library);
                session.SaveInput("br"); session.Error();
                string backup = NewPath("backup.sqlite"); store.Backup(backup);
                session.SaveInput("breeze"); session.Complete(true, DateTime.UtcNow);
                string safety = store.Restore(backup);
                Assert(File.Exists(safety) && store.Sequence == 0 && store.LoadActive().Input == "br", "Backup round trip failed.");
                Assert(Count(store, word).Correct == 0 && Count(store, word).Errors == 1, "Restored counts differ.");
                using (LearningStore previous = new LearningStore(safety)) Assert(Count(previous, word).Correct == 1, "Recovery copy lost previous counts.");
            }
        });
        Run("restoring a v1 backup migrates its staged copy without changing the source", delegate
        {
            using (LearningStore store = Store())
            {
                Round round = store.Start(Find("breeze")); round.Input = "breeze"; store.Finish(round, true, DateTime.UtcNow);
                string source = LegacyFixture(); byte[] original = File.ReadAllBytes(source);
                string safety = store.Restore(source); AssertMigrated(store);
                Assert(Number(source, "PRAGMA user_version") == 1 && File.ReadAllBytes(source).SequenceEqual(original), "Source backup modified.");
                using (LearningStore previous = new LearningStore(safety)) Assert(Count(previous, Find("breeze")).Correct == 1, "Prior progress lost.");
            }
        });
        Run("invalid backup rows schemas and triggers cannot replace the current database", delegate
        {
            using (LearningStore store = Store())
            {
                Word word = Find("breeze"); store.Start(word); LearningSession session = new LearningSession(store, library);
                session.SaveInput("br"); session.Error();
                string[] corruptions = {
                    "PRAGMA ignore_check_constraints=ON; UPDATE word_counts SET word_id=999999",
                    "PRAGMA ignore_check_constraints=ON; UPDATE word_counts SET error_count=-1",
                    "DROP TABLE meta",
                    "CREATE TRIGGER unexpected AFTER UPDATE ON pending BEGIN SELECT 1; END"
                };
                foreach (string corruption in corruptions)
                {
                    string path = NewPath("bad.sqlite"); store.Backup(path); Sql(path, corruption);
                    Reject(delegate { store.Restore(path); });
                    Assert(store.LoadActive().Input == "br" && Count(store, word).Errors == 1, "Invalid restore replaced data.");
                }
                string foreign = NewPath("foreign.sqlite"); File.WriteAllText(foreign, "not a database");
                Reject(delegate { store.Restore(foreign); });
                foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
                    Reject(delegate { store.Backup(store.DatabasePath + suffix); });
            }
        });
        Run("abrupt termination keeps committed input and counts and rolls back a pending write", delegate
        {
            string path = NewPath("learning.sqlite"), marker = path + ".ready";
            string arguments = string.Join(" ", new[] { "--crash-writer", path, marker }.Select(a => "\"" + a + "\""));
            using (Process child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, arguments)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
            {
                try
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    while (!File.Exists(marker) && !child.HasExited && clock.ElapsedMilliseconds < 10000) Thread.Sleep(25);
                    Assert(File.Exists(marker), "Crash worker did not reach checkpoint.");
                    child.Kill(); Assert(child.WaitForExit(5000), "Crash worker did not exit.");
                }
                finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); } }
            }
            using (LearningStore store = new LearningStore(path))
            {
                Round round = store.LoadActive();
                Assert(round.Input == "b" && round.Errors == 1 && round.HintUsed, "Committed state lost or uncommitted write persisted.");
                Assert(store.Sequence == 0 && Count(store, Find("breeze")).Errors == 1, "Crash inflated counts.");
            }
        });
        GroupPracticeTests.RunAll(Run, output, library);
        File.WriteAllText(Path.Combine(output, "test-results.json"), new JavaScriptSerializer().Serialize(new {
            utc = DateTime.UtcNow.ToString("o"), passed = results.Count - failed, failed = failed, tests = results }), Encoding.UTF8);
        Console.WriteLine("Result: " + (results.Count - failed) + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void AssertMigrated(LearningStore store)
    {
        WordStatistics a = Count(store, Find("breeze")), b = Count(store, Find("program")), c = Count(store, Find("carbon dioxide"));
        Assert(a.Correct == 2 && a.Errors == 5 && Math.Abs(a.Accuracy.Value - 2.0 / 7) < 0.000001, "Legacy totals not aggregated once.");
        Assert(b.Correct == 0 && b.Errors == 1 && c.Correct == 1 && c.Errors == 0 && store.Statistics().Count == 3, "Other word counts lost.");
        Assert(store.Sequence == 4 && store.LoadActive().Input == "br" && store.LoadActive().Errors == 2 &&
            store.LoadActive().HintUsed && store.LoadActive().Pasted, "Active legacy input lost.");
        Assert(store.Suspended().Count == 1 && store.Suspended()[0].Input == "pro" && store.Suspended()[0].HintUsed, "Suspended legacy input lost.");
    }

    private static string LegacyFixture()
    {
        string path = NewPath("legacy.sqlite");
        Sql(path, @"CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE rounds(id TEXT PRIMARY KEY,word_id INTEGER NOT NULL,started_utc TEXT NOT NULL,ended_utc TEXT,
 local_day TEXT NOT NULL,timezone_id TEXT NOT NULL,outcome TEXT NOT NULL CHECK(outcome IN ('active','suspended','correct','skip')),
 errors INTEGER NOT NULL DEFAULT 0 CHECK(errors>=0),hint_level INTEGER NOT NULL DEFAULT 0 CHECK(hint_level>=0),
 revealed INTEGER NOT NULL DEFAULT 0,pasted INTEGER NOT NULL DEFAULT 0,is_new INTEGER NOT NULL,
 active_ms INTEGER NOT NULL DEFAULT 0 CHECK(active_ms>=0),input TEXT NOT NULL DEFAULT '');
CREATE UNIQUE INDEX one_active_round ON rounds(outcome) WHERE outcome='active';
CREATE INDEX rounds_day ON rounds(local_day,outcome);
CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT,round_id TEXT NOT NULL REFERENCES rounds(id),
 kind TEXT NOT NULL,utc TEXT NOT NULL,local_day TEXT NOT NULL,timezone_id TEXT NOT NULL,input TEXT NOT NULL);
CREATE INDEX events_day ON events(local_day,kind);
CREATE TABLE progress(word_id INTEGER PRIMARY KEY,completed INTEGER NOT NULL,correct INTEGER NOT NULL,errors INTEGER NOT NULL,
 streak INTEGER NOT NULL,due_utc TEXT NOT NULL,due_after INTEGER NOT NULL,needs_review INTEGER NOT NULL);
CREATE TABLE activity(local_day TEXT PRIMARY KEY,active_ms INTEGER NOT NULL CHECK(active_ms>=0));
INSERT INTO activity VALUES('2026-09-27',5000),('2026-09-28',6000);
INSERT INTO settings VALUES('app','{""Hotkey"":""Ctrl+Alt+Q"",""FontSize"":16,""Width"":400,""FluentGoal"":4}');
PRAGMA user_version=1; PRAGMA application_id=1447252557;");
        LegacyRound(path, "a1", Find("breeze"), "correct", 2, 1, 0, 0, "breeze", 27);
        LegacyRound(path, "a2", Find("breeze"), "correct", 0, 0, 0, 0, "breeze", 28);
        LegacyRound(path, "a3", Find("breeze"), "skip", 1, 0, 0, 0, "b", 29);
        LegacyRound(path, "a4", Find("breeze"), "active", 2, 0, 1, 1, "br", 30);
        LegacyRound(path, "b1", Find("program"), "suspended", 1, 1, 0, 0, "pro", 30);
        LegacyRound(path, "c1", Find("carbon dioxide"), "correct", 0, 0, 0, 0, "carbon dioxide", 28);
        Sql(path, "INSERT INTO progress VALUES(@p0,3,2,3,0,'2026-10-01T00:00:00Z',8,1)", Find("breeze").Id);
        Sql(path, "INSERT INTO progress VALUES(@p0,1,1,0,1,'2026-10-02T00:00:00Z',0,0)", Find("carbon dioxide").Id);
        return path;
    }

    private static void LegacyRound(string path, string id, Word word, string outcome, int errors, int hint,
        int revealed, int pasted, string input, int day)
    {
        string date = "2026-09-" + day, utc = date + "T00:00:00Z";
        Sql(path, @"INSERT INTO rounds(id,word_id,started_utc,ended_utc,local_day,timezone_id,outcome,errors,hint_level,revealed,pasted,is_new,active_ms,input)
VALUES(@p0,@p1,@p2,NULL,@p3,'China Standard Time',@p4,@p5,@p6,@p7,@p8,0,1000,@p9)",
            id, word.Id, utc, date, outcome, errors, hint, revealed, pasted, input);
        for (int i = 0; i < errors; i++)
            Sql(path, "INSERT INTO events(round_id,kind,utc,local_day,timezone_id,input) VALUES(@p0,'error',@p1,@p2,'China Standard Time','wrong')", id, utc, date);
    }

    private static int CrashWriter(string[] args)
    {
        VocabularyFile data = VocabularyLoader.LoadBundled();
        using (LearningStore store = new LearningStore(args[1]))
        {
            store.Start(data.Entries.First(w => w.Answers.Contains("breeze")));
            LearningSession session = new LearningSession(store, data); session.SaveInput("b"); session.Error(); session.Hint();
            using (SQLiteConnection writer = Connect(args[1]))
            using (SQLiteTransaction transaction = writer.BeginTransaction())
            using (SQLiteCommand command = new SQLiteCommand("UPDATE pending SET input='uncommitted'; UPDATE word_counts SET error_count=9999", writer, transaction))
            {
                command.ExecuteNonQuery(); File.WriteAllText(args[2], "committed checkpoint + pending transaction");
                Thread.Sleep(Timeout.Infinite);
            }
        }
        return 0;
    }
}
