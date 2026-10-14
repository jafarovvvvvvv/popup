using System.Windows.Threading;
using Popup.Model;

namespace Popup.Services;

/// <summary>
/// Что происходит в системе прямо сейчас — то, ради чего панель показывает
/// полоску, не раскрываясь.
///
/// Смысл в том, чтобы такие вещи было видно, не наводя мышь. Идёт запись,
/// крутится расшифровка, качается файл — это надо знать боковым зрением, и это
/// не стоит шестисот точек раскрытой панели поверх работы.
///
/// Кто именно сообщает — панели всё равно: источником может быть любой процесс,
/// дотянувшийся до канала (см. <see cref="ActivityServer"/>).
/// </summary>
public sealed class ActivityStore : Observable
{
    /// <summary>
    /// Тон активности. Он же решает, как полоска выглядит: три состояния —
    /// это ровно то, что человек различает боковым зрением, не читая.
    /// </summary>
    public enum Tone
    {
        /// <summary>Идёт прямо сейчас: красная точка и бегущая волна.</summary>
        Live,
        /// <summary>Работа без участия человека: серые дышащие точки.</summary>
        Busy,
        /// <summary>Получилось: зелёная вспышка с галкой.</summary>
        Ok,
        /// <summary>Не получилось.</summary>
        Fail,
    }

    public sealed record Action(string Id, string Label);

    public sealed record Item(
        string Source,
        string Name,
        Tone Tone,
        string Label,
        IReadOnlyList<Action> Actions,
        DateTime At);

    private readonly DispatcherTimer _autoClear;

    public ActivityStore()
    {
        // Итоговые состояния гаснут сами: «готово», висящее полчаса, перестаёт
        // что-либо значить и превращается в мусор поверх экрана.
        _autoClear = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2.5),
        };
        _autoClear.Tick += (_, _) =>
        {
            _autoClear.Stop();
            Clear(Current?.Source);
        };
    }

    private Item? _current;
    public Item? Current
    {
        get => _current;
        private set
        {
            if (Set(ref _current, value)) Raise(nameof(IsActive));
        }
    }

    public bool IsActive => Current is not null;

    /// <summary>Позвать, когда человек нажал кнопку на полоске.</summary>
    public event System.Action<string, string>? ActionInvoked;

    public void Invoke(string actionId)
    {
        if (Current is { } item) ActionInvoked?.Invoke(item.Source, actionId);
    }

    public void Show(Item item)
    {
        _autoClear.Stop();
        Current = item;
        if (item.Tone is Tone.Ok or Tone.Fail) _autoClear.Start();
    }

    /// <summary>
    /// Убирает активность, но только если её поставил тот же источник.
    ///
    /// Иначе отвалившийся посреди работы процесс уносил бы с собой чужую
    /// полоску — и запись, идущая прямо сейчас, пропадала бы с экрана из-за
    /// того, что где-то в стороне закончилась загрузка.
    /// </summary>
    public void Clear(string? source)
    {
        if (source is not null && Current?.Source != source) return;
        _autoClear.Stop();
        Current = null;
    }
}
