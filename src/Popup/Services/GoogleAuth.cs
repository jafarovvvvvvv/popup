using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Popup.Services;

/// <summary>
/// Вход в Google для календаря: поток «installed app» с PKCE и возвратом на
/// локальный адрес.
///
/// Написан на голом HTTP, а не на официальном SDK, нарочно: весь поток — три
/// запроса и один <see cref="HttpListener"/>, и они целиком помещаются в этот
/// файл. Пакет Google.Apis притащил бы полтора десятка зависимостей и спрятал
/// бы за собой ровно то, что владелец этой панели захочет однажды поправить
/// сам.
///
/// Как в оригинале, вход спрашивается только по нажатию кнопки внутри вкладки —
/// на старте панель не просит вообще ничего.
/// </summary>
public sealed class GoogleAuth(Settings settings)
{
    private const string Scope = "https://www.googleapis.com/auth/calendar.readonly";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly HttpClient Http = new();

    private sealed class StoredToken
    {
        public string? RefreshToken { get; set; }
        public string? AccessToken { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private StoredToken? _token;

    /// <summary>Ключи вписаны и выглядят так, как должен выглядеть client id.</summary>
    public bool IsConfigured => GoogleClient.IsValidId(settings.GoogleClientId);

    /// <summary>
    /// Ключ вписан, но не похож на настоящий. Отдельное состояние, потому что
    /// «не настроено» и «настроено с опечаткой» требуют разных подсказок, а
    /// разница между ними — один лишний кусок строки, который глаз не ловит.
    /// </summary>
    public bool IsMisconfigured =>
        !string.IsNullOrWhiteSpace(settings.GoogleClientId) && !IsConfigured;

    /// <summary>Есть ли сохранённый вход — по нему вкладка решает, что показывать.</summary>
    public bool IsConnected => LoadToken()?.RefreshToken is not null;

    public void Disconnect()
    {
        _token = null;
        try
        {
            if (File.Exists(AppPaths.GoogleToken)) File.Delete(AppPaths.GoogleToken);
        }
        catch (Exception e)
        {
            Log.Warn("google", $"не удалить токен: {e.Message}");
        }
    }

    // MARK: - Вход

    /// <summary>
    /// Открывает браузер и ждёт возврата. Возвращает <c>true</c>, если вход
    /// состоялся.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellation = default)
    {
        if (GoogleClient.NormalizeId(settings.GoogleClientId) is not { } clientId ||
            !GoogleClient.IsValidId(clientId))
        {
            Log.Warn("google", $"googleClientId не годится: {settings.GoogleClientId ?? "(пусто)"}");
            return false;
        }

        // Порт занимает система: фиксированный номер рано или поздно окажется
        // чужим, а Google для loopback-потока разрешает любой.
        var listener = new HttpListener();
        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(redirect);

        var verifier = RandomUrlSafe(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = RandomUrlSafe(16);

        var url = $"{AuthEndpoint}?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
                  $"&response_type=code&scope={Uri.EscapeDataString(Scope)}" +
                  $"&code_challenge={challenge}&code_challenge_method=S256" +
                  $"&state={state}&access_type=offline&prompt=consent";

        try
        {
            listener.Start();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });

            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3), cancellation);
            var query = context.Request.QueryString;
            var code = query["code"];
            var returnedState = query["state"];

            await RespondAsync(context, code is not null && returnedState == state);

            if (code is null || returnedState != state)
            {
                Log.Warn("google", "вход отменён или ответ не совпал по state");
                return false;
            }

            return await ExchangeAsync(clientId, code, verifier, redirect, cancellation);
        }
        catch (Exception e)
        {
            Log.Error("google", "вход не удался", e);
            return false;
        }
        finally
        {
            if (listener.IsListening) listener.Stop();
            listener.Close();
        }
    }

    /// <summary>Страничка, которую человек видит в браузере после согласия.</summary>
    private static async Task RespondAsync(HttpListenerContext context, bool ok)
    {
        var body = Encoding.UTF8.GetBytes(
            "<!doctype html><meta charset=utf-8>" +
            "<body style=\"font:15px system-ui;background:#111;color:#eee;display:grid;" +
            "place-items:center;height:100vh;margin:0\">" +
            (ok ? "<p>Готово. Можно вернуться в Popup.</p>" : "<p>Вход не завершён.</p>"));
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }

    private async Task<bool> ExchangeAsync(
        string clientId, string code, string verifier, string redirect, CancellationToken cancellation)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirect,
        };
        // Google требует секрет и в loopback-потоке — и сам же пишет, что для
        // десктопного клиента он секретом не считается.
        if (settings.GoogleSecret is { Length: > 0 } secret) form["client_secret"] = secret;

        using var response = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellation);
        var json = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("google", $"обмен кода отклонён: {json}");
            return false;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        _token = new StoredToken
        {
            RefreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            AccessToken = root.GetProperty("access_token").GetString(),
            ExpiresAt = DateTime.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32() - 60),
        };
        SaveToken();
        return true;
    }

    // MARK: - Токен

    /// <summary>Действующий access-токен, при необходимости обновлённый.</summary>
    public async Task<string?> AccessTokenAsync(CancellationToken cancellation = default)
    {
        var token = LoadToken();
        if (token is null) return null;
        if (token.AccessToken is not null && token.ExpiresAt > DateTime.UtcNow) return token.AccessToken;
        if (token.RefreshToken is null) return null;
        if (GoogleClient.NormalizeId(settings.GoogleClientId) is not { } clientId) return null;

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = token.RefreshToken,
            ["grant_type"] = "refresh_token",
        };
        if (settings.GoogleSecret is { Length: > 0 } secret) form["client_secret"] = secret;

        try
        {
            using var response = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellation);
            var json = await response.Content.ReadAsStringAsync(cancellation);
            if (!response.IsSuccessStatusCode)
            {
                Log.Error("google", $"обновление токена отклонено: {json}");
                return null;
            }
            using var document = JsonDocument.Parse(json);
            token.AccessToken = document.RootElement.GetProperty("access_token").GetString();
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(
                document.RootElement.GetProperty("expires_in").GetInt32() - 60);
            _token = token;
            SaveToken();
            return token.AccessToken;
        }
        catch (Exception e)
        {
            Log.Error("google", "не обновить токен", e);
            return null;
        }
    }

    private StoredToken? LoadToken()
    {
        if (_token is not null) return _token;
        try
        {
            if (!File.Exists(AppPaths.GoogleToken)) return null;
            var plain = Secrets.Unprotect(File.ReadAllText(AppPaths.GoogleToken));
            if (plain is null) return null;
            _token = JsonSerializer.Deserialize<StoredToken>(plain);
        }
        catch (Exception e)
        {
            Log.Warn("google", $"не прочитать токен: {e.Message}");
        }
        return _token;
    }

    private void SaveToken()
    {
        try
        {
            var protectedJson = Secrets.Protect(JsonSerializer.Serialize(_token));
            if (protectedJson is not null) AppPaths.WriteAtomic(AppPaths.GoogleToken, protectedJson);
        }
        catch (Exception e)
        {
            Log.Error("google", "не сохранить токен", e);
        }
    }

    // MARK: - Мелочи

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RandomUrlSafe(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
