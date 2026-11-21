using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SourceGit.Models;

namespace SourceGit.Views
{
    public partial class SpreadsheetMergeDialog : Window
    {
        public SpreadsheetMergeDialog()
        {
            InitializeComponent();
        }

        public SpreadsheetMergeDialog(List<SpreadsheetMergeRow> conflicts, bool canAbortPull) : this()
        {
            _conflicts = conflicts;
            if (!canAbortPull)
                CancelMerge.Content = App.Text("Cancel");
            foreach (var row in conflicts)
            {
                foreach (var field in row.Fields)
                {
                    field.Choices =
                    [
                        Choice(SpreadsheetMergeSide.Local, "Local", field.LocalValue),
                        Choice(SpreadsheetMergeSide.Remote, "Remote", field.RemoteValue),
                        Choice(SpreadsheetMergeSide.Base, "Base", field.BaseValue),
                    ];
                    field.SelectedChoice = field.Choices.Find(choice => choice.Side == field.PreferredSide);
                }
            }

            Rows.ItemsSource = conflicts;
            Rows.SelectedIndex = 0;
            RefreshRemaining();
        }

        private static SpreadsheetMergeChoice Choice(SpreadsheetMergeSide side, string label, string value) =>
            new(side, $"{App.Text($"SpreadsheetMerge.{label}")} · {value}");

        private void OnRowSelected(object sender, SelectionChangedEventArgs e)
        {
            if (Rows.SelectedItem is not SpreadsheetMergeRow row)
                return;

            Fields.ItemsSource = row.Fields;
            Fields.Columns[^1].IsVisible = row.CanCombineFields;
            ConfirmFields.IsEnabled = row.CanCombineFields;
            UseLocal.Content = App.Text(row.HasLocalRow ? "SpreadsheetMerge.UseLocal" : "SpreadsheetMerge.DeleteLocal");
            UseRemote.Content = App.Text(row.HasRemoteRow ? "SpreadsheetMerge.UseRemote" : "SpreadsheetMerge.DeleteRemote");
        }

        private void OnUseLocal(object sender, RoutedEventArgs e) => Resolve(SpreadsheetMergeSide.Local);
        private void OnUseRemote(object sender, RoutedEventArgs e) => Resolve(SpreadsheetMergeSide.Remote);

        private void Resolve(SpreadsheetMergeSide side)
        {
            if (Rows.SelectedItem is SpreadsheetMergeRow row)
            {
                row.Resolve(side);
                MoveToNextRow();
            }
        }

        private void OnConfirmFields(object sender, RoutedEventArgs e)
        {
            if (Rows.SelectedItem is SpreadsheetMergeRow { CanCombineFields: true } row)
            {
                row.ResolveFields();
                MoveToNextRow();
            }
        }

        private void MoveToNextRow()
        {
            var next = _conflicts.FindIndex(static row => !row.Resolved);
            if (next >= 0)
                Rows.SelectedIndex = next;
            RefreshRemaining();
        }

        private void RefreshRemaining()
        {
            var count = _conflicts.Count(static row => !row.Resolved);
            Remaining.Text = App.Text("SpreadsheetMerge.Remaining", count);
            Apply.IsEnabled = count == 0;
        }

        private void OnApply(object sender, RoutedEventArgs e) => Close(true);
        private void OnAbort(object sender, RoutedEventArgs e) => Close(false);

        private readonly List<SpreadsheetMergeRow> _conflicts = [];
    }
}
