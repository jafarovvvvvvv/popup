namespace Popup.Services;

/// <summary>
/// Единственное место, знающее, где что лежит.
///
/// Всё пользовательское — в <c>%APPDATA%\Popup</c>, а не рядом с исполняемым
/// файлом: так настройки, сниппеты и плагины переживают пересборку и переезд
/// приложения.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Popup");

    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Snippets => Path.Combine(Root, "snippets.json");
    public static string Notes => Path.Combine(Root, "notes.json");
    public static string Shelf => Path.Combine(Root, "shelf.json");
    public static string Screenshots => Path.Combine(Root, "Screenshots");
    public static string Plugins => Path.Combine(Root, "plugins");
    public static string Logs => Path.Combine(Root, "logs");
    public static string GoogleToken => Path.Combine(Root, "google-token.dat");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Screenshots);
        Directory.CreateDirectory(Plugins);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>
    /// Запись через временный файл: обрыв питания посреди сохранения не должен
    /// оставлять от истории буфера половину JSON-а, которую потом не прочитать.
    /// </summary>
    public static void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}
