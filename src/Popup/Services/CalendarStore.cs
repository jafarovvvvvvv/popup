using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Popup.Model;

namespace Popup.Services;

/// <summary>
/// Встречи на неделю вперёд.
///
/// На macOS оригинал берёт их из EventKit — общего системного календаря,
/// которого в Windows нет: у Outlook свой, у почтового приложения свой, а у
/// Google-календаря в браузере вообще никакого. Поэтому источник здесь один и
/// явный — Google Calendar, — и вход в него спрашивается только по нажатию
/// кнопки внутри вкладки, ровно как в оригинале.
/// </summary>
public sealed partial class CalendarStore : Observable
{
    public sealed record Meeting(
        string Id, string Title, DateTime Start, DateTime End, string? JoinUrl, bool AllDay)
    {
        public bool IsRunning => DateTime.Now >= Start && DateTime.Now < End;
    }

    private readonly GoogleAuth _auth;
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _clock;
    private static readonly HttpClient Http = new();

    public ObservableCollection<Meeting> Meetings { get; } = [];

    /// <summary>
    /// Ссылка на календарь задана — тогда вход через Google не нужен вовсе.
    /// </summary>
    public bool UsesIcs => !string.IsNullOrWhiteSpace(_settings.CalendarIcs);

    public bool IsConfigured => UsesIcs || _auth.IsConfigured;
    public bool IsMisconfigured => !UsesIcs && _auth.IsMisconfigured;

    /// <summary>Требуется ли нажать кнопку входа, чтобы что-то показать.</summary>
    public bool NeedsSignIn => !UsesIcs && !IsConnected;
    public bool IsConnected { get; private set; }
    public bool IsBusy { get; private set; }

    /// <summary>Ближайшая встреча — то, что показывает шапка.</summary>
    public Meeting? Next => Meetings.FirstOrDefault(m => m.End > DateTime.Now);

    /// <summary>Встречи одного дня, по порядку. События на весь день — первыми.</summary>
    public IEnumerable<Meeting> On(DateTime day)
    {
        var from = day.Date;
        var to = from.AddDays(1);
        return Meetings
            .Where(m => m.Start < to && m.End > from)
            .OrderByDescending(m => m.AllDay)
            .ThenBy(m => m.Start);
    }

    /// <summary>Есть ли в этот день хоть что-то — по этому под числом рисуется точка.</summary>
    public bool HasAnythingOn(DateTime day) => On(day).Any();

    // MARK: - Загруженный отрезок

    /// <summary>
    /// Какой отрезок времени уже прочитан.
    ///
    /// Сетка месяца листается, и каждый шаг может увести за пределы того, что
    /// загружено. Держать в памяти всю историю календаря незачем, а ходить в
    /// сеть на каждое нажатие стрелки — тем более: отрезок просто расширяется до
    /// нужного и перечитывается один раз.
    /// </summary>
    private DateTime _loadedFrom = DateTime.MaxValue;
    private DateTime _loadedTo = DateTime.MinValue;

    /// <summary>
    /// Убедиться, что этот отрезок прочитан. Расширяет загруженное окно с
    /// запасом, чтобы соседний месяц уже был на месте к моменту, когда до него
    /// долистают.
    /// </summary>
    /// <summary>
    /// Отрезок, который читаем сейчас. По умолчанию — месяц вокруг сегодняшнего
    /// дня: этого хватает и шапке с ближайшей встречей, и сетке, открытой на
    /// текущем месяце, ещё до того, как её начнут листать.
    /// </summary>
    private (DateTime From, DateTime To) Window()
    {
        var from = _loadedFrom == DateTime.MaxValue ? DateTime.Now.AddDays(-31) : _loadedFrom;
        var to = _loadedTo == DateTime.MinValue ? DateTime.Now.AddDays(31) : _loadedTo;
        _loadedFrom = from;
        _loadedTo = to;
        return (from, to);
    }

