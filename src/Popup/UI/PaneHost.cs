using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Popup.UI;

/// <summary>
/// Место, где живёт содержимое вкладки.
///
/// Панели сменяются на месте — без переезда. Рейл вертикальный, а вкладки между
/// собой не связаны, так что направление было бы только украшением. Уходящая
/// исчезает быстрее, чем приходит новая: так они никогда не висят обе
/// полупрозрачными.
/// </summary>
public sealed class PaneHost : Grid
{
    private UIElement? _current;

    public PaneHost()
    {
        ClipToBounds = true;
    }

    public void Show(UIElement? pane)
    {
        if (ReferenceEquals(_current, pane)) return;

        if (_current is { } outgoing) Remove(outgoing);
        _current = pane;
        if (pane is null) return;

        pane.Opacity = 0;
        var scale = new ScaleTransform(0.97, 0.97);
        pane.RenderTransform = scale;
        pane.RenderTransformOrigin = new Point(0.5, 0.5);
        Children.Add(pane);

        pane.BeginAnimation(OpacityProperty, Theme.Fade(1, Theme.PaneInDuration, Theme.PaneInDelay));
        var grow = new DoubleAnimation
        {
            To = 1,
            Duration = Theme.PaneInDuration,
            BeginTime = Theme.PaneInDelay,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void Remove(UIElement pane)
    {
        var scale = pane.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        pane.RenderTransform = scale;
        pane.RenderTransformOrigin = new Point(0.5, 0.5);

        var shrink = new DoubleAnimation { To = 1.02, Duration = Theme.PaneOutDuration };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        var fade = Theme.Fade(0, Theme.PaneOutDuration);
        // Удаляем не сразу, а когда исчезновение доиграло: снятое с дерева
        // раньше времени содержимое просто мигает, вместо того чтобы уйти.
        fade.Completed += (_, _) => Children.Remove(pane);
        pane.BeginAnimation(OpacityProperty, fade);
    }
}
