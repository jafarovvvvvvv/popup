using Popup.Services;

namespace Popup.Tests;

/// <summary>
/// Полоска перемотки — единственное место панели, где числа видно посимвольно,
/// и колонка под них шириной ровно в 32 точки.
/// </summary>
public class FormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(9, "0:09")]
    [InlineData(59, "0:59")]
    [InlineData(60, "1:00")]
    [InlineData(605, "10:05")]
    public void ShortTracksHaveNoHours(int seconds, string expected) =>
        Assert.Equal(expected, Format.Time(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void HoursAppearOnlyWhenThereAreHours() =>
        Assert.Equal("1:02:03", Format.Time(TimeSpan.FromSeconds(3723)));

    [Fact]
    public void HalfSecondsRoundRatherThanTruncate() =>
        Assert.Equal("0:02", Format.Time(TimeSpan.FromSeconds(1.6)));

    /// <summary>
    /// Пока трек не начался, длительность неизвестна. Показать «0:00» значило бы
    /// соврать: это не ноль, это «неизвестно».
    /// </summary>
    [Fact]
    public void NegativeTimeIsUnknown() =>
        Assert.Equal("--:--", Format.Time(TimeSpan.FromSeconds(-1)));
}
