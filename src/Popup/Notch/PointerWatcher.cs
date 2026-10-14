using System.Windows;
using System.Windows.Threading;

namespace Popup.Notch;

/// <summary>
/// Ведёт состояние наведения, опрашивая положение курсора по таймеру.
/// Порт <c>PointerWatcher.swift</c>.
///
/// События мыши здесь не годятся. Панель почти всё время click-through, то есть
/// её собственные <c>MouseMove</c> до неё не доходят вовсе, а глобальный хук
/// ради наведения — это перехват всего ввода системы ради одной координаты.
/// Опрос <c>GetCursorPos</c> не зависит от того, кто получает события, и потому
/// ведёт себя одинаково поверх любого окна.
///
/// Все прямоугольники и координаты здесь — физические пиксели экрана.
/// </summary>
public sealed class PointerWatcher
{
    /// <summary>Вход сюда раскрывает панель.</summary>
    public Rect OpenRect { get; set; }
    /// <summary>Выход отсюда схлопывает панель.</summary>
    public Rect CloseRect { get; set; }
    /// <summary>
    /// Где панель принимает клики. Везде остальном окно обязано пропускать их
    /// насквозь, так что это же поле переключает <c>WS_EX_TRANSPARENT</c>.
    /// </summary>
    public Rect InteractiveRect { get; set; }

    /// <summary>
    /// Держит панель кликабельной, пока тащат файл, даже если курсор ушёл за
    /// пределы <see cref="InteractiveRect"/> посреди перетаскивания.
    /// </summary>
    public Func<bool> IsDragging { get; set; } = () => false;

    /// <summary>
    /// Раскрыта ли панель прямо сейчас. Читается, а не предполагается: этот
    /// класс следит за курсором, а открывают и закрывают панель ещё и другие
    /// вещи — перетаскивание, трей, вкладка, попросившая клавиатуру.
    /// </summary>
    public Func<bool> IsPanelOpen { get; set; } = () => false;

    /// <summary>
    /// Панель открыта клавишей и держится сама.
    ///
    /// Правило «решает курсор» верно ровно до того момента, когда панель
    /// открыли не курсором. Мышь в этот миг где угодно — обычно далеко, — и
    /// обычная логика закрыла бы панель через треть секунды, не дав прочитать
    /// ни строчки. Пока флаг поднят, слежение только смотрит и молчит.
    /// </summary>
    public Func<bool> IsPinned { get; set; } = () => false;

    /// <summary>
    /// Курсор впервые дошёл до панели, открытой клавишей. С этого мгновения
    /// она снова обычная: пришёл мышью — уйдёшь мышью.
    /// </summary>
    public event Action? PointerArrivedWhilePinned;

    /// <summary>
    /// Достаточно коротко, чтобы ощущалось мгновенным, достаточно длинно, чтобы
    /// мышь, пролетающая вдоль верха экрана, панель не открывала.
    /// </summary>
    public TimeSpan OpenDelay { get; set; } = TimeSpan.FromSeconds(0.3);
    public TimeSpan CloseDelay { get; set; } = TimeSpan.FromSeconds(0.32);

    /// <summary>Полоса у верха экрана, переводящая опрос на полную частоту.</summary>
    public Rect WarmZone { get; set; }

    /// <summary>Гистерезис: насколько ниже полосы курсор должен уйти, чтобы остыть.</summary>
    public double CoolMarginPx { get; set; } = 80;

    /// <summary>
    /// Сколько курсор может простоять неподвижно, прежде чем опрос сбросится до
    /// холостой частоты там же, где он стоит. Движение снова разогревает на
    /// следующем холостом тике.
    /// </summary>
    public TimeSpan RestThreshold { get; set; } = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(1.0 / 60);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(1.0 / 8);

    public event Action<bool>? Changed;
    public event Action<bool>? InteractiveChanged;

