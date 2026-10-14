using Popup.Services;

namespace Popup.Tests;

/// <summary>
/// Календарь по ссылке разбирает чужой файл, который никто не обещал писать
/// аккуратно: переносы посреди названия, экранирование, отменённые даты,
/// повторы без конца. Всё это встречается в обычном рабочем календаре.
/// </summary>
public class IcsCalendarTests
{
    private static readonly DateTime Monday = new(2026, 8, 3);
    private static DateTime From => Monday;
    private static DateTime To => Monday.AddDays(7);

    private static string Wrap(string body) =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\n{body}\r\nEND:VCALENDAR\r\n";

    private static IReadOnlyList<IcsCalendar.Event> Parse(string body, DateTime? from = null, DateTime? to = null) =>
        IcsCalendar.Parse(Wrap(body), from ?? From, to ?? To);

    [Fact]
    public void SimpleEventIsRead()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Планёрка
            DTSTART:20260804T090000
            DTEND:20260804T093000
            END:VEVENT
            """);

        var meeting = Assert.Single(events);
        Assert.Equal("Планёрка", meeting.Summary);
        Assert.Equal(new DateTime(2026, 8, 4, 9, 0, 0), meeting.Start);
        Assert.Equal(new DateTime(2026, 8, 4, 9, 30, 0), meeting.End);
        Assert.False(meeting.AllDay);
    }

    /// <summary>
    /// Длинная строка в iCalendar рвётся, и продолжение начинается с пробела.
    /// Без склейки название приезжает разрезанным.
    /// </summary>
    [Fact]
    public void FoldedLinesAreJoined()
    {
        var events = Parse("BEGIN:VEVENT\r\nUID:a1\r\nSUMMARY:Очень длинное назв\r\n ание встречи\r\n" +
                           "DTSTART:20260804T090000\r\nDTEND:20260804T093000\r\nEND:VEVENT");

        Assert.Equal("Очень длинное название встречи", Assert.Single(events).Summary);
    }

    [Fact]
    public void EscapedCharactersAreDecoded()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Обсудить A\, B и C
            DESCRIPTION:Первая строка\nВторая строка
            DTSTART:20260804T090000
            DTEND:20260804T093000
            END:VEVENT
            """);

        var meeting = Assert.Single(events);
        Assert.Equal("Обсудить A, B и C", meeting.Summary);
        Assert.Equal("Первая строка\nВторая строка", meeting.Description);
    }

    [Fact]
    public void AllDayEventCoversTheWholeDay()
    {
        var meeting = Assert.Single(Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Отпуск
            DTSTART;VALUE=DATE:20260805
            DTEND;VALUE=DATE:20260806
            END:VEVENT
            """));

        Assert.True(meeting.AllDay);
        Assert.Equal(new DateTime(2026, 8, 5), meeting.Start);
    }

    /// <summary>Время по Гринвичу должно приехать в местное.</summary>
    [Fact]
    public void UtcMomentsBecomeLocal()
    {
        var meeting = Assert.Single(Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Звонок
            DTSTART:20260804T090000Z
            DTEND:20260804T093000Z
            END:VEVENT
            """));

        var expected = DateTime.SpecifyKind(new DateTime(2026, 8, 4, 9, 0, 0), DateTimeKind.Utc).ToLocalTime();
        Assert.Equal(expected, meeting.Start);
    }

    [Fact]
    public void EventsOutsideTheWindowAreDropped() =>
        Assert.Empty(Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Далеко
            DTSTART:20261201T090000
            DTEND:20261201T093000
            END:VEVENT
            """));

    /// <summary>Встреча, которая уже идёт, из окна выпадать не должна.</summary>
    [Fact]
    public void RunningEventIsKept()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Идёт
            DTSTART:20260803T090000
            DTEND:20260803T110000
            END:VEVENT
            """, from: new DateTime(2026, 8, 3, 10, 0, 0), to: To);

        Assert.Single(events);
    }

    // MARK: - Повторы

    [Fact]
    public void DailyRepeatFillsTheWeek()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Каждый день
            DTSTART:20260803T090000
            DTEND:20260803T091500
            RRULE:FREQ=DAILY
            END:VEVENT
            """);

        Assert.Equal(7, events.Count);
        Assert.All(events, e => Assert.Equal(9, e.Start.Hour));
    }

    [Fact]
    public void WeeklyByDayPicksOnlyThoseDays()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Пн и Ср
            DTSTART:20260803T100000
            DTEND:20260803T103000
            RRULE:FREQ=WEEKLY;BYDAY=MO,WE
            END:VEVENT
            """);

        Assert.Equal(2, events.Count);
        Assert.Equal(DayOfWeek.Monday, events[0].Start.DayOfWeek);
        Assert.Equal(DayOfWeek.Wednesday, events[1].Start.DayOfWeek);
    }

    [Fact]
    public void IntervalIsRespected()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Через день
            DTSTART:20260803T090000
            DTEND:20260803T091500
            RRULE:FREQ=DAILY;INTERVAL=2
            END:VEVENT
            """);

        Assert.Equal(4, events.Count);
    }

    [Fact]
    public void CountLimitsTheSeries() =>
        Assert.Equal(3, Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Трижды
            DTSTART:20260803T090000
            DTEND:20260803T091500
            RRULE:FREQ=DAILY;COUNT=3
            END:VEVENT
            """).Count);

    [Fact]
    public void UntilStopsTheSeries() =>
        Assert.Equal(3, Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:До среды
            DTSTART:20260803T090000
            DTEND:20260803T091500
            RRULE:FREQ=DAILY;UNTIL=20260805T235959Z
            END:VEVENT
            """).Count);

    /// <summary>Отменённая встреча из ряда исчезает, а не сдвигает остальные.</summary>
    [Fact]
    public void ExcludedDatesDisappear()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Каждый день
            DTSTART:20260803T090000
            DTEND:20260803T091500
            RRULE:FREQ=DAILY
            EXDATE:20260805T090000
            END:VEVENT
            """);

        Assert.Equal(6, events.Count);
        Assert.DoesNotContain(events, e => e.Start.Date == new DateTime(2026, 8, 5));
    }

    /// <summary>
    /// Повтор без конца и без счётчика — обычное дело, и он не должен ни
    /// зациклить разбор, ни выдать бесконечный список.
    /// </summary>
    [Fact]
    public void EndlessRepeatStaysInsideTheWindow() =>
        Assert.Equal(7, Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Навсегда
            DTSTART:20260803T090000
            DTEND:20260803T091500
            RRULE:FREQ=DAILY
            END:VEVENT
            """).Count);

    // MARK: - Кривой ввод

    [Theory]
    [InlineData("")]
    [InlineData("совсем не календарь")]
    [InlineData("BEGIN:VCALENDAR\r\nEND:VCALENDAR")]
    public void NonsenseYieldsNothing(string text) =>
        Assert.Empty(IcsCalendar.Parse(text, From, To));

    /// <summary>Событие без времени начала — не событие, но и не повод упасть.</summary>
    [Fact]
    public void EventWithoutStartIsSkipped() =>
        Assert.Empty(Parse("""
            BEGIN:VEVENT
            UID:a1
            SUMMARY:Без времени
            END:VEVENT
            """));

    /// <summary>Одно испорченное событие не должно уносить с собой соседние.</summary>
    [Fact]
    public void OneBrokenEventDoesNotLoseTheOthers()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:bad
            SUMMARY:Сломано
            DTSTART:не-дата
            END:VEVENT
            BEGIN:VEVENT
            UID:good
            SUMMARY:Целое
            DTSTART:20260804T090000
            DTEND:20260804T093000
            END:VEVENT
            """);

        Assert.Equal("Целое", Assert.Single(events).Summary);
    }

    /// <summary>Встречи приезжают отсортированными по времени.</summary>
    [Fact]
    public void EventsComeBackInOrder()
    {
        var events = Parse("""
            BEGIN:VEVENT
            UID:b
            SUMMARY:Позже
            DTSTART:20260805T150000
            DTEND:20260805T153000
            END:VEVENT
            BEGIN:VEVENT
            UID:a
            SUMMARY:Раньше
            DTSTART:20260804T090000
            DTEND:20260804T093000
            END:VEVENT
            """);

        Assert.Equal(["Раньше", "Позже"], events.Select(e => e.Summary));
    }
}
