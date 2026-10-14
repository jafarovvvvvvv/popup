using Popup.Services;

namespace Popup.Tests;

/// <summary>
/// Иконка угадывается по значению, чтобы строку можно было узнать до того, как
/// её прочитали. Ошибка здесь стоит недорого, но и правило должно быть таким,
/// каким его описали в документации формата.
/// </summary>
public class SnippetTests
{
    [Theory]
    [InlineData("emil@example.com", "IconSend")]
    [InlineData("https://github.com/akalikbergenov/cyclop", "IconLink")]
    [InlineData("http://192.168.0.10", "IconLink")]
    [InlineData("+7 (999) 123-45-67", "IconPin")]
    [InlineData("улица Пушкина, дом Колотушкина", "IconNote")]
    public void IconFollowsTheValue(string text, string expected) =>
        Assert.Equal(expected, new Snippet { Text = text }.IconKey);

    /// <summary>
    /// Адрес с пробелом — это фраза, а не почта. Иначе любое предложение со
    /// словом «собака@» получало бы конверт.
    /// </summary>
    [Fact]
    public void TextWithSpacesIsNotAnAddress() =>
        Assert.Equal("IconNote", new Snippet { Text = "напиши на emil@example.com" }.IconKey);

    /// <summary>
    /// Короткие числа — не телефон: год, номер дома, сумма. Порог в семь цифр
    /// отделяет их от номера.
    /// </summary>
    [Fact]
    public void ShortNumbersAreNotPhones() =>
        Assert.Equal("IconNote", new Snippet { Text = "2026" }.IconKey);

    /// <summary>Имя — то, чем строки различаются; без имени им служит значение.</summary>
    [Fact]
    public void IdentityFallsBackToTheValue()
    {
        Assert.Equal("Почта", new Snippet { Label = "Почта", Text = "a@b.c" }.Id);
        Assert.Equal("a@b.c", new Snippet { Text = "a@b.c" }.Id);
    }
}
