using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SourceGit.Models;

namespace SourceGit.Views
{
    public sealed class SpreadsheetSheetView : DataGrid
    {
        public static readonly StyledProperty<SpreadsheetSheetVersion> VersionProperty =
            AvaloniaProperty.Register<SpreadsheetSheetView, SpreadsheetSheetVersion>(nameof(Version));

        public static readonly StyledProperty<bool> OnlyChangedRowsProperty =
            AvaloniaProperty.Register<SpreadsheetSheetView, bool>(nameof(OnlyChangedRows), true);

        public SpreadsheetSheetVersion Version
        {
            get => GetValue(VersionProperty);
            set => SetValue(VersionProperty, value);
        }

        public bool OnlyChangedRows
        {
            get => GetValue(OnlyChangedRowsProperty);
            set => SetValue(OnlyChangedRowsProperty, value);
        }

        protected override Type StyleKeyOverride => typeof(DataGrid);

        public SpreadsheetSheetView()
        {
            AutoGenerateColumns = false;
            IsReadOnly = true;
            CanUserSortColumns = false;
            HeadersVisibility = DataGridHeadersVisibility.Column;
            GridLinesVisibility = DataGridGridLinesVisibility.All;
            RowHeight = 26;
        }

        public void LocateCell(SpreadsheetRow row, int column)
        {
            if (!_columnsByNumber.TryGetValue(column, out var gridColumn))
                return;

            ClearFrozenLocation();
            if (row.Number <= FrozenRowCount)
            {
                SelectedItem = null;
                if (_frozenChanges.TryGetValue((row.Number, column), out var cell))
                {
                    _locatedFrozenCell = cell;
                    cell.BorderBrush = this.FindResource("Brush.Accent") as IBrush;
                    cell.BorderThickness = new Thickness(1);
                }

                ScrollIntoView(null, gridColumn);
                return;
            }

            SelectedItem = row;
            if (CurrentColumn != null)
                CurrentColumn = gridColumn;

            ScrollIntoView(row, gridColumn);
            Focus();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == VersionProperty)
            {
                var previous = change.GetOldValue<SpreadsheetSheetVersion>();
                var current = Version;
                var sameSheet = previous != null && current != null && previous.Sheet.Name == current.Sheet.Name;
                var horizontal = _horizontalScroll?.Value ?? 0;
                var vertical = _verticalScroll?.Value ?? 0;
                var widths = sameSheet ? _columnsByNumber.ToDictionary(static pair => pair.Key, static pair => pair.Value.Width) : null;
                BuildColumns(widths);
                UpdateRows();
                if (sameSheet)
                    RestorePosition(current, horizontal, vertical);
            }
            else if (change.Property == OnlyChangedRowsProperty)
            {
                UpdateRows();
            }
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _horizontalScroll = e.NameScope.Find<ScrollBar>("PART_HorizontalScrollbar");
            _verticalScroll = e.NameScope.Find<ScrollBar>("PART_VerticalScrollbar");
        }

        private void RestorePosition(SpreadsheetSheetVersion version, double horizontal, double vertical)
        {
            var onlyChanged = OnlyChangedRows;
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(version, Version) || onlyChanged != OnlyChangedRows)
                    return;

