using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Vcrmb.Core
{
    public sealed class PracticeGroup
    {
        public int Number { get; set; }
        public int FirstPosition { get; set; }
        public List<Word> Words { get; set; }
        public string Label { get { return "第 " + Number + " 组 · 第 " + FirstPosition + "–" + (FirstPosition + Words.Count - 1) + " 词（" + Words.Count + " 词）"; } }
    }

    // 只保存分组范围和本轮已处理词条，不保存作答流水或日期。
    public sealed class PracticeGroupState
    {
        public string Chapter { get; set; }
        public int GroupSize { get; set; }
        public int GroupNumber { get; set; }
        public bool ReviewOnly { get; set; }
        public int[] WordIds { get; set; }
        public int[] CompletedWordIds { get; set; }
        public bool IsComplete { get { return CompletedWordIds.Length == WordIds.Length; } }
        public string StorageKey { get { return PracticeGroups.Key(Chapter, GroupSize, GroupNumber, ReviewOnly); } }
    }

    public static class PracticeGroups
    {
        internal static readonly Lazy<VocabularyFile> Bundled = new Lazy<VocabularyFile>(VocabularyLoader.LoadBundled);

        public static List<PracticeGroup> Create(VocabularyFile library, string chapter, int groupSize)
        {
            if (groupSize < 1 || groupSize > VocabularyLoader.EntryCount)
                throw new InvalidDataException("每组词数应为 1–3674 之间的整数。");
            List<Word> words = library.Entries.Where(w => string.IsNullOrEmpty(chapter) || w.Chapter == chapter).OrderBy(w => w.Id).ToList();
            if (words.Count == 0) throw new InvalidDataException("学习章节不存在，请重新选择。");
            List<PracticeGroup> groups = new List<PracticeGroup>();
            for (int offset = 0; offset < words.Count; offset += groupSize)
                groups.Add(new PracticeGroup { Number = groups.Count + 1, FirstPosition = offset + 1,
                    Words = words.GetRange(offset, Math.Min(groupSize, words.Count - offset)) });
            return groups;
        }

        public static PracticeGroup Selected(VocabularyFile library, AppSettings settings)
        {
            List<PracticeGroup> groups = Create(library, settings.Chapter, settings.GroupSize);
            if (settings.GroupNumber < 1 || settings.GroupNumber > groups.Count)
                throw new InvalidDataException("组号应在 1–" + groups.Count + " 之间。");
            return groups[settings.GroupNumber - 1];
        }

        internal static string Key(string chapter, int size, int number, bool reviewOnly)
        {
            return "practice/" + size.ToString(CultureInfo.InvariantCulture) + "/" + number.ToString(CultureInfo.InvariantCulture) +
                "/" + (reviewOnly ? "review/" : "all/") + (chapter ?? "");
        }

        internal static void ValidateState(PracticeGroupState state)
        {
            if (state == null || state.WordIds == null || state.CompletedWordIds == null)
                throw new InvalidDataException("分组练习进度不完整。");
            PracticeGroup group = Selected(Bundled.Value, new AppSettings { Chapter = state.Chapter, GroupSize = state.GroupSize, GroupNumber = state.GroupNumber });
            int[] expected = group.Words.Select(w => w.Id).ToArray();
            if (state.WordIds.Distinct().Count() != state.WordIds.Length || state.WordIds.Except(expected).Any() ||
                !state.WordIds.SequenceEqual(state.WordIds.OrderBy(id => id)) ||
                (!state.ReviewOnly && !state.WordIds.SequenceEqual(expected)) ||
                state.CompletedWordIds.Distinct().Count() != state.CompletedWordIds.Length || state.CompletedWordIds.Except(state.WordIds).Any())
                throw new InvalidDataException("分组练习进度与内置词库不一致。");
        }
    }
}
