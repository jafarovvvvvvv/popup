using System.Text.Json;
using Popup.Model;
using Popup.Services;

namespace Popup.Tests;

/// <summary>
/// Канал — единственная дверь, через которую в панель попадает что-то извне.
/// Всё, что сюда приходит, написано чужой рукой, и разбор обязан переживать
/// и опечатки, и старую запись формата.
/// </summary>
public class ExternalCommandsTests
{
    private static ActivityStore.Item Parse(string json)
    {
        var (_, item) = ExternalCommands.ParseActivity(JsonDocument.Parse(json).RootElement);
        Assert.NotNull(item);
        return item!;
    }

    [Theory]
    [InlineData("live", ActivityStore.Tone.Live)]
    [InlineData("recording", ActivityStore.Tone.Live)]
    [InlineData("busy", ActivityStore.Tone.Busy)]
    [InlineData("processing", ActivityStore.Tone.Busy)]
    [InlineData("ok", ActivityStore.Tone.Ok)]
    [InlineData("done", ActivityStore.Tone.Ok)]
    [InlineData("success", ActivityStore.Tone.Ok)]
    public void ToneAcceptsBothVocabularies(string tone, ActivityStore.Tone expected) =>
        Assert.Equal(expected, Parse($$"""{"type":"activity","source":"s","tone":"{{tone}}"}""").Tone);

    [Theory]
    [InlineData("idle")]
    [InlineData("none")]
    public void IdleMeansClear(string tone)
    {
        var (source, item) = ExternalCommands.ParseActivity(
            JsonDocument.Parse($$"""{"type":"activity","source":"whisper","tone":"{{tone}}"}""").RootElement);
        Assert.Null(item);
        Assert.Equal("whisper", source);
    }

    /// <summary>
    /// Кнопку можно описать и объектом, и просто именем: на полоске в двести
    /// точек подпись всё равно негде показать.
    /// </summary>
    [Fact]
    public void ActionsAcceptBothShapes()
    {
        var full = Parse("""{"type":"activity","source":"s","tone":"live","actions":[{"id":"stop","label":"Стоп"}]}""");
        Assert.Equal("stop", full.Actions.Single().Id);
        Assert.Equal("Стоп", full.Actions.Single().Label);

        var terse = Parse("""{"type":"activity","source":"s","tone":"live","actions":["stop"]}""");
        Assert.Equal("stop", terse.Actions.Single().Id);
    }

    /// <summary>Сообщение без единого знакомого поля не должно ронять разбор.</summary>
    [Fact]
    public void UnknownShapesSurvive()
    {
        var (source, item) = ExternalCommands.ParseActivity(
            JsonDocument.Parse("""{"type":"activity"}""").RootElement);
        Assert.Equal("external", source);
        Assert.Null(item);
    }

    [Fact]
    public void NameFallsBackToSource() =>
        Assert.Equal("whisper", Parse("""{"type":"activity","source":"whisper","tone":"live"}""").Name);
}

/// <summary>
/// Правила, по которым полоска сменяется: один источник не должен стирать
/// чужую работу с экрана.
/// </summary>
public class ActivityStoreTests
{
    private static ActivityStore.Item Item(string source, ActivityStore.Tone tone = ActivityStore.Tone.Live) =>
        new(source, source, tone, "", [], DateTime.Now);

    [Fact]
    public void ShowingMakesItActive()
    {
        var store = new ActivityStore();
        Assert.False(store.IsActive);
        store.Show(Item("whisper"));
        Assert.True(store.IsActive);
    }

    [Fact]
    public void OneSourceDoesNotClearAnother()
    {
        var store = new ActivityStore();
        store.Show(Item("whisper"));
        store.Clear("build");
        Assert.True(store.IsActive);
        Assert.Equal("whisper", store.Current!.Source);
    }

    [Fact]
    public void ASourceClearsItsOwn()
    {
        var store = new ActivityStore();
        store.Show(Item("whisper"));
        store.Clear("whisper");
        Assert.False(store.IsActive);
    }

    /// <summary>Панель имеет право убрать что угодно — например, при выходе.</summary>
    [Fact]
    public void NullClearsAnything()
    {
        var store = new ActivityStore();
        store.Show(Item("whisper"));
        store.Clear(null);
        Assert.False(store.IsActive);
    }
}
