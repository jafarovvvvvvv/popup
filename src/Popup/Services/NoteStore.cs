using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Popup.Model;

namespace Popup.Services;

public sealed class Note
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("edited")]
    public DateTime Edited { get; set; } = DateTime.Now;
}

/// <summary>
/// Черновики: место, куда положить мысль на час.
///
/// Нарочно не приложение для заметок. Ни папок, ни форматирования, ни поиска —
/// для этого есть настоящие редакторы. Здесь заменяется несохранённый буфер,
/// который в них держат: номер из разговора, половина ссылки, мысль, к которой
/// вернуться, — записано быстро, потом удалено или унесено через буфер обмена.
///
/// Всё здесь устроено этой короткой жизнью: заметки создаются пустыми и
/// мгновенно, удаляются в один клик, а пустые подметают себя сами при уходе со
/// вкладки. Файл, в котором они лежат, — деталь реализации: в отличие от
/// сниппетов, править его руками никто не предполагает.
/// </summary>
public sealed class NoteStore : Observable
{
    public ObservableCollection<Note> Notes { get; } = [];

    private readonly DispatcherTimer _save;

    /// <summary>
    /// Какую заметку показывает редактор. Живёт здесь, а не в панели, чтобы
    /// выбор пережил схлопывание вместе с ней.
    /// </summary>
    private string? _selectedId;
    public string? SelectedId
    {
        get => _selectedId;
        set => Set(ref _selectedId, value);
    }

    public Note? Selected => Notes.FirstOrDefault(n => n.Id == SelectedId);

    public NoteStore()
    {
        // Спустя мгновение после паузы в наборе, а не на каждое нажатие: текст
        // и так лежит в памяти, а файл обязан быть верным лишь к тому моменту,
        // когда его кто-то мог бы прочитать.
        _save = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(0.8),
        };
        _save.Tick += (_, _) => Flush();
        Load();
    }

    // MARK: - Правка

    /// <summary>
    /// Новая пустая заметка, выбранная и готовая к набору. Свежие сверху, и
    /// дальше порядок не меняется: список, который пересобирает себя на каждую
    /// правку, теряет место, на котором читатель остановился, ради опрятности,
    /// о которой никто не просил.
    /// </summary>
    public Note Add()
    {
        var note = new Note();
        Notes.Insert(0, note);
        SelectedId = note.Id;
        Schedule();
        RaiseAll();
        return note;
    }

    public void Update(string id, string text)
    {
        var note = Notes.FirstOrDefault(n => n.Id == id);
        if (note is null) return;
        note.Text = text;
        note.Edited = DateTime.Now;
        Schedule();
    }

    public void Remove(string id)
    {
        var note = Notes.FirstOrDefault(n => n.Id == id);
        if (note is null) return;
        Notes.Remove(note);
        if (SelectedId == id) SelectedId = Notes.FirstOrDefault()?.Id;
        Schedule();
        RaiseAll();
    }

    /// <summary>
    /// Позвать при уходе со вкладки: заметки, в которые так и не написали,
    /// подметают себя сами. Воссоздать такую стоит одного наведения, а хвост
    /// пустых карточек — ровно тот беспорядок, ради которого черновики и
    /// заводят.
    /// </summary>
    public void Leave()
    {
        var empty = Notes.Where(n => string.IsNullOrWhiteSpace(n.Text)).ToList();
        foreach (var note in empty) Notes.Remove(note);
        if (SelectedId is not null && Notes.All(n => n.Id != SelectedId))
            SelectedId = Notes.FirstOrDefault()?.Id;
        Flush();
        RaiseAll();
    }

    // MARK: - Хранение

    private void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.Notes)) return;
            var stored = JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(AppPaths.Notes)) ?? [];
            foreach (var note in stored) Notes.Add(note);
            SelectedId = Notes.FirstOrDefault()?.Id;
        }
        catch (Exception e)
        {
            Log.Error("notes", "не прочитать notes.json", e);
        }
    }

    private void Schedule()
    {
        _save.Stop();
        _save.Start();
    }

    public void Flush()
    {
        _save.Stop();
        try
        {
            AppPaths.WriteAtomic(
                AppPaths.Notes,
                JsonSerializer.Serialize(Notes, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Error("notes", "не записать notes.json", e);
        }
    }
}
