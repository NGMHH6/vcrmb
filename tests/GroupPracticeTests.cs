using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Vcrmb.Core;

internal static class GroupPracticeTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        bool rejected = false; try { action(); } catch (Exception) { rejected = true; }
        Assert(rejected, "Expected invalid action to fail.");
    }
    private static object Sql(string path, string sql, params object[] args)
    {
        using (SQLiteConnection connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open(); using (SQLiteCommand command = new SQLiteCommand(sql, connection))
            {
                for (int i = 0; i < args.Length; i++) command.Parameters.AddWithValue("@p" + i, args[i]);
                return command.ExecuteScalar();
            }
        }
    }
    private static string NewPath(string root)
    {
        string path = Path.Combine(root, "group-test-data", Guid.NewGuid().ToString("N"), "learning.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)); return path;
    }
    private static void Complete(LearningSession session, DateTime now, bool correct)
    {
        if (correct) session.SaveInput(session.Word.Answers[0]);
        Assert(session.Complete(correct, now), "Completion was rejected.");
    }

    internal static void RunAll(Action<string, Action> run, string root, VocabularyFile library)
    {
        run("groups partition fixed chapter order including a short last group and reject invalid ranges", delegate
        {
            var all = PracticeGroups.Create(library, "", 20);
            Assert(all.Count == 184 && all[0].Words.Select(w => w.Id).SequenceEqual(Enumerable.Range(1, 20)), "First group changed order.");
            Assert(all[1].Words.Select(w => w.Id).SequenceEqual(Enumerable.Range(21, 20)) && all.Last().Words.Count == 14, "Group boundary incorrect.");
            string chapter = library.Entries[241].Chapter;
            var scoped = PracticeGroups.Create(library, chapter, 20);
            Assert(scoped.Count == 7 && scoped[0].Words[0].Id == 242 && scoped.Last().Words.Count == 10, "Chapter partition leaked.");
            Assert(PracticeGroups.Create(library, "", 3674).Single().Words.Count == 3674, "Whole book group rejected.");
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                foreach (int invalid in new[] { 0, -1, 3675 }) Reject(delegate { store.SaveSettings(new AppSettings { GroupSize = invalid }); });
                foreach (int invalid in new[] { 0, -1, 185 }) Reject(delegate { store.SaveSettings(new AppSettings { GroupNumber = invalid }); });
                Reject(delegate { store.SaveSettings(new AppSettings { Chapter = "unknown" }); });
            }
        });
        run("one group practices each word once and stops despite errors hints skips or due reviews", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                AppSettings settings = new AppSettings { GroupSize = 8, GroupNumber = 2 };
                LearningSession session = new LearningSession(store, library); DateTime now = DateTime.UtcNow;
                for (int id = 9; id <= 16; id++)
                {
                    session.Next(settings, now.AddDays(id)); Assert(session.Word.Id == id, "Order escaped selected group or repeated a word.");
                    if (id == 9)
                    {
                        session.SaveInput("wrong"); session.Error(); session.Hint();
                        Assert(session.GroupState.CompletedWordIds.Length == 0, "Error or hint consumed a place.");
                    }
                    Complete(session, now.AddDays(id), id != 10);
                    Assert(!session.Complete(true, now), "Same answer completed twice.");
                }
                session.Next(settings, now.AddMonths(1));
                Assert(session.Current == null && session.GroupState.CompletedWordIds.Length == 8, "Group did not stop.");
                Assert(store.Statistics().Sum(s => s.Correct) == 7 && store.Statistics().Sum(s => s.Errors) == 1, "Grouped statistics changed semantics.");
                settings = session.NextGroup(settings, now);
                Assert(settings.GroupNumber == 3 && store.LoadSettings().GroupNumber == 3 && session.Word.Id == 17, "Next group did not persist.");
            }
        });
        run("group jump resumes independent progress drafts hints and errors and restart resets only the chosen group", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                DateTime now = DateTime.UtcNow; AppSettings first = new AppSettings { GroupSize = 2 };
                LearningSession session = new LearningSession(store, library); session.Next(first, now);
                Complete(session, now, true); session.Next(first, now); session.SaveInput("hydro"); session.Error(); session.Hint();
                AppSettings second = first.Copy(); second.GroupNumber = 2; session.SwitchScope(second, now);
                Assert(session.Word.Id == 3, "Jump did not select group two.");
                Complete(session, now, false); session.Next(second, now); session.SaveInput("draft four");
                session.SwitchScope(first, now);
                Assert(session.Word.Id == 2 && session.Current.Input == "hydro" && session.Current.HintUsed && session.Current.Errors == 1,
                    "Return lost unfinished input or assistance.");
                Assert(session.GroupState.CompletedWordIds.SequenceEqual(new[] { 1 }), "Group completion position lost.");
                session.SwitchScope(first, now, true);
                Assert(session.Word.Id == 1 && session.Current.Input == "" && !session.Current.HintUsed && session.GroupState.CompletedWordIds.Length == 0, "Restart did not begin from first word.");
                Assert(store.Statistics().Single(s => s.WordId == 1).Correct == 1 && store.Statistics().Single(s => s.WordId == 2).Errors == 1, "Restart erased totals.");
                session.SwitchScope(second, now);
                Assert(session.Word.Id == 4 && session.Current.Input == "draft four" && session.GroupState.CompletedWordIds.SequenceEqual(new[] { 3 }), "Restart erased another group.");
            }
        });
        run("partial group and selected scope survive process restart and backup restore", delegate
        {
            string path = NewPath(root), backup = path + ".backup"; DateTime now = DateTime.UtcNow;
            AppSettings settings = new AppSettings { GroupSize = 3, GroupNumber = 4 };
            using (LearningStore store = new LearningStore(path))
            {
                LearningSession session = new LearningSession(store, library); session.Next(settings, now);
                Complete(session, now, true); session.Next(settings, now); session.SaveInput("unfinished"); session.Hint(); store.Backup(backup);
                Complete(session, now, false);
            }
            using (LearningStore store = new LearningStore(path))
            {
                LearningSession session = new LearningSession(store, library); settings = store.LoadSettings(); session.Next(settings, now);
                Assert(settings.GroupSize == 3 && settings.GroupNumber == 4 && session.Word.Id == 12 && session.GroupState.CompletedWordIds.Length == 2, "Restart repeated a finished word.");
                store.Restore(backup); session = new LearningSession(store, library); session.Next(store.LoadSettings(), now);
                Assert(session.Word.Id == 11 && session.Current.Input == "unfinished" && session.Current.HintUsed && session.GroupState.CompletedWordIds.SequenceEqual(new[] { 10 }), "Backup lost group state.");
            }
        });
        run("changing group size creates the new boundaries and returning restores the previous partition", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                DateTime now = DateTime.UtcNow; AppSettings before = new AppSettings { GroupSize = 2, GroupNumber = 3 };
                LearningSession session = new LearningSession(store, library); session.Next(before, now); Complete(session, now, true);
                session.Next(before, now); session.SaveInput("word six");
                AppSettings after = new AppSettings { GroupSize = 3, GroupNumber = 3 }; session.SwitchScope(after, now);
                Assert(session.Word.Id == 7 && session.GroupState.WordIds.SequenceEqual(new[] { 7, 8, 9 }), "Size change reused old boundaries.");
                session.SwitchScope(before, now);
                Assert(session.Word.Id == 6 && session.Current.Input == "word six" && session.GroupState.CompletedWordIds.SequenceEqual(new[] { 5 }), "Previous partition not restored.");
            }
        });
        run("last group stops at the final word and next group wraps only on explicit continuation", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                DateTime now = DateTime.UtcNow; AppSettings settings = new AppSettings { GroupSize = 1, GroupNumber = 3674 };
                LearningSession session = new LearningSession(store, library); session.Next(settings, now);
                Assert(session.Word.Id == 3674, "Last group selected wrong word."); Complete(session, now, true); session.Next(settings, now);
                Assert(session.Current == null && store.LoadSettings().GroupNumber == 3674, "Last group wrapped automatically.");
                settings = session.NextGroup(settings, now);
                Assert(settings.GroupNumber == 1 && session.Word.Id == 1, "Explicit wrap failed.");
            }
        });
        run("review-only snapshots due words inside the selected fixed group without pulling other groups", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                DateTime now = DateTime.UtcNow;
                foreach (int id in new[] { 1, 2, 21 })
                {
                    Round round = store.Start(library.Entries[id - 1]); round.Input = library.Entries[id - 1].Answers[0];
                    store.Finish(round, id == 1, now);
                }
                AppSettings settings = new AppSettings { GroupSize = 20, ReviewOnly = true };
                LearningSession session = new LearningSession(store, library); session.Next(settings, now);
                Assert(session.Current == null && session.GroupState.WordIds.Length == 0, "Reviewed too early.");
                session.Next(settings, now.AddMinutes(11));
                Assert(session.Word.Id == 2 && session.GroupState.WordIds.SequenceEqual(new[] { 2 }), "Review escaped group or ignored due time.");
                Complete(session, now.AddMinutes(11), true); session.Next(settings, now.AddDays(3));
                Assert(session.Current == null, "Completed review group kept adding words.");
                settings = session.NextGroup(settings, now.AddMinutes(11)); Assert(session.Word.Id == 21, "Next review group not reachable.");
            }
        });
        run("continuing or restarting an empty review group never erases suspended new-word input", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                DateTime now = DateTime.UtcNow; AppSettings normal = new AppSettings { GroupSize = 3674 };
                LearningSession session = new LearningSession(store, library); session.Next(normal, now); session.SaveInput("keep draft"); session.Hint();
                AppSettings review = normal.Copy(); review.ReviewOnly = true; session.SwitchScope(review, now);
                Assert(session.Current == null, "Unexpected review word.");
                review = session.NextGroup(review, now); session.SwitchScope(review, now, true); session.SwitchScope(normal, now);
                Assert(session.Word.Id == 1 && session.Current.Input == "keep draft" && session.Current.HintUsed, "Review continuation or restart cleared a new-word draft.");
                Assert(store.Statistics().Count == 0, "Navigation counted an attempt.");
            }
        });
        run("group completion failure rolls back group position counts review and pending state atomically", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                AppSettings settings = new AppSettings { GroupSize = 2 }; DateTime now = DateTime.UtcNow;
                LearningSession session = new LearningSession(store, library); session.Next(settings, now); session.SaveInput(session.Word.Answers[0]);
                Sql(store.DatabasePath, "CREATE TRIGGER fail_group BEFORE INSERT ON settings WHEN NEW.key LIKE 'practice/%' BEGIN SELECT RAISE(ABORT,'group write failed'); END");
                try
                {
                    Reject(delegate { session.Complete(true, now); });
                    Assert(store.Statistics().Count == 0 && store.Sequence == 0 && store.Progress().Count == 0 && store.LoadActive().WordId == 1 &&
                        store.LoadPracticeGroup(settings).CompletedWordIds.Length == 0 && session.Current.Outcome == "active", "Partial group completion was committed.");
                }
                finally { Sql(store.DatabasePath, "DROP TRIGGER fail_group"); }
                Complete(session, now, true); Assert(store.Statistics().Single().Correct == 1 && session.GroupState.CompletedWordIds.Length == 1, "Retry counted twice.");
            }
        });
        run("failed group jump rolls back selected settings pending input and in-memory session", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                DateTime now = DateTime.UtcNow; AppSettings settings = new AppSettings { GroupSize = 2 };
                LearningSession session = new LearningSession(store, library); session.Next(settings, now); session.SaveInput("keep current"); session.Hint();
                AppSettings target = settings.Copy(); target.GroupNumber = 2;
                Sql(store.DatabasePath, "CREATE TRIGGER fail_switch BEFORE INSERT ON settings WHEN NEW.key LIKE 'practice/%' BEGIN SELECT RAISE(ABORT,'switch failed'); END");
                try
                {
                    Reject(delegate { session.SwitchScope(target, now, true); });
                    Assert(store.LoadSettings().GroupNumber == 1 && store.LoadActive().WordId == 1 && store.LoadActive().Input == "keep current" &&
                        session.Word.Id == 1 && session.Current.HintUsed && session.GroupState.GroupNumber == 1 && store.LoadPracticeGroup(target) == null,
                        "Failed jump left a partial scope change.");
                }
                finally { Sql(store.DatabasePath, "DROP TRIGGER fail_switch"); }
                session.SwitchScope(target, now); Assert(session.Word.Id == 3, "Jump retry failed.");
            }
        });
        run("legacy settings select the unfinished word group and corrupt group backups cannot replace data", delegate
        {
            using (LearningStore store = new LearningStore(NewPath(root)))
            {
                Word word = library.Entries[32]; Round pending = store.Start(word); pending.Input = "br"; store.Save(pending);
                Sql(store.DatabasePath, "INSERT OR REPLACE INTO settings(key,value) VALUES('app',@p0)", "{\"Hotkey\":\"Ctrl+Alt+Q\",\"FontSize\":16}");
                AppSettings settings = store.LoadSettings();
                Assert(settings.GroupNumber == 2 && settings.GroupSize == 20, "Legacy current word moved to first group.");
                LearningSession session = new LearningSession(store, library); session.Next(settings, DateTime.UtcNow);
                Assert(session.Word.Id == 33 && session.Current.Input == "br", "Legacy draft lost.");
                string backup = store.DatabasePath + ".backup"; store.Backup(backup);
                Sql(backup, "UPDATE settings SET value=@p0 WHERE key LIKE 'practice/%'",
                    "{\"Chapter\":\"\",\"GroupSize\":20,\"GroupNumber\":2,\"ReviewOnly\":false,\"WordIds\":[9999],\"CompletedWordIds\":[]}");
                Reject(delegate { store.Restore(backup); });
                Assert(store.LoadActive().WordId == 33 && store.LoadActive().Input == "br" && store.LoadPracticeGroup(settings).WordIds.Length == 20, "Invalid backup damaged live progress.");
            }
        });
    }
}
