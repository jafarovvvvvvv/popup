using System.Globalization;

namespace Popup.Services;

/// <summary>
/// Раскладка месяца по клеткам — арифметика, отделённая от рисования.
///
/// Отделена потому, что здесь легко ошибиться незаметно: неделя начинается с
/// понедельника или с воскресенья в зависимости от языка, месяц начинается
/// посреди недели, и в него влезает то пять недель, то шесть. Ошибка сдвигает
/// весь месяц на клетку — картинка при этом выглядит правдоподобно, и заметить
/// это можно только по числу под пальцем.
/// </summary>
public static class MonthGrid
{
    /// <summary>Сколько недель показывает сетка. Всегда шесть — чтобы она не прыгала по высоте.</summary>
    public const int Weeks = 6;
    public const int Days = Weeks * 7;

    /// <summary>
    /// Первая клетка сетки: понедельник (или воскресенье — как принято в языке)
    /// той недели, в которую попадает первое число месяца.
    /// </summary>
    public static DateTime FirstCell(DateTime month, CultureInfo culture)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var weekStart = culture.DateTimeFormat.FirstDayOfWeek;
        var shift = ((int)first.DayOfWeek - (int)weekStart + 7) % 7;
        return first.AddDays(-shift);
    }

    /// <summary>Все 42 дня сетки подряд, начиная с первой клетки.</summary>
    public static IEnumerable<DateTime> Cells(DateTime month, CultureInfo culture)
    {
        var day = FirstCell(month, culture);
        for (var i = 0; i < Days; i++) yield return day.AddDays(i);
    }

    /// <summary>
    /// Подписи дней недели в том же порядке, что и клетки: «пн вт ср…».
    /// </summary>
    public static IReadOnlyList<string> WeekdayNames(CultureInfo culture)
    {
        var weekStart = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var names = culture.DateTimeFormat.AbbreviatedDayNames;
        return [.. Enumerable.Range(0, 7).Select(i => names[(weekStart + i) % 7])];
    }

    /// <summary>«август 2026» — заголовок сетки.</summary>
    public static string Title(DateTime month, CultureInfo culture) =>
        culture.TextInfo.ToTitleCase(month.ToString("MMMM yyyy", culture));

    /// <summary>«вторник, 4 августа» — подпись выбранного дня.</summary>
    public static string DayTitle(DateTime day, CultureInfo culture) =>
        culture.TextInfo.ToTitleCase(day.ToString("dddd, d MMMM", culture));
}
