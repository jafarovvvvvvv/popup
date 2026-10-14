using System.Text.Json;
using System.Text.Json.Serialization;

namespace Popup.Services;

/// <summary>
/// Настройки приложения. Обычный JSON в <c>%APPDATA%\Popup\settings.json</c> —
/// правится руками так же охотно, как из трея.
/// </summary>
public sealed class Settings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Файл правят руками, и правят по документации, где ключи написаны с
        // маленькой буквы. Без этой пары строк такой ключ молча не подхватится:
        // .NET по умолчанию пишет с большой и читает точным совпадением, так
        // что правка «по инструкции» просто ничего бы не меняла.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Одна лишняя запятая или неизвестный ключ не должны стоить всех
        // настроек: файл живой, его дописывают на ходу.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Иначе плюс в «ctrl+alt+space» уезжает в +, а слэши в адресах —
        // в \/. Формально верно, читать невозможно.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Сохранять ли картинку из буфера как файл на полке.
    /// По умолчанию включено: ради этого папка со скриншотами и заведена.
    /// </summary>
    public bool SaveClipboardImages { get; set; } = true;

    /// <summary>Разделы, содержимое которых закрыто от чужих глаз.</summary>
    public List<string> PrivacySections { get; set; } = [];

    /// <summary>Язык интерфейса: <c>ru</c>, <c>en</c> или <c>null</c> — как в системе.</summary>
    public string? Language { get; set; }

    public bool RunAtStartup { get; set; }

    /// <summary>
    /// Показывали ли знакомство. Отдельный флаг, а не «есть ли файл настроек»:
    /// файл можно удалить, разбираясь с чем-то другим, и приветствие не должно
    /// возвращаться как ни в чём не бывало.
    /// </summary>
    public bool OnboardingShown { get; set; }

    /// <summary>
    /// Клавиша, показывающая панель, когда руки уже на клавиатуре.
    /// Запись как у LocalWhisper: <c>ctrl+alt+space</c>. Пусто — без клавиши.
    /// </summary>
    public string? ToggleHotkey { get; set; } = "ctrl+alt+space";

    // MARK: - Переводчик

    /// <summary><c>openai</c> или <c>deepl</c>.</summary>
    public string TranslateProvider { get; set; } = "openai";
    public string TranslateEndpoint { get; set; } = "https://api.openai.com/v1";
    public string TranslateModel { get; set; } = "gpt-5.4-mini";

    /// <summary>
    /// Сюда ключ вписывают руками — и он тут не задерживается: при первом же
    /// чтении переезжает в <see cref="TranslateApiKeyEnc"/> под DPAPI, а
    /// отсюда стирается. См. <see cref="AdoptPlainSecrets"/>.
    /// </summary>
    public string? TranslateApiKey { get; set; }

    /// <summary>Ключ под DPAPI — см. <see cref="Secrets"/>.</summary>
    public string? TranslateApiKeyEnc { get; set; }

    /// <summary>Расшифрованный ключ. В файл не пишется никогда.</summary>
    [JsonIgnore]
    public string? TranslateKey => Secrets.Unprotect(TranslateApiKeyEnc);

    // MARK: - Календарь

    /// <summary>
    /// Секретная ссылка на календарь в формате iCalendar — вписывается сюда.
    ///
    /// Самый короткий путь к встречам и путь по умолчанию: ссылку выдаёт сам
    /// календарь — Google, Outlook, Яндекс, — она уже содержит доступ, и
    /// никакого приложения в Google Cloud заводить не надо. Если она задана,
    /// вход через Google не используется вовсе.
    ///
    /// Именно потому, что доступ уже в ней, она и есть пароль: кто знает
    /// ссылку — читает календарь. Поэтому здесь она не задерживается, как и
    /// остальные секреты, а переезжает под DPAPI при первом чтении.
    /// </summary>
    public string? CalendarIcsUrl { get; set; }

    public string? CalendarIcsUrlEnc { get; set; }

    /// <summary>Расшифрованная ссылка. В файл не пишется никогда.</summary>
    [JsonIgnore]
    public string? CalendarIcs => Secrets.Unprotect(CalendarIcsUrlEnc);

    /// <summary>
    /// OAuth client id десктопного приложения из Google Cloud.
    /// Запасной путь: см. <c>docs/google-calendar.md</c>.
    /// </summary>
    public string? GoogleClientId { get; set; }

    /// <summary>
    /// Client secret того же приложения — вписывается руками сюда. Google
    /// требует его при обмене кода даже в loopback-потоке и сам же
    /// документирует, что для десктопного клиента он секретом не является. Всё
    /// равно не остаётся открытым: при первом чтении переезжает под DPAPI, а
    /// отсюда стирается. Файл настроек открывают и показывают чаще, чем думают.
    /// </summary>
    public string? GoogleClientSecret { get; set; }

    public string? GoogleClientSecretEnc { get; set; }

    /// <summary>Расшифрованный секрет. В файл не пишется никогда.</summary>
    [JsonIgnore]
    public string? GoogleSecret => Secrets.Unprotect(GoogleClientSecretEnc);

    /// <summary>
    /// Забирает вписанные руками секреты под DPAPI и стирает открытый текст.
    ///
    /// Без этого шага документация врала бы: там сказано «впиши ключ в
    /// settings.json», а поле, помеченное как незаписываемое, молча
    /// игнорировалось бы при чтении — ключ вписан, и ничего не работает.
    ///
    /// Возвращает <c>true</c>, если файл пора переписать.
    /// </summary>
    public bool AdoptPlainSecrets()
    {
        var changed = false;

        // Заодно чиним то, что человек мог вписать с промахом. Молча и в самом
        // файле: показать в панели «у тебя лишний суффикс» можно, но починить
        // и не тревожить — лучше.
        var normalizedId = GoogleClient.NormalizeId(GoogleClientId);
        if (normalizedId != GoogleClientId)
        {
            GoogleClientId = normalizedId;
            changed = true;
        }

        if (GoogleClientSecret is { Length: > 0 } google)
        {
            GoogleClientSecretEnc = Secrets.Protect(google);
            GoogleClientSecret = null;
            changed = true;
        }

        if (TranslateApiKey is { Length: > 0 } key)
        {
            TranslateApiKeyEnc = Secrets.Protect(key);
            TranslateApiKey = null;
            changed = true;
        }

        if (CalendarIcsUrl is { Length: > 0 } ics)
        {
            CalendarIcsUrlEnc = Secrets.Protect(ics.Trim());
            CalendarIcsUrl = null;
            changed = true;
        }

        return changed;
    }

    // MARK: - Загрузка и сохранение

    /// <summary>
    /// Когда файл в последний раз писала сама панель. По этому отличается
    /// собственная запись от правки руками — иначе наблюдатель за файлом
    /// зациклился бы на своём же сохранении.
    /// </summary>
    [JsonIgnore]
    public DateTime LastSelfWrite { get; private set; }

    public static Settings Load()
    {
        var settings = ReadFile();
        if (settings.AdoptPlainSecrets()) settings.Save();
        return settings;
    }

    /// <summary>
    /// Разобрать настройки из строки. Открыто наружу ради тестов: формат файла
    /// — то, что человек правит руками, и его послабления должны проверяться
    /// сами, а не выясняться потерянной правкой.
    /// </summary>
    public static Settings FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
        }
        catch (JsonException)
        {
            // Сломанный файл — это настройки по умолчанию. Панель, не
            // запустившаяся из-за лишней запятой, хуже панели с обычными
            // настройками.
            return new Settings();
        }
    }

    /// <summary>Как настройки лягут в файл.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static Settings ReadFile()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
            {
                var text = File.ReadAllText(AppPaths.Settings);
                return JsonSerializer.Deserialize<Settings>(text, JsonOptions) ?? new Settings();
            }
        }
        catch (Exception e)
        {
            // Файл правится руками, а значит рано или поздно будет сломан. Своя
            // панель, вернувшаяся к настройкам по умолчанию, полезнее диалога
            // с ошибкой при старте — но сказать об этом в журнал надо, иначе
            // сломанная запятая выглядит как «настройки не применяются».
            Log.Error("settings", "settings.json не читается", e);
        }
        return new Settings();
    }

    /// <summary>
    /// Перечитывает файл в этот же объект.
    ///
    /// Именно в этот, а не в новый: ссылку на настройки держат переводчик,
    /// вход в Google, трей и хранилища, и подменять её пришлось бы всем сразу.
    /// Возвращает <c>true</c>, если что-то действительно изменилось.
    /// </summary>
    public bool ReloadInPlace()
    {
        var fresh = ReadFile();
        var before = JsonSerializer.Serialize(this, JsonOptions);

        SaveClipboardImages = fresh.SaveClipboardImages;
        PrivacySections = fresh.PrivacySections;
        Language = fresh.Language;
        RunAtStartup = fresh.RunAtStartup;
        ToggleHotkey = fresh.ToggleHotkey;
        OnboardingShown = fresh.OnboardingShown;

        TranslateProvider = fresh.TranslateProvider;
        TranslateEndpoint = fresh.TranslateEndpoint;
        TranslateModel = fresh.TranslateModel;
        TranslateApiKey = fresh.TranslateApiKey;
        TranslateApiKeyEnc = fresh.TranslateApiKeyEnc;

        CalendarIcsUrl = fresh.CalendarIcsUrl;
        CalendarIcsUrlEnc = fresh.CalendarIcsUrlEnc;
        GoogleClientId = fresh.GoogleClientId;
        GoogleClientSecret = fresh.GoogleClientSecret;
        GoogleClientSecretEnc = fresh.GoogleClientSecretEnc;

        // Вписанный руками ключ прячем сразу же, не дожидаясь следующего
        // сохранения по другому поводу.
        if (AdoptPlainSecrets()) Save();

        return JsonSerializer.Serialize(this, JsonOptions) != before;
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        LastSelfWrite = DateTime.UtcNow;
        AppPaths.WriteAtomic(AppPaths.Settings, JsonSerializer.Serialize(this, JsonOptions));
    }
}
