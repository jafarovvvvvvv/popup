using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Popup.Model;
using Popup.Services;
using Popup.UI;

namespace Popup.Notch;

/// <summary>
/// Собирает панель и решает, когда она раскрыта. Порт <c>NotchController.swift</c>.
/// </summary>
public sealed class NotchController
{
    private readonly Settings _settings;
    private readonly PointerWatcher _pointer = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private NotchWindow? _window;
    private NotchViewModel? _vm;
    private DispatcherTimer? _shrinkTimer;
    private ActivityServer? _server;
    private SettingsWatcher? _settingsWatcher;
    private readonly Hotkey _hotkey;

    /// <summary>
    /// Монотонная метка для отложенной половины закрытия: любое более новое
    /// открытие или закрытие делает уже запущенное устаревшим.
    /// </summary>
    private int _openGeneration;

    public NotchController(Settings settings)
    {
        _settings = settings;
        _appliedLanguage = settings.Language;
        _hotkey = new Hotkey(Toggle);
    }

    public NotchViewModel? ViewModel => _vm;
    public PrivacyMode? Privacy => _vm?.Privacy;

    /// <summary>
    /// Кто делает содержимое вкладок. Ставится снаружи — контроллер отвечает за
    /// раму и за то, когда она открыта, а не за то, что показано внутри.
    /// </summary>
    public Func<NotchViewModel, PanelTab, UIElement?>? PaneFactory { get; set; }
    public Func<NotchViewModel, PanelTab, UIElement?>? HeaderTrailingFactory { get; set; }

    // MARK: - Установка

