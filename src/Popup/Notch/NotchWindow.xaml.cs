using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Popup.Model;
using Popup.UI;

namespace Popup.Notch;

/// <summary>
/// Окно панели. Порт связки <c>NotchPanel</c> + <c>NotchContentView</c>.
///
/// Окно живёт не по обычным правилам: поверх всех, без фокуса, без места на
/// панели задач и почти всё время прозрачное для кликов. Всё это выставляется
/// по хэндлу — см. <see cref="Win32"/>.
/// </summary>
public partial class NotchWindow : Window
{
    private NotchGeometry _geometry;
    private readonly NotchViewModel _vm;

    /// <summary>
    /// Кто делает содержимое вкладки и что показать в правом углу шапки.
    ///
    /// Отдано наружу, чтобы окно не знало ни об одном конкретном хранилище: оно
    /// рисует раму, а что внутри — забота контроллера. Приходят параметрами
    /// конструктора, а не свойствами после него: первую вкладку окно рисует ещё
    /// в конструкторе, и присвоенная позже фабрика опоздала бы ровно на неё —
    /// то есть на ту единственную, которую видно сразу после запуска.
    /// </summary>
    private readonly Func<PanelTab, UIElement?> _paneFactory;
    private readonly Func<PanelTab, UIElement?> _headerTrailingFactory;

    public event Action? DragEntered;
    public event Action? DragExited;
    public event Func<IReadOnlyList<string>, bool>? FilesDropped;

    /// <summary>Клик по панели — повод снова попросить клавиатуру.</summary>
    public event Action? Pressed;

    /// <summary>Нажали Esc и никто из вкладок его себе не забрал.</summary>
    public event Action? Dismissed;

    /// <summary>Пришло <c>WM_CLIPBOARDUPDATE</c>: в буфере что-то новое.</summary>
    public event Action? ClipboardUpdated;

    /// <summary>Идёт ли перетаскивание прямо сейчас.</summary>
    public bool IsReceivingDrag { get; private set; }

