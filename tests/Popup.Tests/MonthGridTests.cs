using System.Globalization;
using Popup.Services;

namespace Popup.Tests;

/// <summary>
/// Раскладка месяца — то место, где ошибка не бросается в глаза: сетка со
/// сдвигом на одну клетку выглядит совершенно правдоподобно, и заметить её
/// можно только по числу под пальцем.
/// </summary>
public class MonthGridTests
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    /// <summary>1 августа 2026 — суббота. Русская неделя начинается с понедельника.</summary>
    [Fact]
    public void RussianWeekStartsOnMonday()
    {
        var first = MonthGrid.FirstCell(new DateTime(2026, 8, 15), Ru);
        Assert.Equal(new DateTime(2026, 7, 27), first);
        Assert.Equal(DayOfWeek.Monday, first.DayOfWeek);
    }

    /// <summary>А американская — с воскресенья, и сетка сдвигается на день.</summary>
    [Fact]
    public void AmericanWeekStartsOnSunday()
    {
        var first = MonthGrid.FirstCell(new DateTime(2026, 8, 15), En);
        Assert.Equal(new DateTime(2026, 7, 26), first);
        Assert.Equal(DayOfWeek.Sunday, first.DayOfWeek);
    }

    /// <summary>
    /// Месяц, который сам начинается в понедельник, не должен получать пустую
    /// неделю сверху.
    /// </summary>
    [Fact]
    public void MonthStartingOnMondayNeedsNoLeadingWeek()
    {
        // 1 июня 2026 — понедельник.
        Assert.Equal(new DateTime(2026, 6, 1), MonthGrid.FirstCell(new DateTime(2026, 6, 10), Ru));
    }

    [Fact]
    public void GridIsAlwaysSixWeeks()
    {
        foreach (var month in Enumerable.Range(1, 12).Select(m => new DateTime(2026, m, 1)))
        {
            var cells = MonthGrid.Cells(month, Ru).ToList();
            Assert.Equal(42, cells.Count);
            // И все дни месяца обязаны в неё попасть — иначе последние числа
            // просто исчезнут с экрана.
            var days = DateTime.DaysInMonth(month.Year, month.Month);
            Assert.Contains(new DateTime(month.Year, month.Month, 1), cells);
            Assert.Contains(new DateTime(month.Year, month.Month, days), cells);
        }
    }

    [Fact]
    public void CellsRunWithoutGaps()
    {
        var cells = MonthGrid.Cells(new DateTime(2026, 8, 1), Ru).ToList();
        for (var i = 1; i < cells.Count; i++)
            Assert.Equal(cells[i - 1].AddDays(1), cells[i]);
    }

    /// <summary>Февраль високосного года — привычное место для ошибки на день.</summary>
    [Fact]
    public void LeapFebruaryFits()
    {
        var cells = MonthGrid.Cells(new DateTime(2028, 2, 1), Ru).ToList();
        Assert.Contains(new DateTime(2028, 2, 29), cells);
    }

    [Fact]
    public void WeekdayNamesFollowTheSameOrderAsCells()
    {
        var names = MonthGrid.WeekdayNames(Ru);
        Assert.Equal(7, names.Count);
        Assert.StartsWith("пн", names[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("вс", names[6], StringComparison.OrdinalIgnoreCase);

        Assert.StartsWith("sun", MonthGrid.WeekdayNames(En)[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TitlesAreReadable()
    {
        Assert.Equal("Август 2026", MonthGrid.Title(new DateTime(2026, 8, 1), Ru));
        Assert.Contains("4 Августа", MonthGrid.DayTitle(new DateTime(2026, 8, 4), Ru), StringComparison.Ordinal);
    }
}

/// <summary>Выборка встреч по дню — то, что показывает правая колонка.</summary>
public class CalendarDayTests
{
    private static CalendarStore.Meeting Meeting(
        string id, DateTime start, DateTime end, bool allDay = false) =>
        new(id, id, start, end, null, allDay);

    private static CalendarStore Store(params CalendarStore.Meeting[] meetings)
    {
        var store = new CalendarStore(new Settings());
        foreach (var meeting in meetings) store.Meetings.Add(meeting);
        return store;
    }

    [Fact]
    public void OnlyThatDayComesBack()
    {
        var store = Store(
            Meeting("вчера", new DateTime(2026, 8, 3, 10, 0, 0), new DateTime(2026, 8, 3, 11, 0, 0)),
            Meeting("сегодня", new DateTime(2026, 8, 4, 10, 0, 0), new DateTime(2026, 8, 4, 11, 0, 0)),
            Meeting("завтра", new DateTime(2026, 8, 5, 10, 0, 0), new DateTime(2026, 8, 5, 11, 0, 0)));

        Assert.Equal("сегодня", Assert.Single(store.On(new DateTime(2026, 8, 4))).Id);
    }

    /// <summary>
    /// Встреча, начавшаяся вчера вечером и кончающаяся сегодня утром,
    /// принадлежит обоим дням.
    /// </summary>
    [Fact]
    public void OvernightMeetingBelongsToBothDays()
    {
        var store = Store(Meeting("ночная",
            new DateTime(2026, 8, 4, 23, 0, 0), new DateTime(2026, 8, 5, 1, 0, 0)));

        Assert.Single(store.On(new DateTime(2026, 8, 4)));
        Assert.Single(store.On(new DateTime(2026, 8, 5)));
    }

    /// <summary>События на весь день идут первыми — они задают рамку дню.</summary>
    [Fact]
    public void AllDayEventsComeFirst()
    {
        var store = Store(
            Meeting("встреча", new DateTime(2026, 8, 4, 9, 0, 0), new DateTime(2026, 8, 4, 10, 0, 0)),
            Meeting("отпуск", new DateTime(2026, 8, 4), new DateTime(2026, 8, 5), allDay: true));

        Assert.Equal(["отпуск", "встреча"], store.On(new DateTime(2026, 8, 4)).Select(m => m.Id));
    }

    [Fact]
    public void DotAppearsOnlyOnDaysWithSomething()
    {
        var store = Store(Meeting("а",
            new DateTime(2026, 8, 4, 9, 0, 0), new DateTime(2026, 8, 4, 10, 0, 0)));

        Assert.True(store.HasAnythingOn(new DateTime(2026, 8, 4)));
        Assert.False(store.HasAnythingOn(new DateTime(2026, 8, 5)));
    }
}
