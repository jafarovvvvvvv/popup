using System.Collections.ObjectModel;
using System.Text.Json;
using Popup.Model;

namespace Popup.Services;

/// <summary>
/// Полка: место, куда можно бросить файл на полчаса.
///
/// Хранятся ссылки, а не копии — как в оригинале. Полка не архив: она нужна,
/// чтобы донести файл из одного окна в другое, и копия здесь означала бы
/// вторую версию того же файла, расходящуюся с первой. Обратная сторона
/// честная: переименованный или удалённый файл уносит с собой и карточку.
/// </summary>
public sealed class ShelfStore : Observable
{
    public sealed record Item(string Path)
    {
        public string Name => System.IO.Path.GetFileName(Path);
        public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
        public bool IsImage => Extension is "PNG" or "JPG" or "JPEG" or "GIF" or "BMP" or "WEBP";
    }

    public ObservableCollection<Item> Items { get; } = [];

    public void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.Shelf)) return;
            var stored = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AppPaths.Shelf)) ?? [];
            Items.Clear();
            // Пропавшие файлы отсеиваются на чтении, а не показываются мёртвыми
            // карточками: полка, полная ссылок в никуда, перестаёт быть полкой.
            foreach (var path in stored.Where(p => File.Exists(p) || Directory.Exists(p)))
                Items.Add(new Item(path));
            RaiseAll();
        }
        catch (Exception e)
        {
            Log.Error("shelf", "не прочитать полку", e);
        }
    }

    public void Add(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) continue;
            var existing = Items.FirstOrDefault(i =>
                string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) Items.Remove(existing);
            Items.Insert(0, new Item(path));
            added = true;
        }
        if (!added) return;
        Persist();
        RaiseAll();
    }

    public void Remove(Item item)
    {
        Items.Remove(item);
        Persist();
        RaiseAll();
    }

    public void Clear()
    {
        Items.Clear();
        Persist();
        RaiseAll();
    }

    private void Persist()
    {
        try
        {
            AppPaths.WriteAtomic(
                AppPaths.Shelf,
                JsonSerializer.Serialize(Items.Select(i => i.Path), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Log.Error("shelf", "не записать полку", e);
        }
    }
}
