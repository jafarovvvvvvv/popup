using System.Globalization;

namespace Popup.Services;

/// <summary>
/// Строки интерфейса. Обычный словарь в коде, а не .resx: файл на две колонки
/// правится за секунду и виден целиком — ровно то, что нужно проекту, который
/// его владелец собирается дописывать сам.
///
/// Ключ — английский текст, как в <c>Localizable.strings</c> оригинала.
/// </summary>
public static class Strings
{
    private static readonly Dictionary<string, string> Ru = new()
    {
        ["Music"] = "Музыка",
        ["Shelf"] = "Полка",
        ["Clipboard"] = "Буфер",
        ["Snippets"] = "Сниппеты",
        ["Calendar"] = "Календарь",
        ["Translate"] = "Перевод",
        ["Notes"] = "Заметки",
        ["Dev"] = "Разработка",

        ["Nothing playing"] = "Ничего не играет",
        ["Drop files here"] = "Перетащи файлы сюда",
        ["Shelf is empty"] = "На полке пусто",
        ["Clipboard is empty"] = "Буфер пуст",
        ["No snippets yet"] = "Сниппетов пока нет",
        ["No notes"] = "Заметок нет",
        ["No meetings this week"] = "На неделе встреч нет",
        ["Nothing to translate"] = "Нечего переводить",
        ["No plugins installed"] = "Плагинов не установлено",

        ["now"] = "идёт",
        ["min"] = "мин",
        ["h"] = "ч",
        ["d"] = "дн",
        ["All day"] = "Весь день",
        ["Today"] = "Сегодня",
        ["Nothing on this day"] = "В этот день ничего",
        ["Meetings appear once a calendar is connected"] = "Встречи появятся, когда подключишь календарь",
        ["Tomorrow"] = "Завтра",
        ["Connected"] = "Подключено",
        ["Disconnect"] = "Отключить",
        ["Connecting…"] = "Подключаюсь…",
        ["Set googleClientId in settings.json"] =
            "Вставь ссылку на календарь в calendarIcsUrl\nв settings.json — это самый короткий путь",
        ["googleClientId looks wrong"] =
            "googleClientId скопирован с ошибкой.\nОн должен оканчиваться на .apps.googleusercontent.com ровно один раз",

        // Знакомство при первом запуске. Ключи здесь, как и везде, — английский
        // текст: без перевода панель покажет их же, и это будет читаемо.
        ["Skip"] = "Пропустить",
        ["Next"] = "Дальше",
        ["Got it"] = "Понятно",

        ["The panel lives at the top edge"] = "Панель живёт у верхнего края",
        ["You cannot see it until you bring the pointer to it. Move the mouse to the middle of the top edge and pause — the panel unfolds. Move away and it folds back. If your hands are on the keyboard, there is Ctrl+Alt+Space."] =
            "Её не видно, пока к ней не подвести курсор. Наведи мышь на середину верха экрана и задержи — панель раскроется. Уведёшь — свернётся. Если руки на клавиатуре, есть Ctrl+Alt+Space.",

        ["Eight tabs along the sides"] = "Восемь вкладок по бокам",
        ["Music, file shelf, clipboard history, snippets, calendar, translation, notes and the dev tab. Tabs switch on hover — no clicking needed. A file can simply be dropped on the top of the screen."] =
            "Музыка, полка файлов, история буфера, сниппеты, календарь, перевод, заметки и вкладка разработки. Вкладки переключаются наведением — нажимать не нужно. Файл можно просто бросить на верх экрана.",

        ["You can see what is going on"] = "Видно, что происходит",
        ["While a recording, a build or a download is running, a narrow strip with a timer sits at the top. The panel does not unfold and does not cover your work. The LocalWhisper pill moves up here on its own."] =
            "Пока идёт запись голоса, сборка или загрузка, сверху висит узкая полоска с таймером. Панель при этом не раскрывается и не закрывает работу. Пилюля LocalWhisper переезжает сюда сама.",

        ["It is built out to fit you"] = "Достраивается под себя",
        ["The plugins folder gives you a tab of your own from a program in any language. The pipe lets any script show a strip, drop a file on the shelf or put text in the clipboard. All of it is in docs."] =
            "Папка плагинов даёт свою вкладку из программы на чём угодно.\nА через канал любой скрипт может показать полоску, положить файл\nна полку или текст в буфер. Всё это описано в docs.",

        ["Search"] = "Поиск",
        ["Add from clipboard"] = "Добавить из буфера",
        ["Copied"] = "Скопировано",
        ["Copy"] = "Копировать",
        ["Remove"] = "Убрать",
        ["Clear"] = "Очистить",
        ["Reveal"] = "Показать",
        ["Hide"] = "Скрыть",
        ["Join"] = "Войти",
        ["New note"] = "Новая заметка",
        ["Connect Google Calendar"] = "Подключить Google Календарь",
        ["Add a translation key with the key button"] = "Добавь ключ перевода кнопкой с ключом",
        ["Translation API key — Enter to save"] = "Ключ API перевода — Enter, чтобы сохранить",
        ["Translation key"] = "Ключ перевода",
        ["Reload plugins"] = "Перезагрузить плагины",
        ["Open plugins folder"] = "Открыть папку плагинов",
        ["Open screenshots folder"] = "Открыть папку скриншотов",
        ["Save clipboard images"] = "Сохранять картинки из буфера",
        ["Run at startup"] = "Запускать при входе",
        ["Privacy"] = "Приватность",
        ["All"] = "Всё",
        ["Show panel"] = "Показать панель",
        ["Quit"] = "Выход",
        ["Settings file"] = "Файл настроек",
    };

    private static Dictionary<string, string>? _active;

    /// <summary>
    /// Какой язык показывать. <c>null</c> в настройках — как в системе;
    /// всё, что не русский, попадает в английский, потому что ключи и есть
    /// английский текст.
    ///
    /// Смотрим на обе культуры, а не только на язык интерфейса. Windows часто
    /// стоит с английским интерфейсом при русском регионе — и панель, которая
    /// в этом случае говорит по-английски, формально права и практически
    /// бесполезна.
    /// </summary>
    public static void Configure(string? language)
    {
        var russian = language is not null
            ? language.Equals("ru", StringComparison.OrdinalIgnoreCase)
            : IsRussian(CultureInfo.CurrentUICulture) || IsRussian(CultureInfo.CurrentCulture);
        _active = russian ? Ru : null;
    }

    private static bool IsRussian(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Культура, по которой панель называет месяцы и дни недели.
    ///
    /// Не системная: язык интерфейса задаётся настройкой, и календарь на
    /// русской панели, подписывающий дни по-английски, выглядит поломкой. Заодно
    /// отсюда берётся и первый день недели — в России он понедельник.
    /// </summary>
    public static CultureInfo Culture =>
        _active is not null ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.CurrentCulture;

    public static string L(string key) =>
        _active is not null && _active.TryGetValue(key, out var value) ? value : key;
}
