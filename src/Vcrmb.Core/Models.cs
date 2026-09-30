using System;
using System.Collections.Generic;

namespace Vcrmb.Core
{
    public sealed class VocabularyFile
    {
        public int SchemaVersion { get; set; }
        public string SourceUrl { get; set; }
        public string SourceCommit { get; set; }
        public List<Word> Entries { get; set; }
    }

    public sealed class Word
    {
        public int Id { get; set; }
        public string Chapter { get; set; }
        public int Group { get; set; }
        public string[] Answers { get; set; }
        public string PartOfSpeech { get; set; }
        public string Meaning { get; set; }
        public string OriginalMeaning { get; set; }
        public string OriginalExample { get; set; }
        public string Example { get; set; }
        public string Cloze { get; set; }
        public string[] MaskedForms { get; set; }
        public string ExampleOrigin { get; set; }
        public string Extra { get; set; }
    }

    public sealed class Round
    {
        public string Id { get; set; }
        public int WordId { get; set; }
        public string Outcome { get; set; }
        public int Errors { get; set; }
        public bool HintUsed { get; set; }
        public bool Pasted { get; set; }
        public string Input { get; set; }
        public Round Copy() { return (Round)MemberwiseClone(); }
        public bool IsIndependent { get { return Errors == 0 && !HintUsed && !Pasted; } }
    }

    public sealed class WordProgress
    {
        public int WordId { get; set; }
        public int Streak { get; set; }
        public int DueAfter { get; set; }
        public DateTime DueUtc { get; set; }
        public bool NeedsReview { get; set; }
    }

    public sealed class WordStatistics
    {
        public int WordId { get; set; }
        public long Correct { get; set; }
        public long Errors { get; set; }
        public double? Accuracy { get { return Correct + Errors == 0 ? (double?)null : (double)Correct / (Correct + Errors); } }
    }

    public sealed class AppSettings
    {
        public string Chapter { get; set; }
        public int GroupSize { get; set; }
        public int GroupNumber { get; set; }
        public bool ReviewOnly { get; set; }
        public bool Topmost { get; set; }
        public bool PositionLocked { get; set; }
        public int FontSize { get; set; }
        public string Hotkey { get; set; }
        public string HintShortcut { get; set; }
        public string SkipShortcut { get; set; }
        public string RestartGroupShortcut { get; set; }
        public string HideShortcut { get; set; }
        public bool HasPosition { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public string Backdrop { get; set; }
        public double BackdropOpacity { get; set; }
        public bool DarkAppearance { get; set; }
        public bool CaretBlinkEnabled { get; set; }
        public string Monitor { get; set; }
        public double EdgeRight { get; set; }
        public double EdgeBottom { get; set; }
        public int[] ReviewDays { get; set; }
        public AppSettings()
        {
            Chapter = ""; Topmost = true; FontSize = 16; Hotkey = "Ctrl+Alt+Shift+W"; Width = 360;
            GroupSize = 20; GroupNumber = 1;
            HintShortcut = "F1"; SkipShortcut = "F2"; RestartGroupShortcut = "F3"; HideShortcut = "Esc";
            Backdrop = "frosted"; BackdropOpacity = 0.65;
            ReviewDays = new[] { 1, 3, 7, 14 };
        }
        public AppSettings Copy()
        {
            AppSettings copy = (AppSettings)MemberwiseClone();
            copy.ReviewDays = (int[])ReviewDays.Clone(); return copy;
        }
    }
}
