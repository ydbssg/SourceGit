using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class ConfigTableDiffView : UserControl
    {
        public ConfigTableDiffView()
        {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == DataContextProperty && change.OldValue is ViewModels.ConfigTableDiff previous)
                previous.CancelLoad();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            if (DataContext is ViewModels.ConfigTableDiff vm)
                vm.CancelLoad();
        }

        private async void OnLoad(object _, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is ViewModels.ConfigTableDiff vm)
                await vm.LoadAsync();
        }

        private void OnCurrentCellChanged(object sender, EventArgs _) => UpdateCellDetails((DataGrid)sender);

        private void OnSelectedRowChanged(object sender, SelectionChangedEventArgs _) => UpdateCellDetails((DataGrid)sender);

        private void UpdateCellDetails(DataGrid grid)
        {
            if (CellDetails == null)
                return;

            var cell = grid.SelectedItem is Models.ConfigRowDiff row && grid.CurrentColumn is { DisplayIndex: >= 0 } column
                && column.DisplayIndex < row.Cells.Length
                ? row.Cells[column.DisplayIndex]
                : default;
            CellDetails.IsVisible = cell.Before != null || cell.After != null;
            BeforeValue.Text = cell.Before;
            AfterValue.Text = cell.After;
        }

        private async void OnOpenHex(object _, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is ViewModels.ConfigTableDiff vm && vm.RawFile is { } file)
                await this.ShowDialogAsync(new ViewModels.BinaryFileViewer(vm.Repository, file.Path, file.Revision));
        }
    }
}
