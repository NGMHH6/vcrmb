using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Vcrmb.Core
{
    public static class AnswerRules
    {
        public static string Normalize(string value)
        {
            return Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).Trim(), @"\s+", " ")
                .Replace('\u2019', '\'').Replace('\u2018', '\'').ToLowerInvariant();
        }

        public static bool IsCorrect(Word word, string input)
        {
            string value = Normalize(input);
            return word.Answers.Any(answer => Normalize(answer) == value);
        }

        /// <summary>短答案也是另一允许答案的前缀时，等待 Enter，避免截断长拼写。</summary>
        public static bool CanAutoComplete(Word word, string input)
        {
            string value = Normalize(input);
            return IsCorrect(word, value) && !word.Answers.Any(answer =>
                Normalize(answer).Length > value.Length && Normalize(answer).StartsWith(value, StringComparison.Ordinal));
        }

        public static string Hint(Word word)
        {
            return "答案：" + string.Join(" / ", word.Answers);
        }
    }

    public static class VocabularyLoader
    {
        public const int EntryCount = 3674;

        public static VocabularyFile LoadBundled()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            VocabularyFile file;
            // 只读取程序集内的固定词库，不读取程序目录或用户目录中的 JSON 覆盖文件。
            using (Stream resource = typeof(VocabularyLoader).Assembly.GetManifestResourceStream("Vcrmb.vocabulary.json"))
            {
                if (resource == null) throw new InvalidDataException("程序内置词库缺失，请使用完整的运行包。");
                using (StreamReader reader = new StreamReader(resource, Encoding.UTF8))
                    file = serializer.Deserialize<VocabularyFile>(reader.ReadToEnd());
            }
            if (file == null || file.SchemaVersion != 1 || file.Entries == null || file.Entries.Count == 0)
                throw new InvalidDataException("不支持的词库格式。");
            HashSet<int> ids = new HashSet<int>();
            foreach (Word word in file.Entries)
            {
                if (word == null) throw new InvalidDataException("词条不能为 null。");
                if (word.Id <= 0 || !ids.Add(word.Id) || word.Answers == null || word.Answers.Length == 0 ||
                    word.Answers.Any(a => string.IsNullOrWhiteSpace(a) || a.Length > 80) ||
                    string.IsNullOrWhiteSpace(word.Meaning) || string.IsNullOrWhiteSpace(word.Chapter) ||
                    string.IsNullOrWhiteSpace(word.Cloze) || !word.Cloze.Contains("______"))
                    throw new InvalidDataException("词条格式或遮词数据不完整，ID：" + word.Id);
                // 内置例句也必须遮住登记过的变形，不能只校验词条原形。
                if (word.MaskedForms == null || word.MaskedForms.Length == 0 || word.MaskedForms.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("词条缺少已遮词形，ID：" + word.Id);
                foreach (string answer in word.Answers.Concat(word.MaskedForms))
                {
                    if (Regex.IsMatch(word.Cloze, @"(?<![A-Za-z])" + Regex.Escape(answer) + @"(?![A-Za-z])", RegexOptions.IgnoreCase))
                        throw new InvalidDataException("例句泄露答案，ID：" + word.Id);
                }
                foreach (string answer in word.Answers)
                    if (Regex.IsMatch(word.Meaning, @"(?<![A-Za-z])" + Regex.Escape(answer) + @"(?![A-Za-z])", RegexOptions.IgnoreCase))
                        throw new InvalidDataException("中文释义中含有英文答案，ID：" + word.Id);
            }
            file.Entries = file.Entries.OrderBy(w => w.Id).ToList();
            if (!file.Entries.Select(w => w.Id).SequenceEqual(Enumerable.Range(1, EntryCount)))
                throw new InvalidDataException("程序内置词库的词条数量或 ID 不符。");
            return file;
        }
    }

    public static class ReviewQueue
    {
        public static bool IsDue(WordProgress progress, int sequence, DateTime now)
        {
            return progress.DueUtc <= now || (progress.DueAfter > 0 && progress.DueAfter <= sequence);
        }

        public static int IntervalDays(int streak)
        {
            return IntervalDays(streak, new[] { 1, 3, 7, 14 });
        }

        public static int IntervalDays(int streak, int[] days)
        {
            return days[Math.Min(Math.Max(streak - 1, 0), days.Length - 1)];
        }
    }
}
