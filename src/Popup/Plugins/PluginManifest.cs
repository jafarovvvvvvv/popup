using System.Text.Json;
using System.Text.Json.Serialization;
using Popup.Services;

namespace Popup.Plugins;

/// <summary>
/// Описание плагина — файл <c>plugin.json</c> в его папке.
///
/// Плагин — это любая программа, которая умеет читать строки из stdin и писать
/// строки в stdout. Python, Node, скомпилированный exe, .bat — Popup всё равно.
/// Именно поэтому здесь нет ни слова про язык: манифест говорит, чем запустить,
/// а дальше разговор идёт по протоколу, а не по типу процесса.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Устойчивое имя. Вкладка получит идентификатор <c>plugin:{id}</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Подпись в шапке панели.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Ключ иконки из <c>Icons.xaml</c>. Пусто — общая иконка плагина.</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "IconPlugin";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    /// <summary>Рабочая папка, относительно папки плагина. По умолчанию — она сама.</summary>
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = ".";

    /// <summary>
    /// Как часто просить плагин перерисоваться, пока его вкладка открыта.
    /// Ноль — не просить: плагин, которому есть что сказать, скажет сам.
    /// </summary>
    [JsonPropertyName("refreshMs")]
    public int RefreshMs { get; set; }

    /// <summary>Дополнительные переменные окружения процесса.</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>Папка, из которой прочитан манифест.</summary>
    [JsonIgnore]
    public string Folder { get; set; } = "";

    public static PluginManifest? Read(string folder)
    {
        var file = Path.Combine(folder, "plugin.json");
        if (!File.Exists(file)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file));
            if (manifest is null) return null;
            if (manifest.Id.Length == 0) manifest.Id = new DirectoryInfo(folder).Name;
            if (manifest.Name.Length == 0) manifest.Name = manifest.Id;
            if (manifest.Icon.Length == 0) manifest.Icon = "IconPlugin";
            manifest.Folder = folder;
            return manifest;
        }
        catch (Exception e)
        {
            Log.Error("plugins", $"не прочитать {file}", e);
            return null;
        }
    }
}
