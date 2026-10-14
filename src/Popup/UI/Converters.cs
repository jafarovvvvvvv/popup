using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Popup.UI;

/// <summary>Имя иконки из <c>Icons.xaml</c> — в саму фигуру.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && Application.Current?.TryFindResource(key) is Geometry geometry)
            return geometry;
        return Geometry.Empty;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Пусто ли — для подписей «здесь ничего нет».</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    /// <summary>True — показывать, когда пусто; false — когда есть что показать.</summary>
    public bool WhenEmpty { get; set; } = true;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int n => n == 0,
            System.Collections.ICollection c => c.Count == 0,
            _ => false,
        };
        return empty == WhenEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}

/// <summary>Счётчик в шапке: ноль не показываем вовсе.</summary>
public sealed class CountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int n => n,
            System.Collections.ICollection c => c.Count,
            _ => 0,
        };
        return count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c) =>
        throw new NotSupportedException();
}
