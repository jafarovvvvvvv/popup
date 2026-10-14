using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Popup.Plugins;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// Вкладка, которую нарисовал плагин.
///
/// Разбирает присланную схему в обычные контролы панели. Ничего своего плагин
/// на экран не выводит: за счёт этого чужая интеграция выглядит частью панели,
/// а не окном внутри окна, — и не может ни закрасить рейл, ни перехватить
/// клик мимо себя.
/// </summary>
public sealed class PluginPane : Grid
{
    private readonly PluginInstance _plugin;
    private readonly ClipboardStore _clipboard;
    private readonly StackPanel _body = new();

    public PluginPane(PluginInstance plugin, ClipboardStore clipboard)
    {
        _plugin = plugin;
        _clipboard = clipboard;

        Children.Add(PaneKit.List(_body));

        _plugin.PropertyChanged += OnChanged;
        _plugin.SetVisible(true);
        Unloaded += (_, _) =>
        {
            _plugin.PropertyChanged -= OnChanged;
            _plugin.SetVisible(false);
        };

        Rebuild();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _body.Children.Clear();

        if (_plugin.Nodes.Count == 0)
        {
            // Статус, а не пустота: молчащий плагин и упавший плагин выглядят
            // одинаково ровно до тех пор, пока не сказать, который из них это.
            var label = PaneKit.Text(_plugin.Status, 12, "TextTertiary");
            label.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 50, 0, 0);
            label.TextTrimming = TextTrimming.None;
            _body.Children.Add(label);
            return;
        }

        foreach (var node in _plugin.Nodes)
        {
            if (Render(node) is { } element) _body.Children.Add(element);
        }
    }

    private UIElement? Render(UiNode node) => node.Type switch
    {
        "text" => RenderText(node),
        "row" => RenderRow(node),
        "list" => RenderList(node),
        "input" => RenderInput(node),
        "button" => RenderButton(node),
        "image" => RenderImage(node),
        "progress" => RenderProgress(node),
        _ => null,
    };

    private static UIElement RenderText(UiNode node)
    {
        var text = PaneKit.Text(node.Text, node.Bold ? 12.5 : 12,
            node.Muted ? "TextTertiary" : "TextPrimary", node.Bold);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        text.Margin = new Thickness(2, 0, 0, 4);
        return text;
    }

    private UIElement RenderRow(UiNode node)
    {
        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        foreach (var child in node.Children)
        {
            if (Render(child) is not { } element) continue;
            if (element is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(element);
        }
        return row;
    }

    private UIElement RenderList(UiNode node)
    {
        var list = new StackPanel();
        foreach (var item in node.Items)
        {
            var line = new StackPanel();
            line.Children.Add(PaneKit.Text(item.Title, 12, "TextPrimary"));
            if (item.Text.Length > 0 && item.Label.Length > 0)
                line.Children.Add(PaneKit.Text(item.Text, 10.5, "TextTertiary"));

            var captured = item;
            list.Children.Add(PaneKit.Row(line, () => Perform(captured.Action, captured.Id, captured.Value)));
        }
        return list;
    }

    private UIElement RenderInput(UiNode node)
    {
        var field = PaneKit.Field();
        field.Text = node.Value;
        var id = node.Id;
        var action = node.Action.Length > 0 ? node.Action : node.Id;
        field.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            Perform(action, id, field.Text);
            e.Handled = true;
        };

        var box = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = PaneKit.Brush("Surface"),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 6),
            Child = node.Placeholder.Length > 0 ? WithHint(field, node.Placeholder) : field,
        };
        return box;
    }

    /// <summary>
    /// Подсказка от плагина приходит готовой строкой, а не ключом перевода:
    /// свои строки плагин переводит сам, панель в это не вмешивается.
    /// </summary>
    private static UIElement WithHint(TextBox field, string hint)
    {
        var label = PaneKit.Text(hint, field.FontSize, "TextTertiary");
        label.IsHitTestVisible = false;
        void Sync() => label.Visibility = field.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        field.TextChanged += (_, _) => Sync();
        Sync();

        var grid = new Grid();
        grid.Children.Add(label);
        grid.Children.Add(field);
        return grid;
    }

    private UIElement RenderButton(UiNode node)
    {
        var action = node.Action.Length > 0 ? node.Action : node.Id;
        var button = PaneKit.TextButton(node.Label.Length > 0 ? node.Label : node.Text,
            () => Perform(action, node.Id, node.Value));
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 0, 0, 6);
        return button;
    }

    private UIElement RenderImage(UiNode node)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(node.Path);
            image.EndInit();
            image.Freeze();
            return new Image
            {
                Source = image,
                Stretch = Stretch.Uniform,
                MaxHeight = 96,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
            };
        }
        catch (Exception e)
        {
            return PaneKit.Text($"нет картинки: {e.Message}", 11, "TextTertiary");
        }
    }

    private static UIElement RenderProgress(UiNode node) => new Grid
    {
        Height = 4,
        Margin = new Thickness(0, 2, 0, 8),
        Children =
        {
            new Border { CornerRadius = new CornerRadius(2), Background = PaneKit.Brush("Surface") },
            new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = PaneKit.Brush("TextSecondary"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Width = Math.Clamp(node.Progress, 0, 1) * 460,
            },
        },
    };

    /// <summary>
    /// Что делать по нажатию.
    ///
    /// Два действия панель выполняет сама — скопировать и открыть, — потому что
    /// иначе каждый плагин переписывал бы одно и то же, а ради «положить строку
    /// в буфер» пришлось бы будить процесс. Всё остальное уезжает плагину.
    /// </summary>
    private void Perform(string action, string id, string value)
    {
        switch (action)
        {
            case "copy":
                if (value.Length > 0) _clipboard.CopyText(value);
                break;
            case "open":
                if (value.Length == 0) break;
                // Плагин может прислать и ссылку, и путь к папке — способ
                // открыть у них один и тот же.
                if (Directory.Exists(value)) Shell.OpenFolder(value);
                else Shell.OpenUrl(value);
                break;
            case "":
                break;
            default:
                _plugin.Act(action.Length > 0 ? action : id, value);
                break;
        }
    }
}
