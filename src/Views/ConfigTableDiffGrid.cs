using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

using SourceGit.Models;

namespace SourceGit.Views
{
    public sealed class ConfigTableDiffGrid : DataGrid
    {
        public static readonly StyledProperty<ConfigTableChanges> ComparisonProperty =
            AvaloniaProperty.Register<ConfigTableDiffGrid, ConfigTableChanges>(nameof(Comparison));

        public ConfigTableChanges Comparison
        {
            get => GetValue(ComparisonProperty);
            set => SetValue(ComparisonProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(DataGrid);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != ComparisonProperty)
                return;

            ItemsSource = null;
            FrozenColumnCount = 0;
            Columns.Clear();
            var comparison = Comparison;
            if (comparison == null || comparison.Fields.Length == 0)
                return;

            for (var i = 0; i < comparison.Fields.Length; i++)
                AddField(comparison.Fields[i], i);

            FrozenColumnCount = 1;
            ItemsSource = comparison.Rows;
        }

        private void AddField(string name, int index)
        {
            var width = index == 0 ? 110 : Math.Clamp(name.Length * 9 + 30, 120, 230);
            Columns.Add(new DataGridTemplateColumn
            {
                Header = name,
                Width = new DataGridLength(width),
                CellTemplate = new FuncDataTemplate<ConfigRowDiff>((row, _) => CreateCell(row, index)),
                ClipboardContentBinding = new Binding
                {
                    Converter = new FuncValueConverter<ConfigRowDiff, string>(row => row == null ? null : GetCellText(row, index)),
                },
            });
        }

        private Control CreateCell(ConfigRowDiff row, int index)
        {
            var cell = row.Cells[index];
            if (!cell.IsChanged)
            {
                var text = GetCellText(row, index);
                var value = CreateText(text);
                if (index == 0)
                    value.FontWeight = FontWeight.SemiBold;
                if (!string.IsNullOrEmpty(text))
                    ToolTip.SetTip(value, text);
                return value;
            }

            if (cell.Before == null || cell.After == null)
            {
                var content = CreateChangedValue(cell.After ?? cell.Before, cell.After != null);
                ToolTip.SetTip(content, cell.After ?? cell.Before);
                return content;
            }

            var values = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
            var before = CreateChangedValue(cell.Before, false);
            var arrow = CreateText("→");
            var after = CreateChangedValue(cell.After, true);
            Grid.SetColumn(arrow, 1);
            Grid.SetColumn(after, 2);
            values.Children.Add(before);
            values.Children.Add(arrow);
            values.Children.Add(after);
            ToolTip.SetTip(values, new TextBlock
            {
                Text = $"{App.Text("Diff.Old")}  {cell.Before}\n{App.Text("Diff.New")}  {cell.After}",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
            });
            return values;
        }

        private Border CreateChangedValue(string text, bool isAfter) => new()
        {
            Background = this.FindResource(isAfter ? "Brush.Diff.AddedBG" : "Brush.Diff.DeletedBG") as IBrush,
            Child = CreateText(text),
        };

        private static string GetCellText(ConfigRowDiff row, int index)
        {
            var cell = row.Cells[index];
            if (cell.IsChanged && cell.Before != null && cell.After != null)
                return $"{cell.Before} → {cell.After}";
            if (row.IsSchema && index == 0 && !cell.IsChanged)
                return App.Text("Diff.ConfigTable.FieldTypes");
            return cell.After ?? cell.Before;
        }

        private static TextBlock CreateText(string text) => new()
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0),
        };
    }
}
