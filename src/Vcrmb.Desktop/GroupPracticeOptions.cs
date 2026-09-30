using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Vcrmb.Core;

namespace Vcrmb.Desktop
{
    internal sealed class GroupPracticeOptions : StackPanel
    {
        internal readonly TextBox CountInput;
        internal readonly ComboBox GroupInput;
        private readonly TextBlock summary;
        private readonly VocabularyFile library;
        private string chapter;

        internal GroupPracticeOptions(VocabularyFile library, AppSettings settings)
        {
            this.library = library; chapter = settings.Chapter;
            StackPanel countRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            countRow.Children.Add(Ui.Text("每组练习词数 ", 12));
            CountInput = new TextBox { Text = settings.GroupSize.ToString(CultureInfo.InvariantCulture), Width = 82,
                MaxLength = 4, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(CountInput, "每组练习词数"); countRow.Children.Add(CountInput); Children.Add(countRow);
            Children.Add(Ui.Text("跳转到哪一组", 12));
            GroupInput = new ComboBox { MinHeight = 30, DisplayMemberPath = "Label", IsTextSearchEnabled = true };
            TextSearch.SetTextPath(GroupInput, "Number"); AutomationProperties.SetName(GroupInput, "练习组号");
            Children.Add(GroupInput); summary = Ui.Text("", 11); Children.Add(summary);
            Refresh(settings.GroupNumber);
            CountInput.TextChanged += delegate { Refresh(1); };
            GroupInput.SelectionChanged += delegate { UpdateSummary(); };
        }

        internal void SetChapter(string value)
        {
            chapter = value; Refresh(1);
        }

        private bool ReadSize(out int size)
        {
            return int.TryParse(CountInput.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out size) &&
                size >= 1 && size <= VocabularyLoader.EntryCount;
        }

        private void Refresh(int groupNumber)
        {
            int size;
            if (!ReadSize(out size))
            {
                GroupInput.IsEnabled = false; summary.Text = "请输入 1–3674 之间的整数。"; return;
            }
            var groups = PracticeGroups.Create(library, chapter, size);
            GroupInput.ItemsSource = groups; GroupInput.SelectedIndex = Math.Min(groupNumber, groups.Count) - 1;
            GroupInput.IsEnabled = true; UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (!GroupInput.IsEnabled) return;
            summary.Text = "共 " + GroupInput.Items.Count + " 组，最后一组按剩余词数。保存设置后继续所选组；一组完成后按跳过快捷键进入下一组。";
        }

        internal void ApplyTo(AppSettings settings)
        {
            int size;
            if (!ReadSize(out size) || !GroupInput.IsEnabled || GroupInput.SelectedItem == null)
                throw new InvalidOperationException("请输入有效的每组词数并选择组号。");
            settings.GroupSize = size; settings.GroupNumber = ((PracticeGroup)GroupInput.SelectedItem).Number;
        }
    }
}
