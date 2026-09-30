using System;
using System.IO;
using System.Linq;
using Vcrmb.Core;

// 安装回归使用真实独立 SQLite 记录；不访问正式学习目录。
internal static class ProfileProbe
{
    private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(args[1]);
            using (LearningStore store = new LearningStore(Path.Combine(args[1], "learning.sqlite")))
            {
                if (args[0] == "seed")
                {
                    AppSettings settings = new AppSettings { GroupSize = 12, GroupNumber = 4, Topmost = false,
                        Hotkey = "Ctrl+Alt+Shift+Y", Backdrop = "solid" };
                    store.SaveSettings(settings);
                    LearningSession session = new LearningSession(store, VocabularyLoader.LoadBundled());
                    session.Next(settings, DateTime.UtcNow);
                    session.Error(); session.SaveInput(session.Word.Answers[0]);
                    if (!session.Complete(true, DateTime.UtcNow)) throw new Exception("Seed completion failed.");
                    session.Next(settings, DateTime.UtcNow); session.SaveInput("draft");
                    store.Backup(Path.Combine(args[1], "user-backup.sqlite"));
                }
                else if (args[0] != "verify") throw new Exception("Unsupported probe operation.");
                AppSettings actual = store.LoadSettings();
                WordStatistics count = store.Statistics().Single(w => w.WordId == 37);
                if (actual.GroupSize != 12 || actual.GroupNumber != 4 || count.Correct != 1 || count.Errors != 1 ||
                    store.LoadActive().WordId != 38 || store.LoadActive().Input != "draft" ||
                    store.LoadPracticeGroup(actual).CompletedWordIds.Length != 1 ||
                    !File.Exists(Path.Combine(args[1], "user-backup.sqlite")))
                    throw new Exception("Settings, counts, group progress, draft or backup changed.");
            }
            Console.WriteLine("PASS settings/counts/group/draft/backup");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
