using System.Globalization;
using System.Text;

namespace Popup.Services;

/// <summary>
/// Разбор календаря в формате iCalendar — того, что лежит по «секретной
/// ссылке» Google, Outlook или Яндекса.
///
/// Существует как второй источник встреч, и не от бедности. У OAuth есть
/// стена, о которую упирается любой, кто делает приложение для себя одного:
/// доступ к календарю Google считает чувствительным, а значит приложение либо
/// проходит проверку, либо живёт в режиме тестирования со списком допущенных
/// адресов — и в этот список Google пускает не всякую почту. Панель для одного
/// человека не должна упираться в процедуру, придуманную для магазинов
/// приложений.
///
/// Ссылка на ICS обходит это целиком: её выдаёт сам календарь, она уже
/// содержит доступ, и никакого согласия ни у кого спрашивать не надо.
///
/// Разбирается намеренно не весь стандарт. Панель показывает неделю вперёд, и
/// ей нужны время, название, место и ссылка на встречу — плюс развёрнутые
/// повторы, потому что еженедельная планёрка иначе не покажется ни разу.
/// </summary>
public static class IcsCalendar
{
    /// <summary>Одно событие, как его понял разбор.</summary>
    public sealed record Event(
        string Uid,
        string Summary,
        DateTime Start,
        DateTime End,
        bool AllDay,
        string? Location,
        string? Description);

    /// <summary>
    /// Разбирает файл и возвращает события, попадающие в окно, — включая
    /// порождённые повторами.
    /// </summary>
    public static IReadOnlyList<Event> Parse(string text, DateTime from, DateTime to)
    {
        var events = new List<Event>();
        foreach (var block in Blocks(Unfold(text)))
        {
            if (Build(block) is not { } occurrence) continue;
            events.AddRange(Expand(occurrence, block, from, to));
        }
        return [.. events.OrderBy(e => e.Start)];
    }

    // MARK: - Разбор строк