    public NotchWindow(
        NotchViewModel vm,
        NotchGeometry geometry,
        Func<PanelTab, UIElement?> paneFactory,
        Func<PanelTab, UIElement?> headerTrailingFactory)
    {
        _vm = vm;
        _geometry = geometry;
        _paneFactory = paneFactory;
        _headerTrailingFactory = headerTrailingFactory;
        InitializeComponent();

        LeftRailHost.Content = new Rail(vm, vm.LeftRail);
        RightRailHost.Content = new Rail(vm, vm.RightRail, observeChanges: true);

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NotchViewModel.Tab) or "") UpdateTab();
        };

        PreviewMouseDown += (_, _) => Pressed?.Invoke();

        // Не Preview: сначала свой шанс получают вкладки. Переводчику Esc
        // нужнее — он очищает поле, — и только когда очищать нечего, нажатие
        // доходит сюда и закрывает панель.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Dismissed?.Invoke();
            e.Handled = true;
        };

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        UpdateTab();
        ApplyState(_vm.State, animate: false);
    }

    // MARK: - Хэндл и стили

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = Win32.HandleOf(this);

        // Панель не участвует в жизни обычных окон: её нет в Alt-Tab, она не
        // забирает фокус у того, с чем сейчас работают, и пропускает клики
        // насквозь везде, кроме собственного тела.
        Win32.ToggleExStyle(hwnd, Win32.WS_EX_TOOLWINDOW, true);
        Win32.ToggleExStyle(hwnd, Win32.WS_EX_NOACTIVATE, true);
        Win32.ToggleExStyle(hwnd, Win32.WS_EX_TRANSPARENT, true);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        Win32.AddClipboardFormatListener(hwnd);

        ApplyGeometry(_geometry);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_CLIPBOARDUPDATE) ClipboardUpdated?.Invoke();
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        var hwnd = Win32.HandleOf(this);
        if (hwnd != IntPtr.Zero) Win32.RemoveClipboardFormatListener(hwnd);
        base.OnClosed(e);
    }

    // MARK: - Положение

    public void ApplyGeometry(NotchGeometry geometry)
    {
        _geometry = geometry;
        var hwnd = Win32.HandleOf(this);
        if (hwnd == IntPtr.Zero) return;

        var r = geometry.WindowRectPx;
        Win32.SetWindowPos(
            hwnd, Win32.HWND_TOPMOST,
            (int)Math.Round(r.Left), (int)Math.Round(r.Top),
            (int)Math.Round(r.Width), (int)Math.Round(r.Height),
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Пропускать ли клики насквозь. Прямой аналог
    /// <c>panel.ignoresMouseEvents</c>: всё за пределами видимой панели обязано
    /// доходить до приложения под ней.
    /// </summary>
    public bool ClickThrough
    {
        set
        {
            var hwnd = Win32.HandleOf(this);
            if (hwnd != IntPtr.Zero) Win32.ToggleExStyle(hwnd, Win32.WS_EX_TRANSPARENT, value);
        }
    }

    /// <summary>
    /// Отдать панели клавиатуру или вернуть её обратно.
    ///
    /// <c>WS_EX_NOACTIVATE</c> снимается только на это время: окно, которое
    /// нельзя активировать, не получит ни одного нажатия клавиши, а окно,
    /// которое можно, крадёт фокус у того, с чем работает человек. Ни то ни
    /// другое не годится постоянно.
    /// </summary>
    public bool AcceptsKeyboard
    {
        set
        {
            var hwnd = Win32.HandleOf(this);
            if (hwnd == IntPtr.Zero) return;
            Win32.ToggleExStyle(hwnd, Win32.WS_EX_NOACTIVATE, !value);
            if (value)
            {
                Win32.SetForegroundWindow(hwnd);
                Activate();
                MoveFocusIntoPane();
            }
        }
    }

    private void MoveFocusIntoPane()
    {
        // Первое поле ввода в открытой панели — то, ради которого клавиатуру и
        // просили. Искать его здесь, а не в каждой вкладке отдельно: правило
        // одно и то же для переводчика, сниппетов и заметок.
        var field = FindVisualChild<TextBox>(Panes);
        if (field is null) return;
        field.Focus();
        Keyboard.Focus(field);
        field.CaretIndex = field.Text.Length;
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    // MARK: - Раскрытие

    private PanelState _appliedState = PanelState.Collapsed;

    public void ApplyState(PanelState state, bool animate = true)
    {
        if (_appliedState == state && animate) return;
        _appliedState = state;

        var body = _geometry.BodySize(state);
        var topRadius = Theme.TopRadius(state);
        var bottomRadius = Theme.BottomRadius(state);
        var shapeWidth = body.Width + 2 * topRadius;

        var expanded = state == PanelState.Expanded;
        var showsActivity = state == PanelState.Activity;

        if (expanded)
        {
            // Содержимое возвращается в дерево до анимации: раскрывающаяся
            // панель, внутри которой ещё пусто, — это кадр пустоты в самом
            // начале движения.
            Header.Visibility = Visibility.Visible;
            ContentArea.Visibility = Visibility.Visible;
        }
        if (showsActivity) ActivityHost.Visibility = Visibility.Visible;

        if (!animate)
        {
            PanelBody.Width = body.Width;
            PanelBody.Height = body.Height;
            Shape.Width = shapeWidth;
            Shape.TopRadius = topRadius;
            Shape.BottomRadius = bottomRadius;
            Shadow.Opacity = expanded ? 0.5 : 0;
            Header.Opacity = ContentArea.Opacity = expanded ? 1 : 0;
            ActivityHost.Opacity = showsActivity ? 1 : 0;
            if (!expanded) HideChrome();
            if (!showsActivity) ActivityHost.Visibility = Visibility.Collapsed;
            return;
        }

        PanelBody.BeginAnimation(WidthProperty, Theme.Spring(body.Width));
        PanelBody.BeginAnimation(HeightProperty, Theme.Spring(body.Height));
        Shape.BeginAnimation(WidthProperty, Theme.Spring(shapeWidth));
        Shape.BeginAnimation(NotchShape.TopRadiusProperty, Theme.Spring(topRadius));
        Shape.BeginAnimation(NotchShape.BottomRadiusProperty, Theme.Spring(bottomRadius));
        Shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            Theme.Fade(expanded ? 0.5 : 0, Theme.OpenDuration));

        var chromeFade = Theme.Fade(expanded ? 1 : 0, Theme.ContentDuration);
        if (!expanded)
        {
            // Снимаем содержимое с раскладки только после того, как оно
            // погасло: иначе панель на глазах у всех схлопывается пустой.
            chromeFade.Completed += (_, _) =>
            {
                if (_appliedState != PanelState.Expanded) HideChrome();
            };
        }
        Header.BeginAnimation(OpacityProperty, chromeFade);
        ContentArea.BeginAnimation(OpacityProperty, Theme.Fade(expanded ? 1 : 0, Theme.ContentDuration));

        var activityFade = Theme.Fade(showsActivity ? 1 : 0, Theme.ContentDuration);
        if (!showsActivity)
        {
            activityFade.Completed += (_, _) =>
            {
                if (_appliedState != PanelState.Activity) ActivityHost.Visibility = Visibility.Collapsed;
            };
        }
        ActivityHost.BeginAnimation(OpacityProperty, activityFade);
    }

    /// <summary>
    /// Свёрнутая панель не должна ничего считать. Спрятанное содержимое не
    /// участвует ни в замерах, ни в раскладке — а панель проводит свёрнутой
    /// почти всё время своей жизни.
    /// </summary>
    private void HideChrome()
    {
        Header.Visibility = Visibility.Collapsed;
        ContentArea.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Кто рисует полоску активности — в свёрнутом виде и в шапке. Две разные
    /// сущности: у одной есть кнопки, у другой нет.
    /// </summary>
    public void SetActivityViews(UIElement? strip, UIElement? headerChip)
    {
        ActivityHost.Content = strip;
        HeaderActivity.Content = headerChip;
    }

    // MARK: - Вкладка

    private void UpdateTab()
    {
        TitleText.Text = _vm.Tab.Title.ToUpperInvariant();
        HeaderTrailing.Content = _headerTrailingFactory(_vm.Tab);
        Panes.Show(_paneFactory(_vm.Tab));
    }

    /// <summary>Перерисовать правый угол шапки, не трогая саму панель.</summary>
    public void RefreshHeaderTrailing() =>
        HeaderTrailing.Content = _headerTrailingFactory(_vm.Tab);

    // MARK: - Перетаскивание

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        IsReceivingDrag = true;
        DragEntered?.Invoke();
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        IsReceivingDrag = false;
        DragExited?.Invoke();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        IsReceivingDrag = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            FilesDropped?.Invoke(paths);
        e.Handled = true;
    }
}
