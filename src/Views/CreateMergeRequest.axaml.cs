using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class CreateMergeRequest : ChromelessWindow
    {
        public CreateMergeRequest()
        {
            CloseOnESC = true;
            InitializeComponent();
            Resized += (_, _) => KeepFooterOnScreen();
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _opened = true;
            if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
                MaxHeight = (screen.WorkingArea.Height - 24) / DesktopScaling;
            if (DataContext is ViewModels.CreateMergeRequest vm)
                await vm.LoadTargetsAsync();
        }

        private void KeepFooterOnScreen()
        {
            if (!_opened || SizeToContent != SizeToContent.Height ||
                (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen)
                return;

            var area = screen.WorkingArea;
            var height = PixelSize.FromSize(ClientSize, DesktopScaling).Height;
            var lastTop = Math.Max(area.Y + 12, area.Bottom - height - 12);
            if (Position.Y > lastTop)
                Position = new PixelPoint(Position.X, lastTop);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (DataContext is ViewModels.CreateMergeRequest { IsBusy: true })
                e.Cancel = true;
        }

        private async void OnRemoteChanged(object _, SelectionChangedEventArgs e)
        {
            if (_opened && DataContext is ViewModels.CreateMergeRequest vm && e.AddedItems.Count > 0 && e.AddedItems[0] is Models.Remote remote)
            {
                vm.SelectedRemote = remote;
                await vm.LoadTargetsAsync();
            }
        }

        private async void OnRefreshTargets(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.CreateMergeRequest vm)
                await vm.LoadTargetsAsync();
        }

        private async void OnPublish(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.CreateMergeRequest vm)
                await vm.PublishAsync();
        }

        private void OnOpenRequest(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.CreateMergeRequest vm)
                vm.OpenBrowserLink();
        }

        private void OnClose(object _, RoutedEventArgs e)
        {
            Close();
        }

        private bool _opened;
    }
}
