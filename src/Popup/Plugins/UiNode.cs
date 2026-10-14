using System.Text.Json;

namespace Popup.Plugins;

/// <summary>
/// Узел интерфейса, присланный плагином.
///
/// Плагин не рисует — он описывает. Иначе пришлось бы либо пускать чужой код в
/// процесс панели, либо давать ему рисовать самому, и тогда каждая интеграция
/// выглядела бы по-своему. Здесь набор узлов нарочно узкий: из них нельзя
/// собрать что попало, зато всё, что из них собрано, выглядит как остальная
/// панель.
///
/// Формат:
/// <code>
/// {"type":"text","text":"…","muted":true,"bold":false}
/// {"type":"row","children":[…]}
/// {"type":"list","items":[{"id":"1","title":"…","subtitle":"…","value":"…","action":"copy"}]}
/// {"type":"input","id":"query","value":"","placeholder":"…","action":"search"}
/// {"type":"button","id":"go","label":"Расшифровать","value":"…"}
/// {"type":"image","path":"C:\\…"}
/// {"type":"progress","value":0.4}
/// </code>
/// Значения <c>action</c>, которые панель выполняет сама, не спрашивая плагин:
/// <c>copy</c> — положить <c>value</c> в буфер, <c>open</c> — открыть путь или
/// ссылку. Любое другое имя уезжает плагину как <c>{"type":"action"}</c>.
/// </summary>
public sealed class UiNode
{
    public string Type { get; init; } = "text";
    public string Text { get; init; } = "";
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Placeholder { get; init; } = "";
    public string Action { get; init; } = "";
    public string Path { get; init; } = "";
    public bool Muted { get; init; }
    public bool Bold { get; init; }
    public double Progress { get; init; }
    public IReadOnlyList<UiNode> Children { get; init; } = [];
    public IReadOnlyList<UiNode> Items { get; init; } = [];

    /// <summary>Заголовок строки списка — им же служит <see cref="Text"/> у прочих узлов.</summary>
    public string Title => Label.Length > 0 ? Label : Text;

    public static IReadOnlyList<UiNode> ParseList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return [];
        var list = new List<UiNode>();
        foreach (var item in element.EnumerateArray())
        {
            if (Parse(item) is { } node) list.Add(node);
        }
        return list;
    }

    private static UiNode? Parse(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return new UiNode
        {
            Type = Str(element, "type", "text"),
            Text = Str(element, "text"),
            Id = Str(element, "id"),
            Label = Str(element, "label"),
            Value = Str(element, "value"),
            Placeholder = Str(element, "placeholder"),
            Action = Str(element, "action"),
            Path = Str(element, "path"),
            Muted = Bool(element, "muted"),
            Bold = Bool(element, "bold"),
            Progress = Num(element, "value"),
            Children = ParseList(element.TryGetProperty("children", out var c) ? c : default),
            Items = ParseList(element.TryGetProperty("items", out var i) ? i : default),
        };
    }

    private static string Str(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static double Num(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
}
