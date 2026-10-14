using Popup.Services;

namespace Popup.Tests;

/// <summary>
/// Идентификатор клиента Google копируют руками, и ошибаются в нём одинаково.
/// Разбор обязан это переживать, а неисправимое — называть неисправимым до
/// того, как человек уедет в браузер за «401 invalid_client».
/// </summary>
public class GoogleClientTests
{
    private const string Good = "1-test.apps.googleusercontent.com";

    [Fact]
    public void GoodIdSurvivesUntouched() => Assert.Equal(Good, GoogleClient.NormalizeId(Good));

    /// <summary>
    /// Тот самый промах: в Google Cloud идентификатор стоит рядом с подписью
    /// «.apps.googleusercontent.com», и суффикс уезжает в буфер дважды.
    /// </summary>
    [Fact]
    public void DoubledSuffixIsRepaired() =>
        Assert.Equal(Good, GoogleClient.NormalizeId(Good + ".apps.googleusercontent.com"));

    [Fact]
    public void EvenThreeSuffixesAreRepaired() =>
        Assert.Equal(Good, GoogleClient.NormalizeId(
            Good + ".apps.googleusercontent.com.apps.googleusercontent.com"));

    [Fact]
    public void SurroundingSpaceIsTrimmed() =>
        Assert.Equal(Good, GoogleClient.NormalizeId($"  {Good}\r\n"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyStaysEmpty(string? value) => Assert.Null(GoogleClient.NormalizeId(value));

    [Fact]
    public void RepairedIdCountsAsValid() =>
        Assert.True(GoogleClient.IsValidId(Good + ".apps.googleusercontent.com"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не-идентификатор")]
    [InlineData("1-te.apps.googleusercontent.co")]
    [InlineData("GOCSPX-not-an-id")]
    public void NonsenseIsRejected(string? value) => Assert.False(GoogleClient.IsValidId(value));

    /// <summary>
    /// Секрет, вписанный в поле идентификатора, — тоже частая путаница, и её
    /// надо ловить здесь, а не на стороне Google.
    /// </summary>
    [Fact]
    public void SecretInTheIdFieldIsRejected() =>
        Assert.False(GoogleClient.IsValidId("GOCSPX-something-that-is-a-secret"));
}