    public void Install()
    {
        Build();

        // Канал живёт дольше окна: пересборка панели при смене монитора не
        // должна ронять подключения, которые в этот момент что-то сообщают.
        _server = new ActivityServer();
        _server.Received += message =>
        {
            // Каждое сообщение — в журнал: без этого чужой процесс, который
            // «вроде отправил, а панель молчит», отлаживается вслепую с обеих
            // сторон сразу.
            Log.Info("pipe", message.ToString());
            if (_vm is { } vm) ExternalCommands.Apply(vm, message);
        };
        // Процесс, оборвавшийся посреди работы, уносит с собой и свою полоску:
        // «идёт запись» без того, кто записывает, — это не состояние, а мусор.
        // Именно свою: чужую он трогать не должен.
        _server.Disconnected += source =>
        {
            // Клиент, который так и не назвался, ничего и не показывал —
            // убирать за ним нечего, а сбросить он мог бы чужое.
            if (source is null) return;
            _dispatcher.BeginInvoke(() => _vm?.Activity.Clear(source));
        };
        _server.Start();

        _settingsWatcher = new SettingsWatcher(_settings);
        _settingsWatcher.Changed += SettingsChanged;

        SystemEvents.DisplaySettingsChanged += (_, _) => _dispatcher.Invoke(ScreenParametersChanged);

        // Погасший или заблокированный экран смотреть некому, поэтому
        // единственный таймер, который иначе не останавливается никогда —
        // опрос курсора, — останавливается вместе с ним. Панель тоже
        // схлопывается, так что пробуждение всегда начинается со сложенного
        // состояния.
        SystemEvents.SessionSwitch += (_, e) => _dispatcher.Invoke(() =>
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.RemoteDisconnect:
                    Sleep();
                    break;
                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.RemoteConnect:
                    _pointer.Start();
                    break;
            }
        });

        SystemEvents.PowerModeChanged += (_, e) => _dispatcher.Invoke(() =>
        {
            if (e.Mode == PowerModes.Suspend) Sleep();
            else if (e.Mode == PowerModes.Resume) _pointer.Start();
        });
    }

    /// <summary>
    /// Файл настроек поправили руками. Применяем то, что можно применить на
    /// ходу; язык — единственное, ради чего панель пересобирается целиком:
    /// подписи разложены по уже построенным элементам, и менять их поштучно
    /// значило бы держать список всех мест, где они встречаются.
    /// </summary>
    private void SettingsChanged()
    {
        var languageChanged = _appliedLanguage != _settings.Language;
        _appliedLanguage = _settings.Language;

        if (languageChanged)
        {
            Strings.Configure(_settings.Language);
            Rebuild();
            return;
        }

        // Клавишу перевешиваем всегда: сочетание могло смениться, а могло и
        // остаться — перерегистрация стоит доли миллисекунды.
        if (_window is { } window) _hotkey.Register(Win32.HandleOf(window), _settings.ToggleHotkey);

        // Календарь мог только что получить ключи: пусть перепроверит себя, а
        // открытая вкладка перерисуется.
        _vm?.Calendar.RefreshAccess();
        _vm?.Calendar.RaiseAll();
    }

    private string? _appliedLanguage;

    private void Sleep()
    {
        SetOpen(false);
        _pointer.SetInside(false);
        _pointer.Stop();
    }

    public void Teardown()
    {
        _hotkey.Dispose();
        _settingsWatcher?.Dispose();
        _server?.Dispose();
        _pointer.Stop();
        _shrinkTimer?.Stop();
        _vm?.Stop();
        if (_window is { } window)
        {
            window.AcceptsKeyboard = false;
            window.Close();
        }
    }

    /// <summary>
    /// Панель открыта не курсором — клавишей или из трея — и держится сама,
    /// пока с ней не закончат.
    /// </summary>
    private bool _pinned;

    /// <summary>
    /// Переключить панель клавишей или из трея.
    ///
    /// Открытая так панель закрепляется: мышь в этот момент где угодно, и
    /// обычное «решает курсор» захлопнуло бы её через треть секунды. Открепится
    /// она сама — как только курсор до неё дойдёт, — либо по Esc, либо по
    /// повторному нажатию.
    /// </summary>
    public void Toggle()
    {
        if (_vm is null) return;

        if (_vm.IsOpen)
        {
            Unpin();
            SetOpen(false);
            _pointer.SetInside(false);
            return;
        }

        _pinned = true;
        SetOpen(true);
        _pointer.SetInside(true);
    }

    /// <summary>Вернуть панель под власть курсора.</summary>
    private void Unpin() => _pinned = false;

    /// <summary>Esc: закрыть закреплённую панель и отдать клавиатуру.</summary>
    public void Dismiss()
    {
        if (_vm is null) return;
        Unpin();
        _pointer.SetInside(false);
        SetOpen(false);
    }

    // MARK: - Сборка

    private void ScreenParametersChanged()
    {
        var fresh = NotchGeometry.Current();
        if (_vm is null || !_vm.Geometry.Matches(fresh))
        {
            Rebuild();
            return;
        }
        // Тот же монитор в том же масштабе: панель и всё, что на ней, остаются.
        _window?.ApplyGeometry(fresh);
    }

    private void Rebuild()
    {
        var previousTab = _vm?.Tab;
        _pointer.Stop();
        _shrinkTimer?.Stop();
        _vm?.Stop();
        _window?.Close();
        _window = null;
        _vm = null;

        Build();

        if (previousTab is not null && _vm is not null)
        {
            // Ищем по имени, а не по ссылке: правый рейл после пересборки
            // состоит из новых объектов, если его успели достроить плагины.
            var restored = _vm.AllTabs.FirstOrDefault(t => t.Id == previousTab.Id);
            if (restored is not null) _vm.Tab = restored;
        }
    }

    private void Build()
    {
        var geometry = NotchGeometry.Current();
        var vm = new NotchViewModel(geometry, _settings);
        _vm = vm;

        var window = new NotchWindow(
            vm, geometry,
            tab => PaneFactory?.Invoke(vm, tab),
            tab => HeaderTrailingFactory?.Invoke(vm, tab));
        _window = window;

        window.DragEntered += () =>
        {
            vm.Tab = PanelTab.Shelf;
            vm.IsDropTargeted = true;
            SetOpen(true);
        };
        window.DragExited += () =>
        {
            vm.IsDropTargeted = false;
            // После ухода перетаскивания курсор обычно уже не над панелью.
            ScheduleCollapseIfPointerAway();
        };
        window.FilesDropped += paths =>
        {
            vm.IsDropTargeted = false;
            var accepted = vm.Accept(paths);
            _pointer.SetInside(true);
            SetOpen(true);
            ScheduleCollapseIfPointerAway();
            return accepted;
        };
        window.ClipboardUpdated += vm.Clipboard.Refresh;

        // Счётчики и имя источника в шапке живут в хранилищах, а не во вкладке.
        vm.StoresChanged += window.RefreshHeaderTrailing;

        // Полоска активности рисуется дважды: телом свёрнутой панели без
        // кнопок и чипом в шапке раскрытой — с ними.
        window.SetActivityViews(
            new ActivityStrip(vm.Activity, interactive: false),
            new ActivityStrip(vm.Activity, interactive: true));

        // Появление и исчезновение активности меняет размер панели ровно так
        // же, как наведение, — через то же место.
        vm.StateChanged += state =>
        {
            if (!vm.IsExpanded) window.ApplyState(state);
            ApplyActiveRect(open: vm.IsExpanded);
            UpdateHoverRects();
        };

        // Нажатие на кнопку полоски уезжает тому, кто эту активность поставил.
        vm.Activity.ActionInvoked += (source, action) =>
            _server?.Broadcast(new { type = "action", source, id = action });

        // Клик мимо панели снимает клавиатуру, но оставляет вкладку на месте,
        // поэтому клик обратно в панель должен уметь попросить её снова.
        window.Pressed += () =>
        {
            if (vm.Tab.NeedsKeyboard) vm.WantsKeyboard = true;
        };

        // Уход фокуса в другое приложение снимает клавиатуру. Ловить «клик
        // снаружи» негде, но потеря активности говорит о том же. Вкладка
        // остаётся какой была — сбрасывается только притязание на клавиатуру.
        window.Deactivated += (_, _) => vm.WantsKeyboard = false;

        // Ведётся не по тому, какая вкладка показана, а по осознанной просьбе:
        // наведение может попасть на вкладку с полем прямо сейчас, и одно это
        // не повод отнимать клавиатуру у окна под панелью.
        vm.KeyboardWanted += SetKeyboard;

        window.Show();
        // После Show у окна появился хэндл — только теперь на него можно
        // повесить клавишу. Пересборка панели перевешивает её на новое окно.
        _hotkey.Register(Win32.HandleOf(window), _settings.ToggleHotkey);
        ApplyActiveRect(open: false);

        UpdateHoverRects();
        _pointer.CloseRect = geometry.ExpandedHoverRectPx;
        _pointer.WarmZone = geometry.WarmZonePx;
        _pointer.CoolMarginPx = geometry.ToPx(80);
        // Настоящий вырез — это дыра: под ним ничего нет, и открыть его сразу
        // ничего не стоит. Здесь панель лежит поверх работающего экрана, и
        // курсор, идущий вдоль верхнего края, обычно идёт мимо. Просьбой
        // раскрыться считается остановка.
        _pointer.OpenDelay = TimeSpan.FromSeconds(0.3);
        _pointer.IsDragging = () => window.IsReceivingDrag;
        _pointer.IsPanelOpen = () => vm.IsOpen;
        _pointer.IsPinned = () => _pinned;
        _pointer.PointerArrivedWhilePinned += Unpin;
        _pointer.Changed += SetOpen;

        // Esc — общий выход. Вкладки, которым он нужнее (переводчик очищает
        // поле), забирают его себе первыми и до сюда не пускают.
        window.Dismissed += Dismiss;
        // Всё за пределами видимой панели обязано дойти до приложения под ней.
        _pointer.InteractiveChanged += interactive => window.ClickThrough = !interactive;
        _pointer.Start();

        vm.Start();

        // Пересобранная панель начинает свёрнутой. Если курсор уже стоит на
        // ней, раскрываем сразу, а не ждём поездки к верхнему краю и обратно.
        if (geometry.ExpandedHoverRectPx.Contains(Win32.CursorPosition()))
        {
            _pointer.SetInside(true);
            SetOpen(true);
        }
    }

    // MARK: - Открытие и закрытие

    /// <summary>Отдать панели клавиатуру или вернуть её обратно.</summary>
    private void SetKeyboard(bool wants)
    {
        if (_window is null) return;
        if (wants)
        {
            SetOpen(true);
            _pointer.SetInside(true);
        }
        _window.AcceptsKeyboard = wants;
        // Набранное остаётся: отойти посмотреть что-то — не то же самое, что
        // выбросить текст.
        if (!wants) ScheduleCollapseIfPointerAway();
    }

    /// <summary>
    /// Решает всегда курсор. Поле с текстом внутри не удерживает панель
    /// открытой: её открывают наведением, и всё, что пережило уход курсора,
    /// пришлось бы закрывать как-то ещё — а это второе правило для панели, у
    /// которой правило ровно одно. Набранное сохраняется, так что возвращение
    /// находит текст там, где его оставили.
    /// </summary>
    private void SetOpen(bool open)
    {
        if (_vm is not { } vm || _window is not { } window || vm.IsOpen == open) return;

        _openGeneration++;
        _shrinkTimer?.Stop();

        if (open)
        {
            // Кликабельная область растёт первой, чтобы курсор не провалился
            // сквозь участок, который анимация ещё не успела накрыть.
            ApplyActiveRect(open: true);
            vm.IsOpen = true;
            window.ApplyState(PanelState.Expanded);
            // Часы полоски перемотки идут только пока их видно.
            vm.Media.SetActive(true);
            vm.Calendar.SetActive(true);
            window.RefreshHeaderTrailing();
        }
        else
        {
            // Закрываемся — значит закрепление больше не действует.
            Unpin();
            // Клавиатура отпускается первой, схлопывание — вторым, и никогда не
            // в одном проходе. В оригинале это лечило панель, застывавшую
            // раскрытой: снятие фокуса с поля и удаление самого поля,
            // случившиеся в одной транзакции, применяли состояние, но теряли
            // перерисовку.
            vm.WantsKeyboard = false;
            var generation = _openGeneration;
            _dispatcher.BeginInvoke(() =>
            {
                if (_openGeneration == generation) Collapse();
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>Видимая половина закрытия, проходом позже отпускания клавиатуры.</summary>
    private void Collapse()
    {
        if (_vm is not { } vm || _window is not { } window || !vm.IsOpen) return;

        // Что открыли руками, снова уходит под укрытие вместе с панелью.
        // Следующее наведение — то, которого никто не планировал, и оно не
        // должно раскрыться на строку, показанную десять минут назад.
        vm.Privacy.CoverEverything();
        vm.IsOpen = false;
        // Схлопывается не обязательно в полоску: если пока панель была
        // раскрыта, снаружи началась запись, ей есть что показывать и дальше.
        window.ApplyState(vm.State);
        vm.Media.SetActive(false);
        vm.Calendar.SetActive(false);

        // Кликабельную область ужимаем только после того, как панель доехала.
        // Сделать это раньше — оставить промежуток, в котором клики попадают в
        // то, что за панелью, пока она ещё видна.
        _shrinkTimer?.Stop();
        _shrinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.45) };
        _shrinkTimer.Tick += (_, _) =>
        {
            _shrinkTimer?.Stop();
            ApplyActiveRect(open: false);
        };
        _shrinkTimer.Start();
    }

    private void ScheduleCollapseIfPointerAway()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_vm is not { } vm) return;
            // Пересинхронизируемся в любом случае. Курсор, который всё ещё на
            // панели, обязан быть записан как внутри, иначе слежение так и
            // будет считать, что он ушёл, и панель зависнет открытой до
            // следующего касания верхнего края.
            var away = !vm.Geometry.ExpandedHoverRectPx.Contains(Win32.CursorPosition());
            _pointer.SetInside(!away);
            if (away) SetOpen(false);
        };
        timer.Start();
    }

    /// <summary>
    /// Куда надо привести курсор, чтобы панель раскрылась.
    ///
    /// Пока висит полоска активности, целью становится она сама: она заметно
    /// больше свёрнутой полоски и стоит ровно там же, и требовать попасть в
    /// шесть точек под ней значило бы прятать панель за тем, что её же и
    /// показывает.
    /// </summary>
    private void UpdateHoverRects()
    {
        if (_vm is not { } vm) return;
        _pointer.OpenRect = vm.State == PanelState.Activity
            ? vm.Geometry.ActivityHoverRectPx
            : vm.Geometry.HoverRectPx;
    }

    private void ApplyActiveRect(bool open)
    {
        if (_vm is not { } vm) return;
        // Свёрнутая панель кликов не принимает вообще — ни пустая, ни с
        // полоской активности. Иначе полоска, стоящая поверх чужого окна,
        // начала бы съедать клики по нему.
        var size = vm.Geometry.BodySize(open ? PanelState.Expanded : PanelState.Collapsed);
        var rect = vm.Geometry.ContentScreenRectPx(size);
        if (open)
        {
            // Запас, чтобы плечи оставались нажимаемыми. Никогда в свёрнутом
            // виде: там это съедало бы клики рядом с полоской.
            var slack = vm.Geometry.ToPx(Theme.OpenTopRadius);
            rect = new Rect(rect.Left - slack, rect.Top, rect.Width + 2 * slack, rect.Height);
        }
        _pointer.InteractiveRect = rect;
    }
}