    public async Task EnsureRangeAsync(DateTime from, DateTime to)
    {
        if (from >= _loadedFrom && to <= _loadedTo) return;
        _loadedFrom = from < _loadedFrom ? from.AddDays(-31) : _loadedFrom;
        _loadedTo = to > _loadedTo ? to.AddDays(31) : _loadedTo;
        await ReloadAsync();
    }

    private readonly Settings _settings;

    public CalendarStore(Settings settings)
    {
        _settings = settings;
        _auth = new GoogleAuth(settings);

        // Раз в пять минут: встречи не появляются чаще, а каждый опрос — это
        // сетевой запрос от приложения, которое почти всё время свёрнуто.
        _refresh = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _refresh.Tick += (_, _) => _ = ReloadAsync();

        // Обратный отсчёт в шапке идёт только пока панель раскрыта.
        _clock = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(20),
        };
        _clock.Tick += (_, _) => RaiseAll();
    }

    /// <summary>
    /// Продолжает с того места, где остановились, только если вход уже был
    /// выдан раньше. Сама никогда ничего не спрашивает.
    /// </summary>
    public void Start()
    {
        IsConnected = _auth.IsConnected;
        // По ссылке спрашивать нечего и не у кого — сразу читаем.
        if (!UsesIcs && !IsConnected) return;
        _refresh.Start();
        _ = ReloadAsync();
    }

    public void Stop()
    {
        _refresh.Stop();
        _clock.Stop();
    }

    public void SetActive(bool active)
    {
        if (active) _clock.Start(); else _clock.Stop();
        if (active && (UsesIcs || IsConnected)) _ = ReloadAsync();
    }

    /// <summary>Единственная кнопка, которая просит разрешение, — и её нажимает человек.</summary>
    public async Task ConnectAsync()
    {
        IsBusy = true;
        RaiseAll();
        try
        {
            if (await _auth.ConnectAsync())
            {
                IsConnected = true;
                _refresh.Start();
                await ReloadAsync();
            }
        }
        finally
        {
            IsBusy = false;
            RaiseAll();
        }
    }

    public void Disconnect()
    {
        _auth.Disconnect();
        IsConnected = false;
        _refresh.Stop();
        Meetings.Clear();
        RaiseAll();
    }

    public void RefreshAccess()
    {
        var connected = _auth.IsConnected;
        if (connected == IsConnected) return;
        IsConnected = connected;
        RaiseAll();
    }

    // MARK: - Загрузка

    public async Task ReloadAsync()
    {
        if (UsesIcs)
        {
            await ReloadFromIcsAsync();
            return;
        }

        var token = await _auth.AccessTokenAsync();
        if (token is null)
        {
            IsConnected = false;
            RaiseAll();
            return;
        }

        try
        {
            var (windowFrom, windowTo) = Window();
            var from = windowFrom.ToUniversalTime();
            var to = windowTo.ToUniversalTime();
            var url = "https://www.googleapis.com/calendar/v3/calendars/primary/events" +
                      $"?timeMin={Uri.EscapeDataString(from.ToString("o", CultureInfo.InvariantCulture))}" +
                      $"&timeMax={Uri.EscapeDataString(to.ToString("o", CultureInfo.InvariantCulture))}" +
                      "&singleEvents=true&orderBy=startTime&maxResults=250";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Log.Error("calendar", $"запрос отклонён: {json}");
                return;
            }

            Meetings.Clear();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (Parse(item) is { } meeting) Meetings.Add(meeting);
                }
            }
            RaiseAll();
        }
        catch (Exception e)
        {
            Log.Error("calendar", "не загрузить встречи", e);
        }
    }

    /// <summary>
    /// Тот же список встреч, но из файла по ссылке. Ни входа, ни токенов, ни
    /// согласий — календарь сам выдал ссылку, и в ней уже есть доступ.
    /// </summary>
    private async Task ReloadFromIcsAsync()
    {
        if (_settings.CalendarIcsUrl is not { Length: > 0 } url) return;

        // webcal:// — та же ссылка, просто с другой схемой: так её отдаёт
        // Outlook, чтобы её подхватило системное приложение календаря.
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url["webcal://".Length..];

        try
        {
            var text = await Http.GetStringAsync(url);
            var (from, to) = Window();

            Meetings.Clear();
            foreach (var item in IcsCalendar.Parse(text, from, to))
            {
                Meetings.Add(new Meeting(
                    Id: $"{item.Uid}@{item.Start:O}",
                    Title: item.Summary,
                    Start: item.Start,
                    End: item.End,
                    JoinUrl: FindJoinUrl(item.Location, item.Description),
                    AllDay: item.AllDay));
            }
            IsConnected = true;
            RaiseAll();
        }
        catch (Exception e)
        {
            Log.Error("calendar", "не прочитать календарь по ссылке", e);
        }
    }

    private static Meeting? Parse(JsonElement item)
    {
        try
        {
            if (item.TryGetProperty("status", out var status) && status.GetString() == "cancelled") return null;

            var id = item.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");
            var title = item.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";

            var (start, allDay) = ReadTime(item, "start");
            var (end, _) = ReadTime(item, "end");
            if (start is null || end is null) return null;

            return new Meeting(id, title, start.Value, end.Value, FindJoinUrl(item), allDay);
        }
        catch
        {
            return null;
        }
    }

    private static (DateTime?, bool) ReadTime(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var node)) return (null, false);
        if (node.TryGetProperty("dateTime", out var dateTime) &&
            DateTime.TryParse(dateTime.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return (parsed.ToLocalTime(), false);
        }
        // Событие на весь день приходит без времени — только датой.
        if (node.TryGetProperty("date", out var date) &&
            DateTime.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return (day, true);
        }
        return (null, false);
    }

    /// <summary>
    /// Ссылка, по которой во встречу входят.
    ///
    /// Сначала родное поле <c>conferenceData</c> — там она лежит у Meet и у
    /// всего, что интегрировано с календарём. Дальше — разбор описания и места:
    /// ссылку на Zoom или Teams чаще всего просто вставляют текстом, и именно
    /// её приходится искать глазами за минуту до начала.
    /// </summary>
    private static string? FindJoinUrl(JsonElement item)
    {
        if (item.TryGetProperty("conferenceData", out var conference) &&
            conference.TryGetProperty("entryPoints", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.TryGetProperty("entryPointType", out var type) && type.GetString() == "video" &&
                    entry.TryGetProperty("uri", out var uri))
                {
                    return uri.GetString();
                }
            }
        }

        if (item.TryGetProperty("hangoutLink", out var hangout)) return hangout.GetString();

        return FindJoinUrl(
            item.TryGetProperty("location", out var location) ? location.GetString() : null,
            item.TryGetProperty("description", out var description) ? description.GetString() : null);
    }

    /// <summary>Разбор места и описания — общий для обоих источников встреч.</summary>
    private static string? FindJoinUrl(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (text is null) continue;
            var match = MeetingLink().Match(text);
            if (match.Success) return match.Value;
        }
        return null;
    }

    [GeneratedRegex(
        @"https?://(?:[\w-]+\.)*(?:zoom\.us|meet\.google\.com|teams\.microsoft\.com|teams\.live\.com|whereby\.com|discord\.gg|meet\.jit\.si|telemost\.yandex\.ru)/\S+",
        RegexOptions.IgnoreCase)]
    private static partial Regex MeetingLink();

    /// <summary>
    /// Обратный отсчёт для шапки: «через 12 мин», «идёт», «завтра».
    /// Точность здесь не нужна — нужен ответ на вопрос «я успеваю?».
    /// </summary>
    public static string Countdown(Meeting meeting)
    {
        if (meeting.IsRunning) return Strings.L("now");
        var left = meeting.Start - DateTime.Now;
        if (left.TotalMinutes < 1) return Strings.L("now");
        if (left.TotalMinutes < 60) return $"{(int)left.TotalMinutes} {Strings.L("min")}";
        if (left.TotalHours < 24) return $"{(int)left.TotalHours} {Strings.L("h")}";
        return $"{(int)left.TotalDays} {Strings.L("d")}";
    }
}
