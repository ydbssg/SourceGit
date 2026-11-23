using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public class AuthorNameBox : Border
    {
        public static readonly StyledProperty<string> AuthorNameProperty =
            AvaloniaProperty.Register<AuthorNameBox, string>(nameof(AuthorName));

        public static readonly StyledProperty<bool> IsCurrentHeadProperty =
            AvaloniaProperty.Register<AuthorNameBox, bool>(nameof(IsCurrentHead));

        public static readonly (string Key, string Hex)[] Palette =
        [
            ("Orange", "#E85D04"),
            ("Blue", "#3A86FF"),
            ("Green", "#2A9D8F"),
            ("Red", "#E63946"),
            ("Purple", "#9B5DE5"),
            ("Gold", "#E9C46A"),
            ("Pink", "#FF4D8D"),
            ("Cyan", "#00B4D8"),
        ];

        public string AuthorName
        {
            get => GetValue(AuthorNameProperty);
            set => SetValue(AuthorNameProperty, value);
        }

        public bool IsCurrentHead
        {
            get => GetValue(IsCurrentHeadProperty);
            set => SetValue(IsCurrentHeadProperty, value);
        }

        public AuthorNameBox()
        {
            Background = Brushes.Transparent;
            CornerRadius = new CornerRadius(3);
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Child = _text = new TextBlock
            {
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            ViewModels.Preferences.AuthorBoxesChanged += OnBoxesChanged;
            Apply();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            ViewModels.Preferences.AuthorBoxesChanged -= OnBoxesChanged;
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == AuthorNameProperty || change.Property == IsCurrentHeadProperty)
                Apply();
        }

        private void OnBoxesChanged()
        {
            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                Apply();
            else
                global::Avalonia.Threading.Dispatcher.UIThread.Post(Apply);
        }

        private void Apply()
        {
            _text.Text = AuthorName ?? string.Empty;
            _text.FontWeight = IsCurrentHead ? FontWeight.Bold : FontWeight.Regular;

            var color = ViewModels.Preferences.Instance.GetAuthorBoxColor(AuthorName);
            if (string.IsNullOrEmpty(color))
            {
                BorderThickness = new Thickness(0);
                Padding = new Thickness(0);
                BorderBrush = null;
                return;
            }

            BorderThickness = new Thickness(1.5);
            Padding = new Thickness(4, 0);
            BorderBrush = BrushFor(color);
        }

        public static void ShowMenu(Control owner, string name)
        {
            if (owner == null || string.IsNullOrEmpty(name))
                return;

            var prefs = ViewModels.Preferences.Instance;
            var current = prefs.GetAuthorBoxColor(name);
            var menu = new ContextMenu();

            var title = new MenuItem();
            title.Header = string.IsNullOrEmpty(current)
                ? App.Text("Histories.AuthorBox.Add")
                : App.Text("Histories.AuthorBox.ChangeColor");
            title.IsEnabled = false;
            menu.Items.Add(title);

            foreach (var (key, hex) in Palette)
            {
                var item = new MenuItem();
                item.Header = App.Text($"Histories.AuthorBox.Color.{key}");
                item.Icon = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    Background = BrushFor(hex),
                    BorderBrush = BrushFor("#888888"),
                    BorderThickness = new Thickness(1),
                };
                if (string.Equals(current, hex, StringComparison.OrdinalIgnoreCase))
                    item.FontWeight = FontWeight.Bold;

                var chosen = hex;
                item.Click += (_, ev) =>
                {
                    prefs.SetAuthorBox(name, chosen);
                    ev.Handled = true;
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new MenuItem { Header = "-" });

            var remove = new MenuItem();
            remove.Header = App.Text("Histories.AuthorBox.Remove");
            remove.IsEnabled = !string.IsNullOrEmpty(current);
            remove.Click += (_, ev) =>
            {
                prefs.RemoveAuthorBox(name);
                ev.Handled = true;
            };
            menu.Items.Add(remove);
            menu.Open(owner);
        }

        private static IBrush BrushFor(string hex)
        {
            if (_brushes.TryGetValue(hex, out var brush))
                return brush;

            brush = new SolidColorBrush(Color.Parse(hex));
            _brushes[hex] = brush;
            return brush;
        }

        private readonly TextBlock _text;
        private static readonly Dictionary<string, IBrush> _brushes = new(StringComparer.OrdinalIgnoreCase);
    }
}
