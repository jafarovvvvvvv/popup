using System.Windows.Media.Animation;

namespace Popup.UI;

/// <summary>
/// Числовая половина темы — то, что читает код, а не разметка.
/// Порт констант и анимаций из <c>Theme.swift</c>.
/// </summary>
public static class Theme
{
    public const double CollapsedTopRadius = 6;
    public const double CollapsedBottomRadius = 9;
    public const double ActivityTopRadius = 9;
    public const double ActivityBottomRadius = 17;
    public const double OpenTopRadius = 12;
    public const double OpenBottomRadius = 22;

    public static double TopRadius(Notch.PanelState state) => state switch
    {
        Notch.PanelState.Expanded => OpenTopRadius,
        Notch.PanelState.Activity => ActivityTopRadius,
        _ => CollapsedTopRadius,
    };

    public static double BottomRadius(Notch.PanelState state) => state switch
    {
        Notch.PanelState.Expanded => OpenBottomRadius,
        Notch.PanelState.Activity => ActivityBottomRadius,
        _ => CollapsedBottomRadius,
    };

    /// <summary>
    /// Раскрытие и схлопывание. В оригинале это
    /// <c>spring(response: 0.27, dampingFraction: 0.82)</c> — пружина со слабым
    /// перелётом. В WPF пружин нет, поэтому берём <see cref="BackEase"/> с малой
    /// амплитудой: панель чуть проскакивает конечный размер и возвращается,
    /// как и на macOS.
    /// </summary>
    public static Duration OpenDuration => new(TimeSpan.FromSeconds(0.30));

    public static IEasingFunction OpenEasing => new BackEase
    {
        Amplitude = 0.12,
        EasingMode = EasingMode.EaseOut,
    };

    /// <summary>Появление и исчезновение содержимого внутри шапки.</summary>
    public static Duration ContentDuration => new(TimeSpan.FromSeconds(0.16));

    /// <summary>
    /// Смена вкладок: уходящая панель исчезает быстрее, чем приходит новая, —
    /// так они никогда не висят обе полупрозрачными.
    /// </summary>
    public static Duration PaneInDuration => new(TimeSpan.FromSeconds(0.20));
    public static Duration PaneOutDuration => new(TimeSpan.FromSeconds(0.12));
    public static TimeSpan PaneInDelay => TimeSpan.FromSeconds(0.04);

    /// <summary>
    /// Задержка переключения по наведению на рейле.
    ///
    /// Достаточно длинная, чтобы проглотить мышь, идущую мимо, и достаточно
    /// короткая, чтобы намеренное наведение отвечало как будто сразу.
    /// </summary>
    public static TimeSpan RailDwell => TimeSpan.FromMilliseconds(150);

    public static DoubleAnimation Spring(double to) => new()
    {
        To = to,
        Duration = OpenDuration,
        EasingFunction = OpenEasing,
        FillBehavior = FillBehavior.HoldEnd,
    };

    /// <summary>
    /// Появление и исчезновение.
    ///
    /// Задержка подставляется нулём, а не <c>null</c>: у <c>Timeline</c>
    /// пустой <c>BeginTime</c> означает «не запускать никогда», а вовсе не
    /// «начать сразу», и такая анимация молча не проигрывается.
    /// </summary>
    public static DoubleAnimation Fade(double to, Duration duration, TimeSpan? delay = null) => new()
    {
        To = to,
        Duration = duration,
        BeginTime = delay ?? TimeSpan.Zero,
        FillBehavior = FillBehavior.HoldEnd,
    };
}
