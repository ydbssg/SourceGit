using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class SpreadsheetDiffView : UserControl
    {
        public SpreadsheetDiffView()
        {
            InitializeComponent();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            if (DataContext is ViewModels.SpreadsheetDiff vm)
                vm.CancelLoad();
        }

        private async void OnLoad(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SpreadsheetDiff vm)
                await vm.LoadAsync();
            e.Handled = true;
        }

        private void OnLocateChange(object _, RoutedEventArgs e) => NavigateChange(0, e);
        private void OnLocatePreviousChange(object _, RoutedEventArgs e) => NavigateChange(-1, e);
        private void OnLocateNextChange(object _, RoutedEventArgs e) => NavigateChange(1, e);

        private void NavigateChange(int offset, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SpreadsheetDiff vm && vm.LocateChange(offset) is { } target)
                SheetGrid.LocateCell(target.Row, target.Column);

            e.Handled = true;
        }
    }
}