                RestoreOffset(_horizontalScroll, horizontal);
                RestoreOffset(_verticalScroll, vertical);
            }, DispatcherPriority.Loaded);
        }

        private static void RestoreOffset(ScrollBar bar, double offset)
        {
            if (bar == null || !double.IsFinite(offset) || bar.Maximum < bar.Minimum)
                return;

            bar.Value = Math.Clamp(offset, bar.Minimum, bar.Maximum);
            // DataGrid applies the offset on the scroll event, including when the thumb is moved.
            NotifyScroll(bar, ScrollEventType.ThumbTrack);
        }

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "OnScroll")]
        private static extern void NotifyScroll(ScrollBar bar, ScrollEventType scrollEventType);

        private void BuildColumns(Dictionary<int, DataGridLength> widths)
        {
            ItemsSource = null;
            FrozenColumnCount = 0;
            Columns.Clear();
            _columnsByNumber.Clear();
            _frozenChanges.Clear();
            _locatedFrozenCell = null;

            var version = Version;
            if (version == null)
                return;

            var frozenRows = version.Sheet.Rows.TakeWhile(static row => row.Number <= FrozenRowCount).ToArray();
            ColumnHeaderHeight = ColumnTitleHeight + frozenRows.Length * RowHeight;
            Columns.Add(new DataGridTemplateColumn
            {
                Header = BuildColumnHeader("#", version, 0, frozenRows),
                Width = new DataGridLength(50),
                CellTemplate = new FuncDataTemplate<SpreadsheetRow>(static (row, _) => CreateText(row.Number.ToString()))
            });

            var header = version.IdColumn > 0 ? version.Sheet.Rows.Find(static row => row.Number == 3) : null;
            if (version.IdColumn > 0)
                AddColumn(version, version.IdColumn, header, frozenRows, widths);
            foreach (var column in version.Sheet.Columns)
            {
                if (column != version.IdColumn)
                    AddColumn(version, column, header, frozenRows, widths);
            }

            FrozenColumnCount = version.IdColumn > 0 ? 2 : 1;
        }

        private void AddColumn(SpreadsheetSheetVersion version, int column, SpreadsheetRow header, SpreadsheetRow[] frozenRows,
            Dictionary<int, DataGridLength> widths)
        {
            var letter = SpreadsheetSheet.ColumnName(column);
            var field = header?.GetCell(column)?.Value;
            var title = string.IsNullOrEmpty(field) ? letter : $"{letter} · {field}";
            var gridColumn = new DataGridTemplateColumn
            {
                Header = BuildColumnHeader(title, version, column, frozenRows),
                Width = widths?.GetValueOrDefault(column, new DataGridLength(132)) ?? new DataGridLength(132),
                CellTemplate = new FuncDataTemplate<SpreadsheetRow>((row, _) => CreateCell(version, row, column))
            };
            Columns.Add(gridColumn);
            _columnsByNumber.Add(column, gridColumn);
        }

        private StackPanel BuildColumnHeader(string title, SpreadsheetSheetVersion version, int column, SpreadsheetRow[] frozenRows)
        {
            // Keep the frozen cells in the native column headers so they use the same widths and horizontal scroll.
            var header = new StackPanel { ClipToBounds = true };
            var lineBrush = this.FindResource("Brush.Border2") as IBrush;
            header.Children.Add(new Border
            {
                Height = ColumnTitleHeight,
                BorderBrush = lineBrush,
                BorderThickness = FrozenRowSeparator,
                Child = CreateText(title),
            });

            foreach (var row in frozenRows)
            {
                var cell = new Border
                {
                    Height = RowHeight,
                    BorderBrush = lineBrush,
                    BorderThickness = FrozenRowSeparator,
                    Child = column == 0 ? CreateText(row.Number.ToString()) : CreateCell(version, row, column),
                };
                header.Children.Add(cell);

                if (column > 0 && version.GetChange(row.Number, column) != SpreadsheetChangeKind.None)
                    _frozenChanges.Add((row.Number, column), cell);
            }

            return header;
        }

        private void UpdateRows()
        {
            ClearFrozenLocation();
            var rows = OnlyChangedRows ? Version?.ChangedRows : Version?.Sheet.Rows;
            ItemsSource = rows is { Count: > 0 } && rows[0].Number <= FrozenRowCount
                ? rows.SkipWhile(static row => row.Number <= FrozenRowCount).ToArray()
                : rows;
        }

        private void ClearFrozenLocation()
        {
            if (_locatedFrozenCell == null)
                return;

            _locatedFrozenCell.BorderBrush = this.FindResource("Brush.Border2") as IBrush;
            _locatedFrozenCell.BorderThickness = FrozenRowSeparator;
            _locatedFrozenCell = null;
        }

        private Control CreateCell(SpreadsheetSheetVersion version, SpreadsheetRow row, int column)
        {
            var cell = row.GetCell(column);
            var change = version.GetChange(row.Number, column);
            if (change == SpreadsheetChangeKind.None)
                return CreateText(cell?.Display, cell?.ToolTipText);

            var label = App.Text($"Diff.Spreadsheet.{change}");
            var detail = cell?.ToolTipText;
            var tip = string.IsNullOrEmpty(detail) ? label : $"{label}\n{detail}";
            var brush = change == SpreadsheetChangeKind.Deleted || (version.IsBefore && change != SpreadsheetChangeKind.Added)
                ? "Brush.Diff.DeletedBG"
                : "Brush.Diff.AddedBG";
            var border = new Border { Child = CreateText(cell?.Display, tip) };
            border.Background = this.FindResource(brush) as IBrush;
            ToolTip.SetTip(border, tip);
            return border;
        }

        private static TextBlock CreateText(string text, string tooltip = null)
        {
            var value = new TextBlock
            {
                Margin = new Thickness(6, 0),
                Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (!string.IsNullOrEmpty(tooltip ?? text))
                ToolTip.SetTip(value, tooltip ?? text);
            return value;
        }

        private const int FrozenRowCount = 4;
        private const double ColumnTitleHeight = 32;
        private static readonly Thickness FrozenRowSeparator = new(0, 0, 0, 1);
        private readonly Dictionary<int, DataGridColumn> _columnsByNumber = [];
        private readonly Dictionary<(int Row, int Column), Border> _frozenChanges = [];
        private Border _locatedFrozenCell;
        private ScrollBar _horizontalScroll;
        private ScrollBar _verticalScroll;
    }
}
