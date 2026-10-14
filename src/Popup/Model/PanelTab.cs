using Popup.Services;

namespace Popup.Model;

/// <summary>
/// Одна вкладка панели.
///
/// В оригинале это перечисление: вкладок ровно семь и больше взяться неоткуда.
/// Здесь вкладку может добавить плагин, поэтому она стала объектом — иконка в
/// рейле и панель под неё заводятся во время работы, а не на этапе сборки.
/// </summary>
public sealed class PanelTab(string id, string titleKey, string iconKey, bool needsKeyboard = false)
{
    /// <summary>Устойчивый идентификатор: <c>media</c>, <c>dev</c>, <c>plugin:localwhisper</c>.</summary>
    public string Id { get; } = id;

    /// <summary>Ключ иконки в <c>Icons.xaml</c>.</summary>
    public string IconKey { get; } = iconKey;

    private readonly string _titleKey = titleKey;

    /// <summary>
    /// Заголовок читается каждый раз, а не запоминается: язык может смениться
    /// в настройках, а вкладки — те же самые объекты от запуска до выхода.
    /// </summary>
    public string Title => Strings.L(_titleKey);

    /// <summary>
    /// Вкладки с полем ввода. Попав на такую, панель сразу забирает клавиатуру,
    /// чтобы «прийти и напечатать» было одним движением.
    /// </summary>
    public bool NeedsKeyboard { get; } = needsKeyboard;

    /// <summary>
    /// Заполнено только у вкладок, которые принёс плагин. По нему панель
    /// понимает, что содержимое рисуется не своим кодом, а разбором схемы,
    /// присланной чужим процессом. Ссылка на сам плагин здесь не хранится —
    /// вкладка знает только имя, а живой процесс ищется в реестре плагинов.
    /// </summary>
    public string? PluginId { get; init; }

    public override string ToString() => Id;

    // MARK: - Встроенные

    public static readonly PanelTab Media = new("media", "Music", "IconMusic");
    public static readonly PanelTab Shelf = new("shelf", "Shelf", "IconShelf");
    public static readonly PanelTab Clipboard = new("clipboard", "Clipboard", "IconClipboard");
    public static readonly PanelTab Snippets = new("snippets", "Snippets", "IconPin", needsKeyboard: true);
    public static readonly PanelTab Calendar = new("calendar", "Calendar", "IconCalendar");
    public static readonly PanelTab Translate = new("translate", "Translate", "IconTranslate", needsKeyboard: true);
    public static readonly PanelTab Notes = new("notes", "Notes", "IconNote", needsKeyboard: true);
    public static readonly PanelTab Dev = new("dev", "Dev", "IconDev");

    /// <summary>
    /// Левый рейл несёт исходную шестёрку и заполнен: седьмая иконка переросла
    /// бы высоту тела панели. Рост продолжается во второй колонке справа —
    /// там же, где живут заметки, вкладка разработки и всё, что принесут
    /// плагины.
    /// </summary>
    public static readonly PanelTab[] LeftRail = [Media, Shelf, Clipboard, Snippets, Calendar, Translate];
}
