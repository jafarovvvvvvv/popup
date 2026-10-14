using System.Collections.ObjectModel;
using Popup.Model;
using Popup.Services;

namespace Popup.Plugins;

/// <summary>
/// Реестр плагинов: находит папки, поднимает процессы, умеет перезапустить всё
/// заново без перезапуска панели.
///
/// Перезагрузка без перезапуска — не удобство, а условие, при котором плагин
/// вообще можно писать: цикл «поправил файл — увидел результат» должен быть
/// короче, чем цикл «поправил — перезапустил — навёл мышь — нашёл вкладку».
/// </summary>
public sealed class PluginHost : Observable
{
    public ObservableCollection<PluginInstance> Plugins { get; } = [];

    /// <summary>Строки разговора со всеми плагинами — то, что показывает вкладка Dev.</summary>
    private readonly List<string> _trace = [];
    public IReadOnlyList<string> Trace => _trace;

    public event Action<string>? Traced;

    /// <summary>Позвать, когда список плагинов изменился: рейл достраивается по нему.</summary>
    public event Action? PluginsChanged;

    private const int TraceCapacity = 400;

    /// <summary>
    /// Раскладывает плагины из комплекта в пользовательскую папку — по одному
    /// разу на плагин.
    ///
    /// Копия, а не запуск из папки приложения: плагин из комплекта задуман как
    /// образец, который правят, а файл рядом с exe перезапишется при первом же
    /// обновлении. Существующие папки не трогаются никогда, даже если в
    /// комплекте лежит версия новее: там уже может быть чужая работа.
    /// </summary>
    public void Seed()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(bundled)) return;

        foreach (var source in Directory.EnumerateDirectories(bundled))
        {
            var target = Path.Combine(AppPaths.Plugins, Path.GetFileName(source));
            if (Directory.Exists(target)) continue;
            try
            {
                Directory.CreateDirectory(target);
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(source, file);
                    var destination = Path.Combine(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
                Log.Info("plugins", $"плагин из комплекта установлен: {Path.GetFileName(source)}");
            }
            catch (Exception e)
            {
                Log.Error("plugins", $"не установить {source}", e);
            }
        }
    }

    public void Load()
    {
        StopAll();
        Plugins.Clear();

        try
        {
            Directory.CreateDirectory(AppPaths.Plugins);
            Seed();
            foreach (var folder in Directory.EnumerateDirectories(AppPaths.Plugins).OrderBy(f => f))
            {
                if (PluginManifest.Read(folder) is not { } manifest) continue;
                if (Plugins.Any(p => p.Manifest.Id == manifest.Id))
                {
                    Write($"= пропущен дубликат идентификатора: {manifest.Id}");
                    continue;
                }
                var instance = new PluginInstance(manifest);
                instance.Traced += Write;
                Plugins.Add(instance);
                instance.Start();
            }
            Write($"= загружено плагинов: {Plugins.Count}");
        }
        catch (Exception e)
        {
            Log.Error("plugins", "не прочитать папку плагинов", e);
        }

        PluginsChanged?.Invoke();
        RaiseAll();
    }

    public void StopAll()
    {
        foreach (var plugin in Plugins)
        {
            plugin.Traced -= Write;
            plugin.Stop();
        }
    }

    public PluginInstance? Find(string id) => Plugins.FirstOrDefault(p => p.Manifest.Id == id);

    private void Write(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss.fff} {line}";
        _trace.Add(stamped);
        while (_trace.Count > TraceCapacity) _trace.RemoveAt(0);
        Traced?.Invoke(stamped);
    }

    /// <summary>Ручная отправка строки плагину — этим пользуется консоль во вкладке Dev.</summary>
    public void SendRaw(PluginInstance plugin, string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            plugin.Send(document.RootElement);
        }
        catch (Exception e)
        {
            Write($"= не отправлено, это не JSON: {e.Message}");
        }
    }
}
