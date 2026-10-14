using System.Windows;
using System.Windows.Forms;

namespace Popup.Notch;

/// <summary>
/// Размеры панели и все производные прямоугольники.
///
/// Два пространства координат, и путать их нельзя:
/// <list type="bullet">
/// <item>DIP — всё, что рисуется. Совпадает с точками macOS, поэтому размеры
///   взяты у оригинала как есть и панель выглядит так же.</item>
/// <item>Физические пиксели — всё, что сравнивается с положением курсора и
///   передаётся в <c>SetWindowPos</c>. На мониторе с масштабом 150% DIP и
///   пиксель расходятся в полтора раза, и окно, поставленное по DIP, уезжает
///   мимо верхнего края экрана.</item>
/// </list>
/// Свойства с суффиксом <c>Px</c> — физические, остальные — DIP.
/// </summary>
public sealed class NotchGeometry
{
    // MARK: - Константы (DIP)

    /// <summary>Ширина свёрнутой полоски. Ширина выреза MacBook у оригинала.</summary>
    public const double CollapsedWidth = 180;

    /// <summary>
    /// Видимая высота свёрнутой полоски.
    ///
    /// Здесь единственное осознанное расхождение с macOS. Там свёрнутая панель —
    /// это сам вырез: чёрный прямоугольник в чёрной рамке, которого не видно.
    /// В Windows наверху экрана лежит содержимое окон, и полоска в 24 точки
    /// висела бы поверх него весь день. Шесть точек — заметно достаточно, чтобы
    /// знать, куда вести мышь, и мало, чтобы не мешать.
    /// </summary>
    public const double CollapsedHeight = 6;

    /// <summary>
    /// Глубина зоны наведения у верхнего края. Чуть больше видимой полоски:
    /// панель успевает раскрыться до того, как курсор доедет.
    /// </summary>
    public const double CollapsedDepth = 8;

    /// <summary>Высота шапки раскрытой панели.</summary>
    public const double HeaderHeight = 24;

    /// <summary>
    /// Полоска живой активности: красная точка, бегущие точки, таймер.
    /// Ровно столько, чтобы прочитать состояние боковым зрением, и ни точкой
    /// больше — она висит поверх чужой работы всё время, пока идёт запись.
    /// </summary>
    public static readonly Size ActivitySize = new(212, 34);

    public static readonly Size ExpandedSize = new(620, 208);

    /// <summary>Запас вокруг панели, чтобы вогнутые плечи и тень не обрезались.</summary>
    public const double PadHorizontal = 40;
    public const double PadBottom = 44;

    /// <summary>
    /// Полоса у верха экрана, в которой курсор опрашивается на полной частоте.
    /// Достаточно глубокая, чтобы мышь, идущую к панели, заметили заранее.
    /// </summary>
    public const double WarmZoneDepth = 260;

    // MARK: - Состояние

    /// <summary>Границы монитора в физических пикселях.</summary>
    public Rect ScreenPx { get; }

    /// <summary>Масштаб монитора: 1.0 при 100%, 1.5 при 150%.</summary>
    public double Scale { get; }

    /// <summary>Горизонтальный центр панели в физических пикселях.</summary>
    public double CenterXPx => ScreenPx.Left + ScreenPx.Width / 2;

    private NotchGeometry(Rect screenPx, double scale)
    {
        ScreenPx = screenPx;
        Scale = scale;
    }

    public static NotchGeometry Current()
    {
        // Панель принадлежит основному монитору: это тот, наверх которого
        // человек бросает мышь не думая.
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var b = screen.Bounds;
        var scale = Win32.DpiScaleAt(b.X + b.Width / 2, b.Y + 1);
        return new NotchGeometry(new Rect(b.X, b.Y, b.Width, b.Height), scale);
    }

    /// <summary>
    /// True, когда ничего влияющего на панель не сдвинулось.
    ///
    /// Уведомления о смене параметров экрана прилетают по десятку поводов, из
    /// которых панели важны два, и пересборка на каждый выбрасывала бы открытое
    /// состояние и выбранную вкладку.
    /// </summary>
    public bool Matches(NotchGeometry other) =>
        ScreenPx == other.ScreenPx && Math.Abs(Scale - other.Scale) < 0.001;

    // MARK: - Производные размеры

    public double ToPx(double dip) => dip * Scale;

    public Size WindowSize => new(ExpandedSize.Width + 2 * PadHorizontal, ExpandedSize.Height + PadBottom);

    /// <summary>Рамка окна в физических пикселях, вплотную к верху экрана.</summary>
    public Rect WindowRectPx => new(
        CenterXPx - ToPx(WindowSize.Width) / 2,
        ScreenPx.Top,
        ToPx(WindowSize.Width),
        ToPx(WindowSize.Height));

    /// <summary>Видимый размер тела панели для текущего состояния.</summary>
    public Size BodySize(PanelState state) => state switch
    {
        PanelState.Expanded => ExpandedSize,
        PanelState.Activity => ActivitySize,
        _ => new Size(CollapsedWidth, CollapsedHeight),
    };

    /// <summary>
    /// Зона, удерживающая полоску активности кликабельной. Нужна не для
    /// нажатий — свёрнутая полоска их не принимает, — а чтобы курсор,
    /// оказавшийся над ней, считался пришедшим к панели и раскрывал её.
    /// </summary>
    public Rect ActivityHoverRectPx => new(
        CenterXPx - ToPx(ActivitySize.Width) / 2 - ToPx(6),
        ScreenPx.Top,
        ToPx(ActivitySize.Width) + ToPx(12),
        ToPx(ActivitySize.Height));

    /// <summary>Место тела внутри окна, в DIP окна.</summary>
    public Rect ContentRect(Size size) => new(
        (WindowSize.Width - size.Width) / 2,
        0,
        size.Width,
        size.Height);

    /// <summary>То же в физических пикселях экрана — для сравнения с курсором.</summary>
    public Rect ContentScreenRectPx(Size size)
    {
        var r = ContentRect(size);
        var w = WindowRectPx;
        return new Rect(w.Left + ToPx(r.Left), w.Top + ToPx(r.Top), ToPx(r.Width), ToPx(r.Height));
    }

    /// <summary>Зона наведения в свёрнутом виде: полоска у верхнего края.</summary>
    public Rect HoverRectPx => new(
        CenterXPx - ToPx(CollapsedWidth) / 2 - ToPx(6),
        ScreenPx.Top,
        ToPx(CollapsedWidth) + ToPx(12),
        ToPx(CollapsedDepth));

    /// <summary>Зона, удерживающая панель раскрытой.</summary>
    public Rect ExpandedHoverRectPx => new(
        CenterXPx - ToPx(ExpandedSize.Width) / 2 - ToPx(12),
        ScreenPx.Top,
        ToPx(ExpandedSize.Width) + ToPx(24),
        ToPx(ExpandedSize.Height) + ToPx(12));

    /// <summary>Полоса, в которой опрос курсора идёт на полной частоте.</summary>
    public Rect WarmZonePx => new(
        ScreenPx.Left,
        ScreenPx.Top,
        ScreenPx.Width,
        ToPx(WarmZoneDepth));
}