    public bool IsInside { get; private set; }

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Input);
    private DateTime? _awaitingSince;
    private bool? _wasInteractive;
    private bool _isWarm;
    private Point _lastPoint = new(-1, -1);
    private DateTime _lastMovedAt = DateTime.MinValue;

    public PointerWatcher()
    {
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => Schedule(warm: false);

    public void Stop()
    {
        _timer.Stop();
        _awaitingSince = null;
        // Последняя отправленная кликабельность умирает вместе с окном, которому
        // её отправляли. Наблюдатель переживает пересборку, а свежее окно
        // начинает click-through; сохранённое «уже кликабельно» проглотило бы
        // первый вызов и оставило новую панель глухой к кликам, пока курсор не
        // сходит наружу и обратно.
        _wasInteractive = null;
    }

    private void Schedule(bool warm)
    {
        _isWarm = warm;
        _timer.Stop();
        _timer.Interval = warm ? FastInterval : IdleInterval;
        _timer.Start();
    }

    /// <summary>
    /// Полная частота только для курсора, который действительно куда-то едет
    /// возле верха экрана; восемь опросов в секунду в остальное время.
    ///
    /// Два условия, и нужны оба. Место: верхняя полоса — или где угодно, пока
    /// панель раскрыта, — с гистерезисом. Движение: курсор должен был недавно
    /// двигаться, потому что припаркованный курсор никуда не едет, как близко к
    /// панели он бы ни стоял. Именно это и даёт нулевой процессор в покое.
    /// </summary>
    private void UpdateRate(Point point)
    {
        if (point != _lastPoint)
        {
            _lastPoint = point;
            _lastMovedAt = DateTime.UtcNow;
        }

        var moving = DateTime.UtcNow - _lastMovedAt < RestThreshold;
        bool near;
        if (IsInside) near = true;
        else if (_isWarm) near = point.Y <= WarmZone.Bottom + CoolMarginPx;
        else near = WarmZone.Contains(point);

        var warm = near && moving;
        if (warm != _isWarm) Schedule(warm);
    }

    /// <summary>Задать состояние принудительно — например, при переключении из трея.</summary>
    public void SetInside(bool value)
    {
        _awaitingSince = null;
        IsInside = value;
    }

    private void Tick()
    {
        var point = Win32.CursorPosition();
        UpdateRate(point);

        var interactive = IsDragging() || InteractiveRect.Contains(point);
        if (_wasInteractive != interactive)
        {
            _wasInteractive = interactive;
            InteractiveChanged?.Invoke(interactive);
        }

        var inside = (IsInside ? CloseRect : OpenRect).Contains(point);

        // Панель держится клавишей. Единственное, что здесь происходит, —
        // ожидание курсора: дошёл до панели, значит дальше она снова живёт по
        // обычным правилам.
        if (IsPinned())
        {
            if (!inside && !CloseRect.Contains(point)) return;
            _awaitingSince = null;
            IsInside = true;
            PointerArrivedWhilePinned?.Invoke();
            return;
        }

        // Раскрыта ли панель и где курсор — два разных факта, и здесь ведётся
        // только второй. Они должны совпадать, но каждый путь, открывающий
        // панель без курсора — перетаскивание, трей, вкладка, забравшая
        // клавиатуру, — это шанс им разойтись, и стоящая открытой панель при
        // мыши в другом конце экрана всегда одна и та же неприятность. Курсор
        // здесь главный, поэтому он и переспрашивает.
        //
        // Перетаскивание — единственный случай, когда панель обязана стоять
        // открытой без курсора: её для того и раскрыли, чтобы на неё бросили.
        if (!inside && !IsInside && !IsDragging() && IsPanelOpen())
        {
            if (_awaitingSince is not { } pending)
            {
                _awaitingSince = DateTime.UtcNow;
                return;
            }
            if (DateTime.UtcNow - pending < CloseDelay) return;
            _awaitingSince = null;
            Changed?.Invoke(false);
            return;
        }

        if (inside == IsInside)
        {
            _awaitingSince = null;
            return;
        }

        if (_awaitingSince is not { } start)
        {
            _awaitingSince = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - start < (inside ? OpenDelay : CloseDelay)) return;

        _awaitingSince = null;
        IsInside = inside;
        Changed?.Invoke(inside);
    }
}
