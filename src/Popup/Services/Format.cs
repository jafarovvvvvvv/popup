using System.Globalization;

namespace Popup.Services;

/// <summary>Мелкое форматирование, общее для нескольких вкладок.</summary>
public static class Format
{
    /// <summary>
    /// Время на полоске перемотки. Часы появляются только у длинных записей —
    /// лишний ноль в начале трёхминутного трека занимает место, которого в
    /// колонке шириной в 32 точки нет.
    /// </summary>
    public static string Time(TimeSpan value)
    {
        if (value < TimeSpan.Zero || value == TimeSpan.MaxValue) return "--:--";
        var total = (int)Math.Round(value.TotalSeconds);
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        return hours > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", minutes, seconds);
    }
}