    /// <summary>
    /// Склеивает перенесённые строки.
    ///
    /// В iCalendar длинная строка рвётся на части, и продолжение начинается с
    /// пробела или табуляции. Без склейки длинное название встречи приезжает
    /// разрезанным пополам.
    /// </summary>
    private static List<string> Unfold(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && lines.Count > 0)
                lines[^1] += line[1..];
            else
                lines.Add(line);
        }
        return lines;
    }

    /// <summary>Каждый блок — свойства одного VEVENT.</summary>
    private static IEnumerable<List<(string Name, string Parameters, string Value)>> Blocks(List<string> lines)
    {
        List<(string, string, string)>? current = null;

        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = [];
                continue;
            }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) yield return current;
                current = null;
                continue;
            }
            if (current is null) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var head = line[..colon];
            var value = line[(colon + 1)..];
            var semicolon = head.IndexOf(';');
            var name = semicolon < 0 ? head : head[..semicolon];
            var parameters = semicolon < 0 ? "" : head[(semicolon + 1)..];

            current.Add((name.ToUpperInvariant(), parameters, value));
        }
    }

    private static string? Value(List<(string Name, string Parameters, string Value)> block, string name) =>
        block.FirstOrDefault(p => p.Name == name).Value;

    private static string Parameters(List<(string Name, string Parameters, string Value)> block, string name) =>
        block.FirstOrDefault(p => p.Name == name).Parameters ?? "";

    private static Event? Build(List<(string Name, string Parameters, string Value)> block)
    {
        if (Value(block, "DTSTART") is not { Length: > 0 } rawStart) return null;

        var allDay = Parameters(block, "DTSTART").Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase)
                     || rawStart.Length == 8;
        if (ParseMoment(rawStart, allDay) is not { } start) return null;

        var rawEnd = Value(block, "DTEND");
        var end = rawEnd is { Length: > 0 } && ParseMoment(rawEnd, allDay) is { } parsedEnd
            ? parsedEnd
            // Без DTEND событие длится нисколько, а событие на весь день — сутки.
            : allDay ? start.AddDays(1) : start;

        return new Event(
            Uid: Value(block, "UID") ?? Guid.NewGuid().ToString("N"),
            Summary: Unescape(Value(block, "SUMMARY")) ?? "",
            Start: start,
            End: end,
            AllDay: allDay,
            Location: Unescape(Value(block, "LOCATION")),
            Description: Unescape(Value(block, "DESCRIPTION")));
    }

    /// <summary>
    /// Время из iCalendar в местное.
    ///
    /// Три записи: <c>20260808</c> — весь день, <c>20260808T090000Z</c> — по
    /// Гринвичу, <c>20260808T090000</c> — местное для календаря. Третье честно
    /// считаем местным здесь: без базы часовых поясов правильнее не выйдет, а
    /// у календаря, который человек смотрит на своей машине, пояс обычно свой.
    /// </summary>
    private static DateTime? ParseMoment(string value, bool allDay)
    {
        var text = value.Trim();

        if (allDay || text.Length == 8)
        {
            return DateTime.TryParseExact(text[..Math.Min(8, text.Length)], "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : null;
        }

        var utc = text.EndsWith('Z');
        var core = utc ? text[..^1] : text;
        if (!DateTime.TryParseExact(core, "yyyyMMddTHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
        {
            return null;
        }
        return utc ? DateTime.SpecifyKind(moment, DateTimeKind.Utc).ToLocalTime() : moment;
    }

    /// <summary>Экранирование текста: перевод строки, запятая, точка с запятой.</summary>
    private static string? Unescape(string? value)
    {
        if (value is null) return null;
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }
            i++;
            result.Append(value[i] switch
            {
                'n' or 'N' => '\n',
                _ => value[i],
            });
        }
        return result.ToString();
    }

    // MARK: - Повторы

    /// <summary>
    /// Разворачивает повторяющееся событие в отдельные встречи внутри окна.
    ///
    /// Поддержаны те повторы, которые реально встречаются в рабочем календаре:
    /// каждый день, каждую неделю по определённым дням, каждый месяц и каждый
    /// год — с шагом, ограничением по числу и по дате, и с вычетом отменённых
    /// дат. Более экзотические правила стандарта сюда не поместятся, и это
    /// честнее, чем разобрать их наполовину.
    /// </summary>
    private static IEnumerable<Event> Expand(
        Event template,
        List<(string Name, string Parameters, string Value)> block,
        DateTime from,
        DateTime to)
    {
        var rule = Value(block, "RRULE");
        if (string.IsNullOrWhiteSpace(rule))
        {
            if (template.End > from && template.Start < to) yield return template;
            yield break;
        }

        var parts = rule.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.OrdinalIgnoreCase);

        var frequency = parts.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        var interval = int.TryParse(parts.GetValueOrDefault("INTERVAL"), out var n) && n > 0 ? n : 1;
        var count = int.TryParse(parts.GetValueOrDefault("COUNT"), out var c) ? c : int.MaxValue;
        var until = parts.TryGetValue("UNTIL", out var rawUntil)
            ? ParseMoment(rawUntil, rawUntil.Length == 8)
            : null;

        var byDay = parts.GetValueOrDefault("BYDAY", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(WeekDay)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToHashSet();

        var excluded = block
            .Where(p => p.Name == "EXDATE")
            .SelectMany(p => p.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(v => ParseMoment(v, v.Length == 8))
            .Where(d => d is not null)
            .Select(d => d!.Value.Date)
            .ToHashSet();

        var length = template.End - template.Start;
        var occurrence = template.Start;
        var produced = 0;

        // Потолок на всякий случай: повтор без конца и без счётчика — обычное
        // дело, а окно всего в неделю.
        for (var guard = 0; guard < 4000 && produced < count; guard++)
        {
            if (occurrence > to) break;
            if (until is { } limit && occurrence > limit) break;

            var matchesDay = byDay.Count == 0 || byDay.Contains(occurrence.DayOfWeek);
            if (matchesDay && !excluded.Contains(occurrence.Date))
            {
                produced++;
                var end = occurrence + length;
                if (end > from && occurrence < to)
                {
                    yield return template with { Start = occurrence, End = end };
                }
            }

            occurrence = frequency switch
            {
                "DAILY" => occurrence.AddDays(interval),
                // По дням недели шагаем сутками, а интервал в неделях
                // отрабатывает сам собой на границе недели.
                "WEEKLY" when byDay.Count > 0 => occurrence.AddDays(1),
                "WEEKLY" => occurrence.AddDays(7 * interval),
                "MONTHLY" => occurrence.AddMonths(interval),
                "YEARLY" => occurrence.AddYears(interval),
                _ => to.AddDays(1),
            };
        }
    }

    private static DayOfWeek? WeekDay(string value)
    {
        // Перед днём может стоять порядковый номер: «2MO» — второй понедельник.
        var code = new string([.. value.Where(char.IsLetter)]).ToUpperInvariant();
        return code switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => null,
        };
    }
}
