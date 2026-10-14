using Popup.Services;

namespace Popup.Tests;

/// <summary>
/// Файл настроек — единственное, что человек правит руками, и каждая поблажка
/// здесь оплачена конкретной потерянной правкой.
/// </summary>
public class SettingsTests
{
    /// <summary>
    /// Документация говорит писать ключи с маленькой буквы. Раньше .NET писал
    /// их с большой и читал точным совпадением — правка «по инструкции» молча
    /// не применялась.
    /// </summary>
    [Fact]
    public void CamelCaseKeysAreRead()
    {
        var settings = Settings.FromJson("""{"saveClipboardImages":false,"toggleHotkey":"ctrl+alt+j"}""");
        Assert.False(settings.SaveClipboardImages);
        Assert.Equal("ctrl+alt+j", settings.ToggleHotkey);
    }

    /// <summary>А заодно и то, что панель писала в старых версиях.</summary>
    [Fact]
    public void OldPascalCaseKeysStillWork() =>
        Assert.False(Settings.FromJson("""{"SaveClipboardImages":false}""").SaveClipboardImages);

    [Fact]
    public void TrailingCommasAndCommentsAreTolerated()
    {
        var settings = Settings.FromJson("""
            {
              // мой ключ
              "translateModel": "gpt-5.4-mini",
            }
            """);
        Assert.Equal("gpt-5.4-mini", settings.TranslateModel);
    }

    /// <summary>Неизвестный ключ не должен стоить всех остальных.</summary>
    [Fact]
    public void UnknownKeysAreIgnored() =>
        Assert.Equal("openai",
            Settings.FromJson("""{"чтоТоНовое":1,"translateProvider":"openai"}""").TranslateProvider);

    /// <summary>
    /// Ключ вписывают открытым текстом — так сказано в документации. После
    /// чтения он обязан оказаться под DPAPI, а из открытого поля исчезнуть.
    /// </summary>
    [Fact]
    public void PlainSecretsMoveUnderProtection()
    {
        var settings = Settings.FromJson("""{"googleClientSecret":"GOCSPX-abc","translateApiKey":"sk-abc"}""");

        Assert.True(settings.AdoptPlainSecrets());

        Assert.Null(settings.GoogleClientSecret);
        Assert.Null(settings.TranslateApiKey);
        Assert.Equal("GOCSPX-abc", settings.GoogleSecret);
        Assert.Equal("sk-abc", settings.TranslateKey);
    }

    /// <summary>Второй проход по тем же настройкам ничего не меняет.</summary>
    [Fact]
    public void AdoptingIsIdempotent()
    {
        var settings = Settings.FromJson("""{"googleClientSecret":"GOCSPX-abc"}""");
        Assert.True(settings.AdoptPlainSecrets());
        Assert.False(settings.AdoptPlainSecrets());
    }

    /// <summary>Задвоенный суффикс чинится прямо в файле, молча.</summary>
    [Fact]
    public void ClientIdIsRepairedOnRead()
    {
        var settings = Settings.FromJson(
            """{"googleClientId":"1-test.apps.googleusercontent.com.apps.googleusercontent.com"}""");

        Assert.True(settings.AdoptPlainSecrets());
        Assert.Equal(
            "1-test.apps.googleusercontent.com",
            settings.GoogleClientId);
    }

    /// <summary>Расшифрованные значения в файл не попадают никогда.</summary>
    [Fact]
    public void DecryptedValuesAreNeverWritten()
    {
        var settings = Settings.FromJson("""{"googleClientSecret":"GOCSPX-abc","translateApiKey":"sk-abc"}""");
        settings.AdoptPlainSecrets();

        var json = settings.ToJson();

        Assert.DoesNotContain("GOCSPX-abc", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abc", json, StringComparison.Ordinal);
        Assert.Contains("googleClientSecretEnc", json, StringComparison.Ordinal);
    }

    /// <summary>Плюс в сочетании клавиш должен остаться плюсом, а не +.</summary>
    [Fact]
    public void HotkeyStaysReadableInTheFile() =>
        Assert.Contains("ctrl+alt+space",
            new Settings { ToggleHotkey = "ctrl+alt+space" }.ToJson(), StringComparison.Ordinal);

    /// <summary>Сломанный файл — это настройки по умолчанию, а не падение.</summary>
    [Fact]
    public void BrokenJsonDoesNotThrow() =>
        Assert.True(Settings.FromJson("{ это не json").SaveClipboardImages);
}
