using System;
using System.Collections.Generic;
using System.Linq;

namespace Vcrmb.Core
{
    public sealed class LearningSession
    {
        private readonly LearningStore store;
        private readonly VocabularyFile library;
        public Round Current { get; private set; }
        public PracticeGroupState GroupState { get; private set; }
        public int GroupCount { get; private set; }
        public Word Word { get { return Current == null ? null : library.Entries.First(w => w.Id == Current.WordId); } }

        public LearningSession(LearningStore store, VocabularyFile library)
        {
            this.store = store; this.library = library;
            Current = store.LoadActive();
            if (Current != null && !library.Entries.Any(w => w.Id == Current.WordId))
                throw new InvalidOperationException("当前词库缺少上次学习的词条，学习记录已保留。");
        }

        public void Next(AppSettings settings, DateTime now)
        {
            PracticeGroup selected = PracticeGroups.Selected(library, settings);
            GroupCount = PracticeGroups.Create(library, settings.Chapter, settings.GroupSize).Count;
            GroupState = store.LoadPracticeGroup(settings);
            if (GroupState == null || (settings.ReviewOnly && GroupState.WordIds.Length == 0))
            {
                var progress = store.Progress(); int sequence = store.Sequence;
                GroupState = new PracticeGroupState { Chapter = settings.Chapter ?? "", GroupSize = settings.GroupSize,
                    GroupNumber = settings.GroupNumber, ReviewOnly = settings.ReviewOnly, CompletedWordIds = new int[0],
                    WordIds = selected.Words.Where(w => !settings.ReviewOnly || (progress.ContainsKey(w.Id) &&
                        ReviewQueue.IsDue(progress[w.Id], sequence, now)))
                        .Select(w => w.Id).ToArray() };
                store.InitializePracticeGroup(settings, GroupState);
            }
            HashSet<int> remaining = new HashSet<int>(GroupState.WordIds.Except(GroupState.CompletedWordIds));
            if (Current != null && Current.Outcome == "active")
            {
                if (remaining.Contains(Current.WordId)) return;
                store.Suspend();
            }
            // 返回中途离开的组时先续上原草稿；其余词按原顺序，每词在本轮最多出现一次。
            Round suspended = store.Suspended().FirstOrDefault(r => remaining.Contains(r.WordId));
            int nextId = suspended == null ? GroupState.WordIds.FirstOrDefault(remaining.Contains) : suspended.WordId;
            Current = nextId == 0 ? null : store.Start(library.Entries.First(w => w.Id == nextId));
        }

        public void SwitchScope(AppSettings settings, DateTime now, bool restart = false)
        {
            ChangeScope(settings, now, restart, true);
        }

        private void ChangeScope(AppSettings settings, DateTime now, bool restart, bool resetDrafts)
        {
            Round previousRound = Current; PracticeGroupState previousGroup = GroupState; int previousCount = GroupCount;
            try
            {
                store.SwitchPracticeGroup(settings, restart, resetDrafts, now, delegate { Current = null; Next(settings, now); });
            }
            catch
            {
                Current = previousRound; GroupState = previousGroup; GroupCount = previousCount; throw;
            }
        }

        public AppSettings NextGroup(AppSettings settings, DateTime now)
        {
            if (GroupState == null || !GroupState.IsComplete || (Current != null && Current.Outcome == "active"))
                throw new InvalidOperationException("请先完成当前组。");
            AppSettings next = settings.Copy();
            int count = PracticeGroups.Create(library, settings.Chapter, settings.GroupSize).Count;
            next.GroupNumber = settings.GroupNumber < count ? settings.GroupNumber + 1 : 1;
            PracticeGroupState previous = store.LoadPracticeGroup(next);
            ChangeScope(next, now, previous != null && previous.IsComplete, false); return next;
        }

        public void SaveInput(string text)
        {
            if (Current == null || Current.Outcome != "active") return;
            Round candidate = Current.Copy(); candidate.Input = text;
            store.Save(candidate); Current = candidate;
        }

        public void Error()
        {
            Round candidate = Current.Copy(); candidate.Errors++;
            store.RecordError(candidate); Current = candidate;
        }

        public void Hint()
        {
            if (Current == null || Current.Outcome != "active" || Current.HintUsed) return;
            Round candidate = Current.Copy(); candidate.HintUsed = true;
            store.Save(candidate); Current = candidate;
        }

        public void Paste()
        {
            if (Current.Pasted) return;
            Round candidate = Current.Copy(); candidate.Pasted = true;
            store.Save(candidate); Current = candidate;
        }

        public bool Complete(bool correct, DateTime now)
        {
            if (Current == null || Current.Outcome != "active") return false;
            if (correct && !AnswerRules.IsCorrect(Word, Current.Input)) return false;
            bool saved = store.Finish(Current, correct, now, GroupState == null ? null : GroupState.StorageKey);
            if (saved)
            {
                Current = Current.Copy(); Current.Outcome = correct ? "correct" : "skip";
                if (GroupState != null) GroupState.CompletedWordIds = GroupState.CompletedWordIds.Concat(new[] { Current.WordId }).ToArray();
            }
            return saved;
        }
    }
}
