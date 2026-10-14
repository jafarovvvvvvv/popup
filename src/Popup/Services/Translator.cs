using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Popup.Model;

namespace Popup.Services;

/// <summary>
/// Перевод между русским и английским.
///
/// На macOS оригинал берёт системный Translation.framework и работает без сети.
/// В Windows такого нет: встроенный переводчик появился только в Windows 11 на
/// машинах с NPU, и рассчитывать на него нельзя. Поэтому здесь провайдер —
/// внешний и сменный: OpenAI-совместимый endpoint (в том числе локальная
/// Ollama, если направить <c>translateEndpoint</c> на неё) или DeepL.
///
/// Направление определяется по алфавиту введённого текста, как в оригинале:
/// выбирать язык руками перед каждым переводом — лишнее движение в панели,
/// которая живёт ровно столько, сколько на ней курсор.
/// </summary>
public sealed class Translator : Observable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Settings _settings;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _inflight;

    public Translator(Settings settings)
    {
        _settings = settings;
        // Перевод — сетевой запрос, и слать его на каждую букву значит платить
        // за фразу столько раз, сколько в ней символов.
        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(0.6),
        };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = TranslateAsync(); };
    }

    private string _source = "";
    public string Source
    {
        get => _source;
        set
        {
            if (!Set(ref _source, value)) return;
            Raise(nameof(FromLanguage));
            Raise(nameof(ToLanguage));
            if (value.Trim().Length == 0)
            {
                _debounce.Stop();
                Result = "";
                return;
            }
            _debounce.Stop();
            _debounce.Start();
        }
    }

    private string _result = "";
    public string Result
    {
        get => _result;
        private set => Set(ref _result, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>
    /// Направление по алфавиту: кириллица — значит с русского. Порог в одну
    /// букву не годится — в русском тексте легко попадается латинское
    /// название, — поэтому сравнивается, каких букв больше.
    /// </summary>
    public bool IsRussianSource
    {
        get
        {
            var cyrillic = _source.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
            var latin = _source.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
            return cyrillic > latin;
        }
    }

    public string FromLanguage => IsRussianSource ? "RU" : "EN";
    public string ToLanguage => IsRussianSource ? "EN" : "RU";

    public bool HasKey => _settings.TranslateKey is { Length: > 0 };

    /// <summary>
    /// Ключ, введённый в самой панели. Идёт тем же путём, что и вписанный в
    /// settings.json: открытым текстом в поле, затем под DPAPI.
    /// </summary>
    public void SetKey(string key)
    {
        key = key.Trim();
        if (key.Length == 0) return;
        _settings.TranslateApiKey = key;
        _settings.AdoptPlainSecrets();
        _settings.Save();
        Error = null;
        Raise(nameof(HasKey));
        // Текст мог быть набран до ключа — переводим его сразу, а не ждём правки.
        _debounce.Stop();
        _ = TranslateAsync();
    }

    public void Clear()
    {
        _debounce.Stop();
        Source = "";
        Result = "";
        Error = null;
    }

    // MARK: - Запрос

    private async Task TranslateAsync()
    {
        var text = _source.Trim();
        if (text.Length == 0) return;

        // Прошлый запрос отменяется: пока он летел, текст уже другой, и его
        // ответ перезаписал бы поле тем, что человек успел стереть.
        _inflight?.Cancel();
        var cancellation = new CancellationTokenSource();
        _inflight = cancellation;

        IsBusy = true;
        Error = null;
        try
        {
            var translated = _settings.TranslateProvider.Equals("deepl", StringComparison.OrdinalIgnoreCase)
                ? await DeepLAsync(text, cancellation.Token)
                : await OpenAiAsync(text, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            Result = translated;
        }
        catch (OperationCanceledException)
        {
            // Отменён своим же следующим нажатием — не ошибка.
        }
        catch (Exception e)
        {
            Log.Error("translate", "перевод не удался", e);
            Error = e.Message;
        }
        finally
        {
            if (ReferenceEquals(_inflight, cancellation)) IsBusy = false;
        }
    }

    private async Task<string> OpenAiAsync(string text, CancellationToken cancellation)
    {
        if (_settings.TranslateKey is not { Length: > 0 } key)
            throw new InvalidOperationException(Strings.L("Add a translation key with the key button"));

        var target = IsRussianSource ? "English" : "Russian";
        var payload = new
        {
            model = _settings.TranslateModel,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = $"Translate the user's text into {target}. " +
                              "Return only the translation, with no quotes, notes or explanations.",
                },
                new { role = "user", content = text },
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, _settings.TranslateEndpoint.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

        using var response = await Http.SendAsync(request, cancellation);
        var json = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(Shorten(json));

        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()?.Trim() ?? "";
    }

    private async Task<string> DeepLAsync(string text, CancellationToken cancellation)
    {
        if (_settings.TranslateKey is not { Length: > 0 } key)
            throw new InvalidOperationException(Strings.L("Add a translation key with the key button"));

        // Бесплатные ключи DeepL оканчиваются на :fx и живут на другом хосте.
        var host = key.EndsWith(":fx", StringComparison.Ordinal)
            ? "https://api-free.deepl.com"
            : "https://api.deepl.com";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/v2/translate")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["target_lang"] = IsRussianSource ? "EN" : "RU",
            }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("DeepL-Auth-Key", key);

        using var response = await Http.SendAsync(request, cancellation);
        var json = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(Shorten(json));

        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("translations")[0].GetProperty("text").GetString() ?? "";
    }

    /// <summary>Ошибка провайдера бывает на пол-экрана, а место есть на строку.</summary>
    private static string Shorten(string message) =>
        message.Length <= 160 ? message : message[..160] + "…";
}
