using System.Windows.Threading;

namespace Popup.Services;

/// <summary>
/// Следит за <c>settings.json</c> и перечитывает его, когда файл правят руками.
///
/// Без этого документация обманывает дважды. В трее есть пункт «Файл настроек»,
/// который приглашает файл открыть; открыв и поправив, человек естественно
/// ждёт, что панель это заметит, — а она читала файл один раз при запуске, и
/// вписанный ключ не делал ничего. Сниппеты давно перечитываются на входе во
/// вкладку ровно по той же причине; настройкам полагается то же самое.
/// </summary>
public sealed class SettingsWatcher : IDisposable
{
    private readonly Settings _settings;
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounce;

    /// <summary>Позвать, когда в настройках действительно что-то изменилось.</summary>
    public event Action? Changed;

    public SettingsWatcher(Settings settings)
    {
        _settings = settings;

        // Редакторы пишут файл в несколько приёмов — временный файл, переимено-
        // вание, обновление времени, — и на одну правку прилетает три-четыре
        // события. Ждём, пока перестанут.
        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Reload();
        };

        AppPaths.EnsureCreated();
        _watcher = new FileSystemWatcher(AppPaths.Root, Path.GetFileName(AppPaths.Settings))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Собственная запись — не повод перечитывать: так рождаются петли, в
        // которых сохранение вызывает чтение, а чтение сохранение.
        if (DateTime.UtcNow - _settings.LastSelfWrite < TimeSpan.FromSeconds(1.5)) return;

        // События приходят с чужого потока, а перечитывать и применять надо там,
        // где живёт панель.
        _debounce.Dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private void Reload()
    {
        try
        {
            if (!_settings.ReloadInPlace()) return;
            Log.Info("settings", "файл изменился, настройки перечитаны");
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error("settings", "не перечитать настройки", e);
        }
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
