using System.Windows.Media;

namespace Popup.Notch;

/// <summary>
/// Силуэт панели: вогнутые плечи, растворяющиеся в верхнем крае экрана, прямые
/// бока, скруглённый низ. Порт <c>NotchShape.swift</c>.
///
/// Тело уже фигуры на <see cref="TopRadius"/> с каждой стороны — в этом запасе
/// и живут плечи, — поэтому элемент должен быть настолько же шире панели.
///
/// Нарисован через <see cref="OnRender"/>, а не наследованием от <c>Shape</c>:
/// <c>Shape</c> считает <c>DefiningGeometry</c> один раз и держит результат в
/// кэше, который сбрасывается только сменой размера. Радиусы здесь меняются
/// анимацией при том же размере, и с кэшем панель так и оставалась бы
/// прямоугольником с той формой, которая случайно попала в первый кадр.
/// </summary>
public sealed class NotchShape : FrameworkElement
{
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(NotchShape),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TopRadiusProperty =
        DependencyProperty.Register(
            nameof(TopRadius), typeof(double), typeof(NotchShape),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BottomRadiusProperty =
        DependencyProperty.Register(
            nameof(BottomRadius), typeof(double), typeof(NotchShape),
            new FrameworkPropertyMetadata(22.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double TopRadius
    {
        get => (double)GetValue(TopRadiusProperty);
        set => SetValue(TopRadiusProperty, value);
    }

    public double BottomRadius
    {
        get => (double)GetValue(BottomRadiusProperty);
        set => SetValue(BottomRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var geometry = Build(RenderSize, TopRadius, BottomRadius);
        if (geometry != Geometry.Empty) drawingContext.DrawGeometry(Fill, null, geometry);
    }

    public static Geometry Build(Size size, double topRadius, double bottomRadius)
    {
        if (size.Width <= 0 || size.Height <= 0) return Geometry.Empty;

        var tr = Math.Max(0, Math.Min(topRadius, size.Width / 2));
        var br = Math.Max(0, Math.Min(bottomRadius, Math.Min(size.Width / 2 - tr, size.Height)));
        double minX = 0, minY = 0, maxX = size.Width, maxY = size.Height;
        var left = minX + tr;
        var right = maxX - tr;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(minX, minY), isFilled: true, isClosed: true);
            // Вогнутое левое плечо: кривая уходит от края экрана внутрь и вниз.
            ctx.QuadraticBezierTo(new Point(left, minY), new Point(left, minY + tr), true, false);
            ctx.LineTo(new Point(left, maxY - br), true, false);
            ctx.QuadraticBezierTo(new Point(left, maxY), new Point(left + br, maxY), true, false);
            ctx.LineTo(new Point(right - br, maxY), true, false);
            ctx.QuadraticBezierTo(new Point(right, maxY), new Point(right, maxY - br), true, false);
            ctx.LineTo(new Point(right, minY + tr), true, false);
            ctx.QuadraticBezierTo(new Point(right, minY), new Point(maxX, minY), true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
