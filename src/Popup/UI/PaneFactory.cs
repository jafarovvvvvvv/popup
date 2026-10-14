using System.Windows.Controls;
using Popup.Model;
using Popup.Services;
using Popup.UI.Panes;

namespace Popup.UI;

/// <summary>
/// Что показать внутри вкладки и что — в правом углу шапки.
///
/// Одна точка ветвления на всю панель: окно не знает ни одной вкладки по имени,
/// а вкладка не знает, как её рисуют. Плагин попадает сюда тем же путём, что и
/// встроенные, — по идентификатору.
/// </summary>
public static class PaneFactory
{
    public static UIElement? Create(NotchViewModel vm, PanelTab tab)
    {
        // Вкладку плагина рисует не свой код панели, а разбор схемы, которую
        // прислал чужой процесс.
        if (tab.PluginId is { } pluginId)
        {
            return vm.Plugins.Find(pluginId) is { } plugin
                ? new PluginPane(plugin, vm.Clipboard)
                : PaneKit.Empty("No plugins installed");
        }
        return CreateBuiltIn(vm, tab);
    }

    private static UIElement? CreateBuiltIn(NotchViewModel vm, PanelTab tab) => tab.Id switch
    {
        "media" => new MediaPane(vm.Media),
        "shelf" => new ShelfPane(vm.Shelf, vm.IsDropTargeted),
        "clipboard" => new ClipboardPane(vm.Clipboard, vm.Privacy),
        "snippets" => new SnippetsPane(vm.Snippets, vm.Clipboard, vm.Privacy),
        "calendar" => new CalendarPane(vm.Calendar, vm.Privacy),
        "translate" => new TranslatePane(vm.Translator, vm.Clipboard),
        "notes" => new NotesPane(vm.Notes, vm.Privacy),
        "dev" => new DevPane(vm.Plugins),
        _ => Placeholder(tab),
    };

    /// <summary>
    /// Правый угол шапки. У каждой вкладки он свой: счётчик, имя источника,
    /// обратный отсчёт до встречи — то, что стоит видеть, не заходя внутрь.
    /// </summary>
    public static UIElement? CreateHeaderTrailing(NotchViewModel vm, PanelTab tab) => tab.Id switch
    {
        "media" => MediaTrailing(vm),
        "shelf" => Counter(vm.Shelf.Items.Count),
        "clipboard" => Counter(vm.Clipboard.Items.Count),
        "snippets" => Counter(vm.Snippets.Items.Count),
        "notes" => Counter(vm.Notes.Notes.Count),
        "calendar" => CalendarTrailing(vm),
        // Переводчик — ничего: колонки и так называют оба языка, а полоска
        // шапки — единственное место в панели, которое не стоит тратить на
        // повтор.
        _ => null,
    };

    private static UIElement? CalendarTrailing(NotchViewModel vm)
    {
        if (vm.Calendar.Next is not { } next) return null;
        return new TextBlock
        {
            Text = CalendarStore.Countdown(next),
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource(
                next.IsRunning ? "TextSecondary" : "TextTertiary"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
    }

    /// <summary>Счётчик в шапке. Ноль не показываем вовсе — это не новость.</summary>
    private static UIElement? Counter(int value) => value > 0
        ? new TextBlock
        {
            Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextTertiary"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        }
        : null;

    private static UIElement MediaTrailing(NotchViewModel vm)
    {
        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };

        if (vm.Media.Track is not null)
        {
            row.Children.Add(new EqualizerBars { IsAnimating = vm.Media.IsPlaying });
        }

        row.Children.Add(new TextBlock
        {
            Text = vm.Media.SourceName ?? "",
            FontSize = 10,
            FontWeight = FontWeights.Medium,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextTertiary"),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        });

        return row;
    }

    /// <summary>
    /// Заглушка для вкладки, содержимое которой ещё не написано. Держит
    /// раскладку честной: пустая вкладка выглядит пустой, а не сломанной.
    /// </summary>
    private static UIElement Placeholder(PanelTab tab) => new TextBlock
    {
        Text = tab.Title,
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextTertiary"),
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Подпись «здесь пока ничего нет» — одинаковая во всех вкладках.</summary>
    public static TextBlock EmptyLabel(string key) => new()
    {
        Text = Strings.L(key),
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextTertiary"),
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
