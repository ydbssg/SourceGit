using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class FilterModeConverters
    {
        public static readonly FuncValueConverter<Models.FilterMode, IBrush> ToBorderBrush =
            new FuncValueConverter<Models.FilterMode, IBrush>(v =>
            {
                return v switch
                {
                    Models.FilterMode.Included or Models.FilterMode.VisibleInHiddenFolder => Brushes.Green,
                    Models.FilterMode.Excluded => Brushes.Red,
                    _ => Brushes.Transparent,
                };
            });
    }
}
