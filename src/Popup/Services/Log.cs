using System.Collections.Concurrent;
using System.Globalization;

namespace Popup.Services;

/// <summary>
/// Журнал приложения.
///
/// Пишет и в файл, и в кольцевой буфер в памяти: файл нужен, когда панель уже
/// упала и её некому спросить, а буфер — вкладке разработки, которая показывает
/// последние строки живьём.
/// </summary>
public static class Log
{
    private const int Capacity = 500;

    private static readonly ConcurrentQueue<string> Recent = new();
    private static readonly Lock FileGate = new();

    /// <summary>Позвать, когда в журнал добавилась строка — на этом живёт вкладка Dev.</summary>
    public static event Action<string>? Written;

    public static IReadOnlyCollection<string> Tail => Recent.ToArray();

    public static void Info(string source, string message) => Write("info", source, message);
    public static void Warn(string source, string message) => Write("warn", source, message);

    public static void Error(string source, string message, Exception? error = null) =>
        Write("error", source, error is null ? message : $"{message}: {error.GetType().Name} {error.Message}");

    private static void Write(string level, string source, string message)
    {
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:HH:mm:ss.fff} {1,-5} {2,-12} {3}",
            DateTime.Now, level, source, message);

        Recent.Enqueue(line);
        while (Recent.Count > Capacity) Recent.TryDequeue(out _);

        Written?.Invoke(line);

        try
        {
            lock (FileGate)
            {
                File.AppendAllText(Path.Combine(AppPaths.Logs, "popup.log"), line + Environment.NewLine);
            }
        }
        catch
        {
            // Журнал, который умеет уронить приложение, хуже отсутствующего.
        }
    }
}
