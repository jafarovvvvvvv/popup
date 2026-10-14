using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Popup.Model;

namespace Popup.Services;

/// <summary>Одна заготовка. Формат файла тот же, что у оригинала.</summary>
public sealed class Snippet
{
    /// <summary>
    /// Необязательное имя. Без него строка показывает само значение — адресу
    /// или номеру телефона этого обычно достаточно.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Имя, а без имени — значение. По нему строки и различаются.</summary>
    [JsonIgnore]
    public string Id => Label.Length == 0 ? Text : Label;

    /// <summary>Угадывается по значению, чтобы строка узнавалась до прочтения.</summary>
    [JsonIgnore]
    public string IconKey
    {
        get
        {
            var value = Text.Trim();
            if (value.Contains('@') && !value.Contains(' ')) return "IconSend";
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return "IconLink";
            var digits = value.Count(char.IsDigit);
            if (digits >= 7 && value.All(c => char.IsDigit(c) || " +-()".Contains(c))) return "IconPin";
            return "IconNote";
        }
    }
}

/// <summary>
/// Список того, что не хочется набирать заново.
///
/// Нарочно не питается буфером обмена: буфер — очередь по свежести, и он
/// теряет ровно ту запись, которой пользуются раз в месяц. Что попадает сюда,
/// решается руками — из панели или прямо в файле, что ближе. Оба пути правят
/// один и тот же список.
/// </summary>
public sealed class SnippetStore : Observable
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Файл открывают и правят руками, и \/ в каждой ссылке — это
        // приложение, усложняющее человеку жизнь ради своего удобства.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public ObservableCollection<Snippet> Items { get; } = [];

    private string _query = "";
    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value)) RaiseAll(); }
    }

    /// <summary>
    /// Файл есть, но не читается — правка руками его сломала. Единственное
    /// состояние, в котором запись запрещена: «не смог прочитать» и «прочитал,
    /// там пусто» — разные ответы, и записывать поверх можно только на второй.
    /// </summary>
    public bool FileBroken { get; private set; }

    /// <summary>
    /// Ищет и по имени, и по значению: адрес помнят либо по тому, как он
    /// называется, либо по тому, что в нём, — надёжно по обоим редко.
    /// </summary>
    public IEnumerable<Snippet> Filtered
    {
        get
        {
            var needle = Query.Trim();
            if (needle.Length == 0) return Items;
            return Items.Where(s => Contains(s.Label, needle) || Contains(s.Text, needle));
        }
    }

    /// <summary>Без учёта регистра, чтобы «почта» находила «Почта».</summary>
    private static bool Contains(string haystack, string needle) =>
        CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            haystack, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    /// <summary>
    /// Перечитывается на каждом заходе во вкладку. Файл правят снаружи, так что
    /// единственный момент, когда копии в памяти можно верить, — момент прямо
    /// перед показом.
    /// </summary>
    public void Reload()
    {
        if (!File.Exists(AppPaths.Snippets))
        {
            // Нет файла — это честный пустой список, и писать поверх безопасно.
            Items.Clear();
            FileBroken = false;
            RaiseAll();
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<Snippet>>(File.ReadAllText(AppPaths.Snippets)) ?? [];
            Items.Clear();
            foreach (var snippet in stored) Items.Add(snippet);
            FileBroken = false;
        }
        catch (Exception e)
        {
            // Файл есть и что-то говорит — просто не читается. Оставляем на
            // экране то, что было, и поднимаем флаг: молчание здесь — это то,
            // из-за чего случайная запятая превращалась в потерянный файл.
            FileBroken = true;
            Log.Error("snippets", "snippets.json не читается", e);
        }
        RaiseAll();
    }

    /// <summary>
    /// Добавляет одну заготовку и пишет файл.
    ///
    /// Сначала перечитывает: файл правят и руками, а копия в памяти свежа лишь
    /// настолько, насколько давно заходили во вкладку. Запись поверх вслепую
    /// молча отменила бы всё, что успели дописать в редакторе.
    /// </summary>
    public void Add(string label, string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return;

        Reload();
        // Файл, который не удалось прочитать, записывать нельзя. Заготовка
        // выбрасывается, а не остаётся в памяти как будто сохранённая:
        // притворство обменяло бы видимый отказ сейчас на тихую потерю потом.
        if (FileBroken)
        {
            Log.Warn("snippets", "запись отменена: файл не читается");
            return;
        }

        var snippet = new Snippet { Label = label.Trim(), Text = value };
        var existing = Items.FirstOrDefault(s => s.Id == snippet.Id);
        if (existing is not null) Items.Remove(existing);
        Items.Insert(0, snippet);
        Persist();
        RaiseAll();
    }

    public void Remove(Snippet snippet)
    {
        Items.Remove(snippet);
        Persist();
        RaiseAll();
    }

    private void Persist()
    {
        if (FileBroken) return;
        try
        {
            AppPaths.WriteAtomic(AppPaths.Snippets, JsonSerializer.Serialize(Items, WriteOptions));
        }
        catch (Exception e)
        {
            Log.Error("snippets", "не записать snippets.json", e);
        }
    }
}
