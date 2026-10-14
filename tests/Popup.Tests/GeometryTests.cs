using Popup.Notch;

namespace Popup.Tests;

/// <summary>
/// Геометрия — единственное место, где путаница между точками и пикселями
/// стоит целой панели: на мониторе с масштабом она уезжает мимо экрана, и
/// заметить это можно только глазами.
/// </summary>
public class GeometryTests
{
    [Fact]
    public void CollapsedIsTheThinStrip()
    {
        var geometry = NotchGeometry.Current();
        var size = geometry.BodySize(PanelState.Collapsed);
        Assert.Equal(NotchGeometry.CollapsedWidth, size.Width);
        Assert.Equal(NotchGeometry.CollapsedHeight, size.Height);
    }

    [Fact]
    public void ActivitySitsBetweenTheTwoOtherStates()
    {
        var geometry = NotchGeometry.Current();
        var collapsed = geometry.BodySize(PanelState.Collapsed);
        var activity = geometry.BodySize(PanelState.Activity);
        var expanded = geometry.BodySize(PanelState.Expanded);

        Assert.True(collapsed.Width < activity.Width);
        Assert.True(activity.Width < expanded.Width);
        Assert.True(collapsed.Height < activity.Height);
        Assert.True(activity.Height < expanded.Height);
    }

    /// <summary>
    /// Окно шире тела ровно на запас под плечи и тень — иначе вогнутые плечи
    /// окажутся срезаны краем окна.
    /// </summary>
    [Fact]
    public void WindowLeavesRoomForShouldersAndShadow()
    {
        var geometry = NotchGeometry.Current();
        Assert.Equal(
            NotchGeometry.ExpandedSize.Width + 2 * NotchGeometry.PadHorizontal,
            geometry.WindowSize.Width);
        Assert.Equal(
            NotchGeometry.ExpandedSize.Height + NotchGeometry.PadBottom,
            geometry.WindowSize.Height);
    }

    /// <summary>Окно стоит по центру верхнего края — и в пикселях, а не в точках.</summary>
    [Fact]
    public void WindowIsCentredOnTheTopEdge()
    {
        var geometry = NotchGeometry.Current();
        var window = geometry.WindowRectPx;

        Assert.Equal(geometry.ScreenPx.Top, window.Top);
        Assert.Equal(geometry.CenterXPx, window.Left + window.Width / 2, precision: 6);
        Assert.Equal(geometry.ToPx(geometry.WindowSize.Width), window.Width, precision: 6);
    }

    /// <summary>
    /// Зона наведения полоски активности заметно больше свёрнутой: иначе панель
    /// пряталась бы за тем, что её же и показывает.
    /// </summary>
    [Fact]
    public void ActivityIsEasierToHitThanTheBareStrip()
    {
        var geometry = NotchGeometry.Current();
        Assert.True(geometry.ActivityHoverRectPx.Width > geometry.HoverRectPx.Width);
        Assert.True(geometry.ActivityHoverRectPx.Height > geometry.HoverRectPx.Height);
    }

    /// <summary>
    /// Курсор, брошенный в самый верх экрана, обязан считаться попавшим:
    /// именно так до панели и добираются.
    /// </summary>
    [Fact]
    public void TheVeryTopRowCountsAsInside()
    {
        var geometry = NotchGeometry.Current();
        var top = new Point(geometry.CenterXPx, geometry.ScreenPx.Top);
        Assert.True(geometry.HoverRectPx.Contains(top));
        Assert.True(geometry.ExpandedHoverRectPx.Contains(top));
        Assert.True(geometry.WarmZonePx.Contains(top));
    }
}
