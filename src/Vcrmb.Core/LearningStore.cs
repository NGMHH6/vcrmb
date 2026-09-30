using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Vcrmb.Core
{
    /// <summary>累计计数与当前练习分开保存；完成的题目不保留流水或日期记录。</summary>
    public sealed class LearningStore : IDisposable
    {
        private const int ApplicationId = 1447252557;
        private SQLiteConnection connection;
        private SQLiteTransaction transaction;
        public string DatabasePath { get; private set; }
        public string UpgradeBackupPath { get; private set; }

        public LearningStore(string path) : this(path, true) { }

        private LearningStore(string path, bool preserveUpgradeBackup)
        {
            DatabasePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
            Open();
            try
            {
                long version = Convert.ToInt64(Scalar("PRAGMA user_version"));
                long appId = Convert.ToInt64(Scalar("PRAGMA application_id"));
                if (version < 0 || version > 2 || (version != 0 && appId != ApplicationId) || (version == 0 && appId != 0))
                    throw new InvalidDataException("学习库版本不兼容；原文件未被覆盖。");
                if (version == 0)
                {
                    if (Convert.ToInt64(Scalar("SELECT count(*) FROM sqlite_master WHERE type='table'")) != 0)
                        throw new InvalidDataException("该文件不是小词窗的学习库。");
                    Atomic(CreateSchema);
                }
                else
                {
                    ValidateDatabase(connection);
                    if (version == 1)
                    {
                        if (preserveUpgradeBackup)
                        {
                            UpgradeBackupPath = RecoveryPath("升级前"); Backup(UpgradeBackupPath);
                        }
                        Atomic(MigrateLegacy);
                    }
                }
            }
            catch { Dispose(); throw; }
        }

        private void Open()
        {
            var builder = new SQLiteConnectionStringBuilder { DataSource = DatabasePath, Version = 3, Pooling = false, DefaultTimeout = 5 };
            connection = new SQLiteConnection(builder.ToString());
            try
            {
                connection.Open();
                Execute("PRAGMA foreign_keys=ON; PRAGMA trusted_schema=OFF; PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
            }
            catch { Dispose(); throw; }
        }

        private SQLiteCommand Command(string sql, object[] values)
        {
            SQLiteCommand command = new SQLiteCommand(sql, connection, transaction);
            for (int i = 0; i < values.Length; i++) command.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
            return command;
        }
        private int Execute(string sql, params object[] values)
        { using (SQLiteCommand command = Command(sql, values)) return command.ExecuteNonQuery(); }
        private object Scalar(string sql, params object[] values)
        { using (SQLiteCommand command = Command(sql, values)) return command.ExecuteScalar(); }
        private List<Dictionary<string, object>> Query(string sql, params object[] values)
        {
            var rows = new List<Dictionary<string, object>>();
            using (SQLiteCommand command = Command(sql, values))
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.GetValue(i);
                    rows.Add(row);
                }
            }
            return rows;
        }
        private void Atomic(Action operation)
        {
            // 切组将设置、暂停旧题、创建组进度和新题纳入同一外层事务。
            if (transaction != null) { operation(); return; }
            using (SQLiteTransaction scope = connection.BeginTransaction())
            {
                transaction = scope;
                try { operation(); scope.Commit(); }
                finally { transaction = null; }
            }
        }

        private void CreateSchema()
        {
            Execute(@"CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE word_counts(word_id INTEGER PRIMARY KEY CHECK(word_id BETWEEN 1 AND 3674),
 correct_count INTEGER NOT NULL DEFAULT 0 CHECK(correct_count>=0),error_count INTEGER NOT NULL DEFAULT 0 CHECK(error_count>=0));
CREATE TABLE pending(id TEXT PRIMARY KEY,word_id INTEGER NOT NULL UNIQUE CHECK(word_id BETWEEN 1 AND 3674),
 outcome TEXT NOT NULL CHECK(outcome IN ('active','suspended')),errors INTEGER NOT NULL DEFAULT 0 CHECK(errors>=0),
 hint_used INTEGER NOT NULL DEFAULT 0 CHECK(hint_used IN (0,1)),pasted INTEGER NOT NULL DEFAULT 0 CHECK(pasted IN (0,1)),
 input TEXT NOT NULL DEFAULT '' CHECK(length(input)<=80));
CREATE UNIQUE INDEX one_active_pending ON pending(outcome) WHERE outcome='active';
CREATE TABLE review_state(word_id INTEGER PRIMARY KEY CHECK(word_id BETWEEN 1 AND 3674),
 streak INTEGER NOT NULL CHECK(streak>=0),due_utc TEXT NOT NULL,due_after INTEGER NOT NULL CHECK(due_after>=0),
 needs_review INTEGER NOT NULL CHECK(needs_review IN (0,1)));
CREATE TABLE meta(key TEXT PRIMARY KEY,value INTEGER NOT NULL CHECK(value>=0));
INSERT INTO meta(key,value) VALUES('sequence',0);
PRAGMA user_version=2; PRAGMA application_id=1447252557;
");
        }

        private void MigrateLegacy()
        {
            CreateSchema();
            // rounds 与 events 描述同一次作答，取已保存计数的最大值而不是相加。
            // progress.errors 只包括结束的题目，需补上仍在练习/暂停中的错误。
            Execute(@"INSERT INTO word_counts(word_id,correct_count,error_count)
SELECT ids.word_id,max(coalesce(p.correct,0),coalesce(r.correct,0)),
 max(coalesce(r.errors,0),coalesce(e.errors,0),coalesce(p.errors,0)+coalesce(r.pending_errors,0))
FROM (SELECT word_id FROM rounds UNION SELECT word_id FROM progress) ids
LEFT JOIN progress p ON p.word_id=ids.word_id
LEFT JOIN (SELECT word_id,sum(CASE WHEN outcome='correct' THEN 1 ELSE 0 END) correct,sum(errors) errors,
 sum(CASE WHEN outcome IN ('active','suspended') THEN errors ELSE 0 END) pending_errors FROM rounds GROUP BY word_id) r ON r.word_id=ids.word_id
LEFT JOIN (SELECT r.word_id,count(*) errors FROM events e JOIN rounds r ON r.id=e.round_id WHERE e.kind='error' GROUP BY r.word_id) e ON e.word_id=ids.word_id;
INSERT INTO pending(id,word_id,outcome,errors,hint_used,pasted,input)
 SELECT id,word_id,outcome,errors,CASE WHEN hint_level>0 OR revealed=1 THEN 1 ELSE 0 END,pasted,input
 FROM rounds WHERE outcome IN ('active','suspended');
INSERT INTO review_state(word_id,streak,due_utc,due_after,needs_review)
 SELECT word_id,streak,due_utc,due_after,needs_review FROM progress;
UPDATE meta SET value=(SELECT count(*) FROM rounds WHERE outcome IN ('correct','skip')) WHERE key='sequence';
DROP TABLE events; DROP TABLE rounds; DROP TABLE activity; DROP TABLE progress;");
        }

        public static string Utc(DateTime value) { return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }
        private static int Number(Dictionary<string, object> row, string name) { return Convert.ToInt32(row[name]); }
        private static string Text(Dictionary<string, object> row, string name) { return Convert.ToString(row[name]); }
        private static Round ReadRound(Dictionary<string, object> row)
        {
            return new Round { Id = Text(row, "id"), WordId = Number(row, "word_id"), Outcome = Text(row, "outcome"),
                Errors = Number(row, "errors"), HintUsed = Number(row, "hint_used") != 0, Pasted = Number(row, "pasted") != 0, Input = Text(row, "input") };
        }
        private static WordProgress ReadProgress(Dictionary<string, object> row)
        {
            return new WordProgress { WordId = Number(row, "word_id"), Streak = Number(row, "streak"), DueAfter = Number(row, "due_after"),
                DueUtc = DateTime.Parse(Text(row, "due_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                NeedsReview = Number(row, "needs_review") != 0 };
        }
        public string SqliteVersion { get { return Convert.ToString(Scalar("SELECT sqlite_version()")); } }
        public int Sequence { get { return Convert.ToInt32(Scalar("SELECT value FROM meta WHERE key='sequence'")); } }
        public Round LoadActive()
        {
            var rows = Query("SELECT * FROM pending WHERE outcome='active'");
            return rows.Count == 0 ? null : ReadRound(rows[0]);
        }
        public List<Round> Suspended() { return Query("SELECT * FROM pending WHERE outcome='suspended' ORDER BY word_id").Select(ReadRound).ToList(); }
        public Dictionary<int, WordProgress> Progress() { return Query("SELECT * FROM review_state").Select(ReadProgress).ToDictionary(p => p.WordId); }

        public Round Start(Word word)
        {
            Round result = null;
            Atomic(delegate
            {
                Execute("UPDATE pending SET outcome='suspended' WHERE outcome='active'");
                var existing = Query("SELECT * FROM pending WHERE word_id=@p0", word.Id);
                if (existing.Count > 0)
                {
                    result = ReadRound(existing[0]); result.Outcome = "active";
                    Execute("UPDATE pending SET outcome='active' WHERE id=@p0", result.Id); return;
                }
                result = new Round { Id = Guid.NewGuid().ToString("N"), WordId = word.Id, Outcome = "active", Input = "" };
                Execute("INSERT INTO pending(id,word_id,outcome) VALUES(@p0,@p1,'active')", result.Id, result.WordId);
            });
            return result;
        }
        public void Suspend() { Execute("UPDATE pending SET outcome='suspended' WHERE outcome='active'"); }
        private void SaveActive(Round round)
        {
            if (Execute(@"UPDATE pending SET input=@p1,hint_used=max(hint_used,@p2),pasted=max(pasted,@p3)
WHERE id=@p0 AND outcome='active' AND errors=@p4", round.Id, round.Input ?? "", round.HintUsed ? 1 : 0,
                round.Pasted ? 1 : 0, round.Errors) != 1)
                throw new InvalidOperationException("本题状态已改变，请重新打开练习。");
        }
        public void Save(Round round) { Atomic(delegate { SaveActive(round); }); }

        /// <summary>每次明确的错误提交立即累计；输入修改、提示和跳过不调用此方法。</summary>
        public void RecordError(Round candidate)
        {
            Atomic(delegate
            {
                var rows = Query("SELECT * FROM pending WHERE id=@p0 AND outcome='active'", candidate.Id);
                if (rows.Count != 1) throw new InvalidOperationException("本题已结束。");
                Round current = ReadRound(rows[0]);
                if (current.Errors == candidate.Errors && current.Input == candidate.Input) return;
                if (candidate.Errors != current.Errors + 1 || candidate.WordId != current.WordId)
                    throw new InvalidOperationException("错误次数与当前题目不一致。");
                Execute("UPDATE pending SET errors=@p1,input=@p2 WHERE id=@p0", candidate.Id, candidate.Errors, candidate.Input ?? "");
                Execute("INSERT OR IGNORE INTO word_counts(word_id) VALUES(@p0)", candidate.WordId);
                Execute("UPDATE word_counts SET error_count=error_count+1 WHERE word_id=@p0", candidate.WordId);
            });
        }

        public bool Finish(Round round, bool correct, DateTime now) { return Finish(round, correct, now, null); }

        internal bool Finish(Round round, bool correct, DateTime now, string groupKey)
        {
            bool finished = false;
            Atomic(delegate
            {
                if (Convert.ToInt32(Scalar("SELECT count(*) FROM pending WHERE id=@p0 AND word_id=@p1 AND outcome='active'", round.Id, round.WordId)) == 0) return;
                PracticeGroupState group = groupKey == null ? null : LoadGroup(groupKey);
                if (groupKey != null && (group == null || !group.WordIds.Contains(round.WordId) || group.CompletedWordIds.Contains(round.WordId)))
                    throw new InvalidOperationException("本组进度已改变，请重新打开练习。");
                SaveActive(round);
                if (correct)
                {
                    Execute("INSERT OR IGNORE INTO word_counts(word_id) VALUES(@p0)", round.WordId);
                    Execute("UPDATE word_counts SET correct_count=correct_count+1 WHERE word_id=@p0", round.WordId);
                }
                Execute("UPDATE meta SET value=value+1 WHERE key='sequence'");
                var previous = Query("SELECT * FROM review_state WHERE word_id=@p0", round.WordId);
                WordProgress state = previous.Count == 0 ? new WordProgress { WordId = round.WordId } : ReadProgress(previous[0]);
                bool independent = correct && round.IsIndependent;
                state.Streak = independent ? state.Streak + 1 : 0; state.NeedsReview = !independent;
                state.DueUtc = independent ? now.AddDays(ReviewQueue.IntervalDays(state.Streak, LoadSettings().ReviewDays)) : now.AddMinutes(10);
                state.DueAfter = independent ? 0 : Sequence + 5;
                Execute(@"INSERT OR REPLACE INTO review_state(word_id,streak,due_utc,due_after,needs_review)
VALUES(@p0,@p1,@p2,@p3,@p4)", round.WordId, state.Streak, Utc(state.DueUtc), state.DueAfter, state.NeedsReview ? 1 : 0);
                Execute("DELETE FROM pending WHERE id=@p0", round.Id);
                if (group != null)
                {
                    // 与累计次数、复习安排及题目结束一起提交；崩溃后不会漏题或重复计次。
                    group.CompletedWordIds = group.CompletedWordIds.Concat(new[] { round.WordId }).ToArray(); SaveGroup(group);
                }
                finished = true;
            });
            return finished;
        }

        public List<WordStatistics> Statistics()
        {
            return Query("SELECT word_id,correct_count,error_count FROM word_counts WHERE correct_count>0 OR error_count>0 ORDER BY word_id")
                .Select(row => new WordStatistics { WordId = Number(row, "word_id"), Correct = Convert.ToInt64(row["correct_count"]),
                    Errors = Convert.ToInt64(row["error_count"]) }).ToList();
        }
        public AppSettings LoadSettings()
        {
            string json = Convert.ToString(Scalar("SELECT value FROM settings WHERE key='app'"));
            AppSettings settings = string.IsNullOrEmpty(json) ? new AppSettings() : new JavaScriptSerializer().Deserialize<AppSettings>(json);
            if (settings == null) throw new InvalidDataException("设置记录损坏。");
            settings.FontSize = Math.Min(18, Math.Max(12, settings.FontSize));
            if (double.IsNaN(settings.Width) || double.IsInfinity(settings.Width)) settings.Width = 360;
            settings.Width = Math.Min(560, Math.Max(320, settings.Width));
            if (string.IsNullOrWhiteSpace(settings.Hotkey)) settings.Hotkey = "Ctrl+Alt+Shift+W";
            if (string.IsNullOrWhiteSpace(settings.HintShortcut)) settings.HintShortcut = "F1";
            if (string.IsNullOrWhiteSpace(settings.SkipShortcut)) settings.SkipShortcut = "F2";
            if (string.IsNullOrWhiteSpace(settings.HideShortcut)) settings.HideShortcut = "Esc";
            if (double.IsNaN(settings.EdgeRight) || double.IsInfinity(settings.EdgeRight)) settings.EdgeRight = 12;
            if (double.IsNaN(settings.EdgeBottom) || double.IsInfinity(settings.EdgeBottom)) settings.EdgeBottom = 12;
            Dictionary<string, object> fields = string.IsNullOrEmpty(json) ? null : new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (fields == null || !fields.ContainsKey("GroupNumber"))
            {
                // 老版没有组号，升级时定位到未完成词（否则首个未学词）所在组，保留当前输入。
                List<Word> scope = PracticeGroups.Create(PracticeGroups.Bundled.Value, settings.Chapter, settings.GroupSize).SelectMany(g => g.Words).ToList();
                Round pending = LoadActive(); var progress = Progress();
                Word target = pending == null ? null : scope.FirstOrDefault(w => w.Id == pending.WordId);
                target = target ?? scope.FirstOrDefault(w => !progress.ContainsKey(w.Id)) ?? scope[0];
                settings.GroupNumber = scope.FindIndex(w => w.Id == target.Id) / settings.GroupSize + 1;
            }
            ValidateSettings(settings); return settings;
        }
        public static void ValidateSettings(AppSettings settings)
        {
            if (settings == null || settings.ReviewDays == null || settings.ReviewDays.Length == 0 || settings.ReviewDays.Length > 10 ||
                settings.ReviewDays.Any(d => d < 1 || d > 365) || !settings.ReviewDays.SequenceEqual(settings.ReviewDays.Distinct().OrderBy(d => d)))
                throw new InvalidDataException("复习间隔应为 1–365 天之间的递增整数，最多 10 段，例如 1, 3, 7, 14。");
            ShortcutRules.Parse(settings.HintShortcut, false); ShortcutRules.Parse(settings.SkipShortcut, false); ShortcutRules.Parse(settings.HideShortcut, false);
            if (!new[] { "frosted", "clear", "solid" }.Contains(settings.Backdrop) || double.IsNaN(settings.BackdropOpacity) ||
                double.IsInfinity(settings.BackdropOpacity) || settings.BackdropOpacity < 0.2 || settings.BackdropOpacity > 0.9)
                throw new InvalidDataException("请选择有效的窗口背景，磨砂浓度应在 20%–90% 之间。");
            PracticeGroups.Selected(PracticeGroups.Bundled.Value, settings);
        }
        public void SaveSettings(AppSettings settings)
        {
            ValidateSettings(settings);
            Execute("INSERT OR REPLACE INTO settings(key,value) VALUES('app',@p0)", new JavaScriptSerializer().Serialize(settings));
        }

        public PracticeGroupState LoadPracticeGroup(AppSettings settings)
        {
            return LoadGroup(PracticeGroups.Key(settings.Chapter, settings.GroupSize, settings.GroupNumber, settings.ReviewOnly));
        }

        private PracticeGroupState LoadGroup(string key)
        {
            string json = Convert.ToString(Scalar("SELECT value FROM settings WHERE key=@p0", key));
            if (string.IsNullOrEmpty(json)) return null;
            PracticeGroupState group = new JavaScriptSerializer().Deserialize<PracticeGroupState>(json);
            PracticeGroups.ValidateState(group);
            if (group.StorageKey != key) throw new InvalidDataException("分组进度与所属章节或组号不一致。");
            return group;
        }

        private void SaveGroup(PracticeGroupState group)
        {
            Execute("INSERT OR REPLACE INTO settings(key,value) VALUES(@p0,@p1)", group.StorageKey, new JavaScriptSerializer().Serialize(group));
        }

        internal void InitializePracticeGroup(AppSettings settings, PracticeGroupState group)
        {
            PracticeGroups.ValidateState(group);
            Atomic(delegate { SaveSettings(settings); SaveGroup(group); });
        }

        internal void SwitchPracticeGroup(AppSettings settings, bool restart, bool resetDrafts, DateTime now, Action activate)
        {
            ValidateSettings(settings);
            PracticeGroup group = PracticeGroups.Selected(PracticeGroups.Bundled.Value, settings);
            Atomic(delegate
            {
                SaveSettings(settings); Suspend();
                if (restart)
                {
                    Execute("DELETE FROM settings WHERE key=@p0", PracticeGroups.Key(settings.Chapter, settings.GroupSize, settings.GroupNumber, settings.ReviewOnly));
                    // 仅显式重练时重置适用草稿，不清除其他模式中尚未到期的输入。
                    if (resetDrafts)
                    {
                        var progress = Progress(); int sequence = Sequence;
                        foreach (Word word in group.Words.Where(w => !settings.ReviewOnly ||
                            (progress.ContainsKey(w.Id) && ReviewQueue.IsDue(progress[w.Id], sequence, now))))
                            Execute("DELETE FROM pending WHERE word_id=@p0", word.Id);
                    }
                }
                activate();
            });
        }

        private string RecoveryPath(string prefix)
        { return Path.Combine(Path.GetDirectoryName(DatabasePath), prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".sqlite"); }
        private bool IsLiveDatabaseFile(string path)
        { return new[] { "", "-wal", "-shm", "-journal" }.Any(suffix => string.Equals(Path.GetFullPath(path), DatabasePath + suffix, StringComparison.OrdinalIgnoreCase)); }
        public void Backup(string destination)
        {
            destination = Path.GetFullPath(destination);
            if (IsLiveDatabaseFile(destination)) throw new InvalidOperationException("备份位置不能覆盖当前学习库。");
            string temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                Execute("VACUUM INTO @p0", temporary);
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static object Inspect(SQLiteConnection candidate, string sql)
        { using (SQLiteCommand command = new SQLiteCommand(sql, candidate)) return command.ExecuteScalar(); }
        private static void RequireZero(SQLiteConnection candidate, string sql)
        { if (Convert.ToInt64(Inspect(candidate, sql)) != 0) throw new InvalidDataException("学习库包含无效或不兼容的记录；未覆盖当前数据。"); }
        private static int ValidateDatabase(SQLiteConnection candidate)
        {
            Inspect(candidate, "PRAGMA trusted_schema=OFF");
            int version = Convert.ToInt32(Inspect(candidate, "PRAGMA user_version"));
            if ((version != 1 && version != 2) || Convert.ToInt32(Inspect(candidate, "PRAGMA application_id")) != ApplicationId ||
                Convert.ToString(Inspect(candidate, "PRAGMA quick_check")) != "ok") throw new InvalidDataException("学习库完整性或版本校验失败。");
            RequireZero(candidate, "SELECT count(*) FROM sqlite_master WHERE type IN ('trigger','view') OR sql LIKE '%VIRTUAL TABLE%'");
            string[] columns = version == 1 ? new[] {
                "SELECT id,word_id,started_utc,ended_utc,local_day,timezone_id,outcome,errors,hint_level,revealed,pasted,is_new,active_ms,input FROM rounds LIMIT 0",
                "SELECT id,round_id,kind,utc,local_day,timezone_id,input FROM events LIMIT 0",
                "SELECT word_id,completed,correct,errors,streak,due_utc,due_after,needs_review FROM progress LIMIT 0",
                "SELECT local_day,active_ms FROM activity LIMIT 0" } : new[] {
                "SELECT word_id,correct_count,error_count FROM word_counts LIMIT 0",
                "SELECT id,word_id,outcome,errors,hint_used,pasted,input FROM pending LIMIT 0",
                "SELECT word_id,streak,due_utc,due_after,needs_review FROM review_state LIMIT 0",
                "SELECT key,value FROM meta LIMIT 0" };
            foreach (string sql in columns.Concat(new[] { "SELECT key,value FROM settings LIMIT 0" }))
                using (SQLiteCommand command = new SQLiteCommand(sql, candidate))
                using (SQLiteDataReader reader = command.ExecuteReader()) { }
            using (SQLiteCommand command = new SQLiteCommand("PRAGMA foreign_key_check", candidate))
            using (SQLiteDataReader reader = command.ExecuteReader())
                if (reader.Read()) throw new InvalidDataException("学习库存在不完整关联。");
            string unfinished = version == 1 ? "rounds" : "pending", review = version == 1 ? "progress" : "review_state";
            RequireZero(candidate, "SELECT count(*) FROM " + unfinished + " WHERE word_id NOT BETWEEN 1 AND 3674 OR errors<0 OR input IS NULL OR length(input)>80");
            RequireZero(candidate, "SELECT count(*) FROM " + review + " WHERE word_id NOT BETWEEN 1 AND 3674 OR streak<0 OR due_after<0");
            RequireZero(candidate, "SELECT CASE WHEN count(*)>1 THEN 1 ELSE 0 END FROM " + unfinished + " WHERE outcome='active'");
            RequireZero(candidate, "SELECT count(*) FROM (SELECT word_id FROM " + unfinished + " WHERE outcome IN ('active','suspended') GROUP BY word_id HAVING count(*)>1)");
            if (version == 1)
            {
                RequireZero(candidate, "SELECT count(*) FROM rounds WHERE outcome NOT IN ('active','suspended','correct','skip') OR hint_level<0 OR revealed NOT IN (0,1) OR pasted NOT IN (0,1)");
                RequireZero(candidate, "SELECT count(*) FROM progress WHERE correct<0 OR errors<0 OR completed<correct");
                RequireZero(candidate, "SELECT count(*) FROM events e LEFT JOIN rounds r ON r.id=e.round_id WHERE r.id IS NULL");
            }
            else
            {
                RequireZero(candidate, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('settings','word_counts','pending','review_state','meta')");
                RequireZero(candidate, "SELECT count(*) FROM word_counts WHERE word_id NOT BETWEEN 1 AND 3674 OR typeof(correct_count)<>'integer' OR typeof(error_count)<>'integer' OR correct_count<0 OR error_count<0");
                RequireZero(candidate, "SELECT count(*) FROM pending WHERE outcome NOT IN ('active','suspended') OR hint_used NOT IN (0,1) OR pasted NOT IN (0,1)");
                RequireZero(candidate, "SELECT count(*)-count(DISTINCT word_id) FROM word_counts");
                if (Convert.ToInt64(Inspect(candidate, "SELECT count(*) FROM meta WHERE key='sequence' AND typeof(value)='integer' AND value>=0")) != 1)
                    throw new InvalidDataException("复习序号无效。");
            }
            using (SQLiteCommand command = new SQLiteCommand("SELECT due_utc FROM " + review, candidate))
            using (SQLiteDataReader reader = command.ExecuteReader())
                while (reader.Read()) DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            string json = Convert.ToString(Inspect(candidate, "SELECT value FROM settings WHERE key='app'"));
            if (!string.IsNullOrEmpty(json)) ValidateSettings(new JavaScriptSerializer().Deserialize<AppSettings>(json));
            using (SQLiteCommand command = new SQLiteCommand("SELECT key,value FROM settings WHERE key LIKE 'practice/%'", candidate))
            using (SQLiteDataReader reader = command.ExecuteReader())
                while (reader.Read())
                {
                    PracticeGroupState group = new JavaScriptSerializer().Deserialize<PracticeGroupState>(reader.GetString(1));
                    PracticeGroups.ValidateState(group);
                    if (reader.GetString(0) != group.StorageKey) throw new InvalidDataException("备份中的分组进度与所属组号不一致。");
                }
            return version;
        }
        public int ValidateBackup(string source)
        {
            FileInfo file = new FileInfo(source);
            if (!file.Exists || file.Length > 128L * 1024 * 1024) throw new InvalidDataException("备份不存在或超过 128 MiB。");
            var builder = new SQLiteConnectionStringBuilder { DataSource = file.FullName, ReadOnly = true, FailIfMissing = true, Pooling = false };
            using (var candidate = new SQLiteConnection(builder.ToString())) { candidate.Open(); return ValidateDatabase(candidate); }
        }
        public string Restore(string source)
        {
            if (IsLiveDatabaseFile(source)) throw new InvalidOperationException("请选择导出的备份文件。");
            string stage = DatabasePath + ".restore-" + Guid.NewGuid().ToString("N"), safety = RecoveryPath("恢复前");
            try
            {
                File.Copy(source, stage, false);
                if (ValidateBackup(stage) == 1)
                    using (LearningStore converted = new LearningStore(stage, false)) converted.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                ValidateBackup(stage); Backup(safety);
                Execute("PRAGMA wal_checkpoint(TRUNCATE)"); Dispose();
                bool replaced = false;
                try { File.Replace(stage, DatabasePath, null); replaced = true; Open(); }
                catch
                {
                    Dispose();
                    if (replaced) { File.Copy(safety, stage, true); File.Replace(stage, DatabasePath, null); }
                    Open(); throw;
                }
                return safety;
            }
            finally { if (File.Exists(stage)) File.Delete(stage); }
        }
        public void Dispose() { if (connection != null) { connection.Dispose(); connection = null; } }
    }
}
