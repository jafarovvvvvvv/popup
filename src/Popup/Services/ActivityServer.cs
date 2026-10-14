using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace Popup.Services;

/// <summary>
/// Дверь для чужих процессов: именованный канал <c>\\.\pipe\popup</c>.
///
/// Плагины Popup запускает сам, и разговор с ними идёт сверху вниз. Здесь всё
/// наоборот: процесс уже работает по своим причинам — распознаёт речь, качает
/// файл, собирает проект — и ему нужно сказать панели пару слов. Заводить ради
/// этого плагин было бы вывернуто наизнанку: тогда панель запускала бы то, что
/// и так запущено.
///
/// Канал доступен только текущему пользователю: это тот же уровень доверия, что
/// и папка плагинов, — свои процессы на своей машине.
///
/// Протокол — построчный JSON в обе стороны, как у плагинов, чтобы не заводить
/// в одном приложении два разных языка.
///
/// <code>
/// {"type":"activity","source":"localwhisper","name":"Whisper",
///  "tone":"live","label":"0:07","actions":[{"id":"stop","label":"Стоп"}]}
/// {"type":"activity","source":"localwhisper","tone":"idle"}
/// {"type":"clip","text":"…"}          — положить в буфер и в историю
/// {"type":"shelf","paths":["C:\\…"]}  — положить на полку
/// {"type":"note","text":"…"}          — завести заметку
/// </code>
/// Обратно уезжает только нажатие на полоске:
/// <code>
/// {"type":"action","id":"stop"}
/// </code>
/// </summary>
public sealed class ActivityServer : IDisposable
{
    public const string PipeName = "popup";

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Живые подключения. Их несколько: одновременно говорить могут и Whisper,
    /// и сборка, и что угодно ещё, а полоску они делят по правилам
    /// <see cref="ActivityStore"/>.
    /// </summary>
    private readonly List<StreamWriter> _clients = [];
    private readonly Lock _clientsGate = new();

    /// <summary>Разбирает одно сообщение. Вызывается уже в потоке интерфейса.</summary>
    public Action<JsonElement>? Received { get; set; }

    public void Start()
    {
        // По одному циклу приёма на подключение: канал в Windows устроен так,
        // что экземпляр сервера обслуживает ровно одного клиента, и следующего
        // надо ждать на новом.
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(_stopping.Token);
                var connected = pipe;
                // Умышленно без await: цикл обязан немедленно вернуться к
                // ожиданию следующего клиента, иначе второй процесс не
                // достучится, пока говорит первый.
                _ = Task.Run(async () => await ServeAsync(connected));
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception e)
            {
                pipe?.Dispose();
                Log.Error("pipe", "не принять подключение", e);
                // Не крутиться вхолостую, если канал занят или запрещён.
                await Task.Delay(2000);
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        lock (_clientsGate) _clients.Add(writer);
        Log.Info("pipe", "клиент подключился");

        // Чьё имя называл этот клиент. Нужно на выходе: полоску надо убрать за
        // тем, кто ушёл, а не за тем, кто в этот момент показывался.
        string? source = null;

        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));

            while (!_stopping.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_stopping.Token);
                if (line is null) break;
                if (line.Trim().Length == 0) continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    // Клон: JsonDocument умрёт вместе с этим витком цикла, а
                    // разбор произойдёт позже и в другом потоке.
                    var message = document.RootElement.Clone();
                    if (message.TryGetProperty("source", out var named) &&
                        named.ValueKind == JsonValueKind.String)
                    {
                        source = named.GetString();
                    }
                    // Умышленно без await: разбор идёт в потоке интерфейса, а
                    // цикл чтения обязан немедленно вернуться к каналу.
                    _ = _dispatcher.BeginInvoke(() => Received?.Invoke(message));
                }
                catch (JsonException)
                {
                    Log.Warn("pipe", $"строка не разобрана: {Shorten(line)}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Панель закрывается.
        }
        catch (IOException)
        {
            // Клиент ушёл, не попрощавшись. Обычное дело.
        }
        catch (Exception e)
        {
            Log.Error("pipe", "разговор оборвался", e);
        }
        finally
        {
            lock (_clientsGate) _clients.Remove(writer);
            Log.Info("pipe", $"клиент отключился: {source ?? "(без имени)"}");
            Disconnected?.Invoke(source);
            try { pipe.Dispose(); } catch { /* уже закрыт */ }
        }
    }

    /// <summary>
    /// Клиент отвалился. Передаётся имя, которым он представлялся, — убрать
    /// надо его полоску, а не ту, что висит сейчас.
    /// </summary>
    public event Action<string?>? Disconnected;

    /// <summary>
    /// Отправить всем подключённым. Адресовать конкретному незачем: команд
    /// отсюда уходит ровно одна — нажали кнопку на полоске, — а кнопку ставит
    /// тот, чья активность сейчас видна.
    /// </summary>
    public void Broadcast(object message)
    {
        var line = JsonSerializer.Serialize(message);
        List<StreamWriter> snapshot;
        lock (_clientsGate) snapshot = [.. _clients];

        foreach (var writer in snapshot)
        {
            try
            {
                writer.WriteLine(line);
            }
            catch
            {
                // Оборвавшееся подключение уберёт из списка его собственный
                // цикл чтения — здесь достаточно не мешать остальным.
            }
        }
    }

    private static string Shorten(string value) => value.Length <= 120 ? value : value[..120] + "…";

    public void Dispose()
    {
        _stopping.Cancel();
        lock (_clientsGate)
        {
            foreach (var writer in _clients)
            {
                try { writer.Dispose(); } catch { /* всё равно закрываемся */ }
            }
            _clients.Clear();
        }
        _stopping.Dispose();
    }
}
