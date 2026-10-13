using System.Globalization;
using System.Windows.Data;

namespace StartSequencer.UI;

// NumberBox.Value is double?; view model numbers are int or int?.
public sealed class NumberConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? (double)i : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var nullable = Nullable.GetUnderlyingType(targetType) != null;
        if (value is not double d || double.IsNaN(d)) return nullable ? null : Binding.DoNothing;
        return (int)Math.Round(d);
    }
}

// Group header binding: CollectionViewGroup.Name to the view model's StageGroup.
public sealed class GroupLookupConverter : IValueConverter
{
    public MainViewModel? ViewModel { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? ViewModel?.Group(name) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

// Column header sort glyph: values are the header text, the sorted column and whether it is descending.
public sealed class SortGlyphConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [string header, string column, bool descending] && header == column ? (descending ? "" : "") : "";

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}
