using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// Полка. Порт <c>ShelfPane.swift</c>.
///
/// Карточки лежат в ряд и тащатся наружу: полка существует ради того, чтобы
/// файл можно было донести из одного окна в другое, не проходя через папку
/// «Загрузки».
/// </summary>
public sealed class ShelfPane : Grid
{
    private const double CardWidth = 96;

    private readonly ShelfStore _store;
    private readonly bool _isTargeted;
    private readonly StackPanel _cards = new()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
    };

    public ShelfPane(ShelfStore store, bool isTargeted)
    {
        _store = store;
        _isTargeted = isTargeted;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 4),
        };
        header.Children.Add(PaneKit.TextButton(Strings.L("Clear"), () => _store.Clear()));
        SetRow(header, 0);
        Children.Add(header);

        var scroller = new ScrollViewer
        {
            Content = _cards,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
        };
        SetRow(scroller, 1);
        Children.Add(scroller);

        _store.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _store.PropertyChanged -= OnChanged;

        Rebuild();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _cards.Children.Clear();

        if (_store.Items.Count == 0)
        {
            // Пока на панель тащат файл, подпись меняется на приглашение: в
            // этот момент вопрос у человека ровно один — сюда ли бросать.
            _cards.Children.Add(PaneKit.Empty(_isTargeted ? "Drop files here" : "Shelf is empty"));
            return;
        }

        foreach (var item in _store.Items) _cards.Children.Add(Card(item));
    }

    private UIElement Card(ShelfStore.Item item)
    {
        var thumb = new Grid { Height = 74, Margin = new Thickness(0, 0, 0, 6) };
        thumb.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = PaneKit.Brush("Surface"),
            BorderThickness = new Thickness(1),
            BorderBrush = PaneKit.Brush("Hairline"),
        });

        if (item.IsImage && LoadThumbnail(item.Path) is { } image)
        {
            thumb.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill },
                Margin = new Thickness(1),
            });
        }
        else
        {
            // Не картинка — показываем расширение: по нему файл узнаётся
            // быстрее, чем по обрезанному имени.
            var label = PaneKit.Text(item.Extension.Length > 0 ? item.Extension : "FILE", 11, "TextSecondary", bold: true);
            label.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            label.TextTrimming = TextTrimming.None;
            thumb.Children.Add(label);
        }

        var remove = PaneKit.IconButton("IconClose", () => _store.Remove(item), 18);
        remove.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        remove.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        remove.Margin = new Thickness(0, 2, 2, 0);
        remove.Opacity = 0;
        thumb.Children.Add(remove);

        var name = PaneKit.Text(item.Name, 10, "TextSecondary");
        name.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        name.MaxWidth = CardWidth;

        var stack = new StackPanel { Width = CardWidth, Margin = new Thickness(0, 0, 10, 0) };
        stack.Children.Add(thumb);
        stack.Children.Add(name);

        var card = new Border
        {
            Child = stack,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = item.Path,
        };
        // Крестик появляется только под курсором: восемь крестиков на полке
        // сразу — это восемь приглашений удалить не то.
        card.MouseEnter += (_, _) => remove.Opacity = 1;
        card.MouseLeave += (_, _) => remove.Opacity = 0;

        // Клик и перетаскивание начинаются одинаково — нажатием, — и различить
        // их можно только по тому, сдвинулась ли мышь. Без этого порога любое
        // нажатие на карточку немедленно превращалось в перетаскивание, и
        // открыть файл в проводнике было нельзя вовсе.
        Point? pressedAt = null;
        card.MouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(card);
        card.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || pressedAt is not { } start) return;
            var now = e.GetPosition(card);
            if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            pressedAt = null;
            // Перетаскивание наружу — то, ради чего полка и существует.
            // Проводник и любое другое приложение принимают ровно этот формат.
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move);
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            // Отпустили, не сдвинувшись, — значит это был клик.
            if (pressedAt is null) return;
            pressedAt = null;
            Reveal(item.Path);
        };

        return card;
    }

    private static BitmapImage? LoadThumbnail(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // Уменьшение при декодировании: полка показывает картинку в 96
            // точек, и держать в памяти полный кадр с телефона ради неё незачем.
            image.DecodePixelWidth = 192;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e)
        {
            Log.Warn("shelf", $"не прочитать превью {path}: {e.Message}");
            return null;
        }
    }

    private static void Reveal(string path) => Shell.RevealFile(path);
}
