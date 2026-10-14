using System.Text.Json;
using Popup.Services;

namespace Popup.Model;

/// <summary>
/// Что чужой процесс может попросить у панели через канал.
///
/// Набор нарочно маленький и весь про «положить сюда»: показать, что идёт
/// работа, закинуть файл на полку, текст в буфер, мысль в заметки. Ничего
/// читающего и ничего запускающего здесь нет — панель принимает, но не
/// выдаёт наружу ни истории буфера, ни календаря, ни сниппетов.
/// </summary>
public static class ExternalCommands
{
    public static void Apply(NotchViewModel vm, JsonElement message)
    {
        var type = Str(message, "type");
        switch (type)
        {
            case "activity":
                Activity(vm, message);
                break;

            case "clip":
                if (Str(message, "text") is { Length: > 0 } text)
                {
                    vm.Clipboard.CopyText(text);
                    // В историю запись попадёт сама: буфер сменился, и панель
                    // услышит об этом системным сообщением, как о любом другом
                    // копировании.
                }
                break;

            case "shelf":
                if (message.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
                {
                    vm.Shelf.Add(paths.EnumerateArray()
                        .Where(p => p.ValueKind == JsonValueKind.String)
                        .Select(p => p.GetString()!)
                        .Where(p => p.Length > 0));
                }
                break;

            case "note":
                if (Str(message, "text") is { Length: > 0 } body)
                {
                    var note = vm.Notes.Add();
                    vm.Notes.Update(note.Id, body);
                    vm.Notes.Flush();
                }
                break;

            case "hello":
                // Клиент представился. Отвечать нечего: он уже понял, что панель
                // жива, самим фактом успешного подключения.
                break;

            default:
                Log.Warn("pipe", $"неизвестная команда: {type ?? "(нет)"}");
                break;
        }
    }

    private static void Activity(NotchViewModel vm, JsonElement message)
    {
        var update = ParseActivity(message);
        if (update.Item is { } item) vm.Activity.Show(item);
        else vm.Activity.Clear(update.Source);
    }

    /// <summary>
    /// Разбор одного сообщения об активности. <c>Item</c> пустой — источник
    /// просит убрать свою полоску.
    ///
    /// Отделено от применения нарочно: здесь живут все допуски формата — два
    /// словаря тонов, две записи кнопок, — и это ровно та часть, которую
    /// стоит проверять тестами, не поднимая ради этого всю панель.
    /// </summary>
    public static (string Source, ActivityStore.Item? Item) ParseActivity(JsonElement message)
    {
        var source = Str(message, "source") ?? "external";
        var tone = Str(message, "tone") ?? "idle";

        if (tone is "idle" or "none" or "") return (source, null);

        var actions = new List<ActivityStore.Action>();
        if (message.TryGetProperty("actions", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var action in list.EnumerateArray())
            {
                // Допускаются обе записи: {"id":"stop","label":"Стоп"} и просто
                // "stop". Вторая короче, а подпись кнопке нужна не всегда —
                // на полоске в двести точек её всё равно негде показать.
                if (action.ValueKind == JsonValueKind.String)
                {
                    var id = action.GetString();
                    if (id is { Length: > 0 }) actions.Add(new ActivityStore.Action(id, id));
                }
                else if (action.ValueKind == JsonValueKind.Object && Str(action, "id") is { Length: > 0 } id)
                {
                    actions.Add(new ActivityStore.Action(id, Str(action, "label") ?? id));
                }
            }
        }

        return (source, new ActivityStore.Item(
            Source: source,
            Name: Str(message, "name") ?? source,
            Tone: tone switch
            {
                "live" or "recording" => ActivityStore.Tone.Live,
                "busy" or "processing" => ActivityStore.Tone.Busy,
                "ok" or "success" or "done" => ActivityStore.Tone.Ok,
                _ => ActivityStore.Tone.Fail,
            },
            Label: Str(message, "label") ?? "",
            Actions: actions,
            At: DateTime.Now));
    }

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
