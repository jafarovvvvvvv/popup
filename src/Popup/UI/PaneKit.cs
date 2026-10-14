using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Popup.Services;
using Path = System.Windows.Shapes.Path;

namespace Popup.UI;

/// <summary>
/// Мелочь, из которой собраны все вкладки: карточки, строки, иконки, укрытие.
///
/// Вынесена сюда не ради экономии строк, а ради того, чтобы шесть вкладок
/// выглядели одной панелью. Как только у каждой заводится свой отступ и свой
/// оттенок серого, панель перестаёт читаться как единое целое, а поправить это
/// потом — значит править шесть мест.
/// </summary>
public static class PaneKit
{
    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Контурная иконка из <c>Icons.xaml</c>.</summary>
    public static Path Icon(string key, string brushKey = "TextSecondary", double scale = 1)
    {
        var path = new Path
        {
            Data = (Geometry)(Application.Current.TryFindResource(key) ?? Geometry.Empty),
            Stroke = Brush(brushKey),
            StrokeThickness = 1.3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.None,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        if (Math.Abs(scale - 1) > 0.001)
        {
            path.RenderTransformOrigin = new Point(0.5, 0.5);
            path.RenderTransform = new ScaleTransform(scale, scale);
        }
        return path;
    }

    public static TextBlock Text(
        string text, double size = 12, string brushKey = "TextPrimary", bool bold = false) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(brushKey),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };

    /// <summary>Вертикальный список строк с тонкой полосой прокрутки.</summary>
    public static ScrollViewer List(Panel content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Background = Brushes.Transparent,
        Padding = new Thickness(0, 0, 4, 0),
    };

    /// <summary>Строка списка: подсвечивается наведением, отвечает на клик.</summary>
    public static Border Row(UIElement content, Action? onClick = null)
    {
        var row = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Padding = new Thickness(8, 5, 6, 5),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = onClick is null ? Cursors.Arrow : Cursors.Hand,
        };
        row.MouseEnter += (_, _) => row.Background = Brush("Surface");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        if (onClick is not null) row.MouseLeftButtonUp += (_, _) => onClick();
        return row;
    }

    /// <summary>Маленькая круглая кнопка с иконкой — удалить, скопировать, открыть.</summary>
    public static Button IconButton(string iconKey, Action onClick, double size = 22)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("NotchButton"),
            Width = size,
            Height = size,
            Content = Icon(iconKey, "TextTertiary", 0.85),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Кнопка с подписью.</summary>
    public static Button TextButton(string label, Action onClick)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("NotchTextButton"),
            Content = Text(label, 11, "TextSecondary"),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>
    /// Укрытие содержимого строки.
    ///
    /// Размытие, а не звёздочки: звёздочки прячут длину и форму вместе с
    /// текстом, и строка перестаёт быть узнаваемой даже для того, кто её сам
    /// туда положил. Размытая строка сохраняет силуэт — видно, что это адрес, а
    /// не номер карты, — но прочитать её через плечо или на записи нельзя.
    /// </summary>
    public static void ApplyCover(UIElement element, bool covered) =>
        element.Effect = covered ? new BlurEffect { Radius = 7, KernelType = KernelType.Gaussian } : null;

    /// <summary>Подпись «здесь пока ничего нет», одинаковая во всех вкладках.</summary>
    public static UIElement Empty(string key)
    {
        var label = Text(Strings.L(key), 12, "TextTertiary");
        label.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        label.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.None;
        return label;
    }

    /// <summary>Поле ввода в стиле панели.</summary>
    public static TextBox Field(string? placeholderKey = null, bool multiline = false)
    {
        var box = new TextBox
        {
            Style = (Style)Application.Current.FindResource("NotchTextBox"),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalContentAlignment = multiline
                ? System.Windows.VerticalAlignment.Top
                : System.Windows.VerticalAlignment.Center,
        };
        if (placeholderKey is not null) box.Tag = Strings.L(placeholderKey);
        return box;
    }

    /// <summary>
    /// Подсказка в пустом поле. Рисуется отдельным слоем под полем, а не
    /// текстом внутри него: текст-заглушка внутри поля приходится стирать
    /// вручную, и первым, что человек делает в панели, оказывается уборка.
    /// </summary>
    public static Grid WithPlaceholder(TextBox box, string placeholderKey)
    {
        var hint = Text(Strings.L(placeholderKey), box.FontSize, "TextTertiary");
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(2, 0, 0, 0);
        hint.VerticalAlignment = box.VerticalContentAlignment;

        void Sync() => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();

        var grid = new Grid();
        grid.Children.Add(hint);
        grid.Children.Add(box);
        return grid;
    }
}
