using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using Popup.Model;
using Popup.Services;

namespace Popup.Plugins;

/// <summary>
/// Один запущенный плагин: процесс, разговор с ним и последняя присланная
/// картинка интерфейса.
///
/// Разговор — построчный JSON через stdin/stdout. Выбран именно он, а не
/// сокет или COM: строку в stdout умеет написать вообще всё, включая
/// трёхстрочный скрипт на Python, а отладить такой протокол можно, запустив
/// плагин руками в терминале и посмотрев глазами.
///
/// Popup → плагин:
/// <code>
/// {"type":"render"}
/// {"type":"action","id":"...","value":"..."}
/// {"type":"shutdown"}
/// </code>
/// Плагин → Popup:
/// <code>
/// {"type":"ui","nodes":[ ... ]}
/// {"type":"log","message":"..."}
/// </code>
/// Узлы интерфейса описаны в <see cref="UiNode"/>.
/// </summary>
public sealed class PluginInstance(PluginManifest manifest) : Observable
{
    public PluginManifest Manifest { get; } = manifest;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Process? _process;
    private DispatcherTimer? _refresh;

    /// <summary>Последний присланный интерфейс. Пусто — плагин ещё ничего не сказал.</summary>
    public IReadOnlyList<UiNode> Nodes { get; private set; } = [];

    private string _status = "не запущен";
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Каждая строка разговора — и туда, и обратно. Это то, что видно во вкладке Dev.</summary>
    public event Action<string>? Traced;

    public void Start()
    {
        if (IsRunning) return;
        try
        {
            var workingDirectory = Path.GetFullPath(Path.Combine(Manifest.Folder, Manifest.Cwd));
            var info = new ProcessStartInfo
            {
                FileName = Manifest.Command,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = Encoding.UTF8,
            };
            foreach (var arg in Manifest.Args) info.ArgumentList.Add(arg);
            foreach (var (key, value) in Manifest.Env) info.Environment[key] = value;
            // Python по умолчанию буферизует stdout, и плагин, написавший ответ,
            // молчал бы до самого выхода. Просить об этом каждого автора
            // плагина — значит закладывать грабли в протокол.
            info.Environment["PYTHONUNBUFFERED"] = "1";
            info.Environment["PYTHONIOENCODING"] = "utf-8";

            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line) _dispatcher.BeginInvoke(() => Receive(line));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                // stderr — это не протокол, а то, что плагин сказал в сторону:
                // трассировка исключения, предупреждение библиотеки. Место ему
                // в журнале, а не в разборщике JSON.
                if (e.Data is { } line) _dispatcher.BeginInvoke(() => Trace($"< [err] {line}"));
            };
            process.Exited += (_, _) => _dispatcher.BeginInvoke(() =>
            {
                Status = $"завершился (код {SafeExitCode(process)})";
                RaiseAll();
            });

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            Status = "работает";
            Trace($"= запущен: {Manifest.Command} {string.Join(' ', Manifest.Args)}");

            Send(new { type = "render" });

            if (Manifest.RefreshMs > 0)
            {
                _refresh = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(Math.Max(250, Manifest.RefreshMs)),
                };
                _refresh.Tick += (_, _) => { if (_isVisible) Send(new { type = "render" }); };
                _refresh.Start();
            }
        }
        catch (Exception e)
        {
            Status = $"не запустился: {e.Message}";
            Log.Error("plugins", $"{Manifest.Id} не запустился", e);
        }
        RaiseAll();
    }

    private static string SafeExitCode(Process process)
    {
        try { return process.ExitCode.ToString(); } catch { return "?"; }
    }

    /// <summary>
    /// Открыта ли вкладка этого плагина. Опрос идёт только пока она видна:
    /// плагин, которого никто не смотрит, не должен раз в секунду ходить в
    /// файловую систему.
    /// </summary>
    private bool _isVisible;
    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (visible) Send(new { type = "render" });
    }

    public void Stop()
    {
        _refresh?.Stop();
        _refresh = null;
        if (_process is not { } process) return;
        try
        {
            if (!process.HasExited)
            {
                Send(new { type = "shutdown" });
                // Даём попрощаться, но недолго: панель закрывается, а не ждёт.
                if (!process.WaitForExit(500)) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e)
        {
            Log.Warn("plugins", $"{Manifest.Id} не остановился: {e.Message}");
        }
        finally
        {
            process.Dispose();
            _process = null;
            Status = "остановлен";
            RaiseAll();
        }
    }

    // MARK: - Разговор

    public void Send(object message)
    {
        if (_process is not { HasExited: false } process) return;
        try
        {
            var line = JsonSerializer.Serialize(message);
            Trace($"> {line}");
            process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
        }
        catch (Exception e)
        {
            Log.Warn("plugins", $"{Manifest.Id}: не отправить сообщение: {e.Message}");
        }
    }

    public void Act(string id, string? value) => Send(new { type = "action", id, value });

    private void Receive(string line)
    {
        Trace($"< {line}");
        if (line.Trim().Length == 0) return;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

            switch (type)
            {
                case "ui":
                    Nodes = UiNode.ParseList(root.TryGetProperty("nodes", out var nodes) ? nodes : default);
                    RaiseAll();
                    break;
                case "log":
                    Log.Info($"plugin:{Manifest.Id}", root.TryGetProperty("message", out var m)
                        ? m.GetString() ?? "" : line);
                    break;
                default:
                    Trace($"= неизвестный тип сообщения: {type ?? "(нет)"}");
                    break;
            }
        }
        catch (JsonException)
        {
            // Плагин напечатал не JSON. Это нормально во время разработки —
            // отладочный print попадает в тот же поток, — поэтому строка
            // просто ложится в журнал вкладки Dev, а не роняет плагин.
            Trace("= строка не разобрана как JSON");
        }
    }

    private void Trace(string line) => Traced?.Invoke($"[{Manifest.Id}] {line}");
}
