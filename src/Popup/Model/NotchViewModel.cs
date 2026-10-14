using System.Collections.ObjectModel;
using Popup.Notch;
using Popup.Plugins;
using Popup.Services;

namespace Popup.Model;

/// <summary>
/// Состояние панели: что раскрыто, какая вкладка выбрана, у кого клавиатура.
/// Порт <c>NotchViewModel.swift</c>.
/// </summary>
public sealed class NotchViewModel : Observable
{
    public NotchGeometry Geometry { get; }
    public Settings Settings { get; }

    /// <summary>Общая для всех вкладок, показывающих то, что лучше не показывать.</summary>
    public PrivacyMode Privacy { get; }

    public MediaController Media { get; }
    public ShelfStore Shelf { get; }
    public ClipboardStore Clipboard { get; }
    public SnippetStore Snippets { get; }
    public NoteStore Notes { get; }
    public CalendarStore Calendar { get; }
    public Translator Translator { get; }
    public PluginHost Plugins { get; }
    public ActivityStore Activity { get; }

    public NotchViewModel(NotchGeometry geometry, Settings settings)
    {
        Geometry = geometry;
        Settings = settings;
        Privacy = new PrivacyMode(settings);
        Media = new MediaController();
        Shelf = new ShelfStore();
        Clipboard = new ClipboardStore();
        Snippets = new SnippetStore();
        Notes = new NoteStore();
        Calendar = new CalendarStore(settings);
        Translator = new Translator(settings);
        Plugins = new PluginHost();
        Plugins.PluginsChanged += RebuildRightRail;
        Activity = new ActivityStore();
        Activity.PropertyChanged += (_, _) =>
        {
            Raise(nameof(State));
            StateChanged?.Invoke(State);
        };

        LeftRail = new ObservableCollection<PanelTab>(PanelTab.LeftRail);
        RightRail = [PanelTab.Notes, PanelTab.Dev];
        _tab = PanelTab.Media;

        TabChanged += OnTabChanged;

        // Шапка панели читает сквозь хранилища — счётчики, имя источника,
        // столбики эквалайзера. Вложенные модели сами о себе наверх не
        // сообщают, поэтому пересылка нужна вручную.
        //
        // Пересылается только пока панель раскрыта. В свёрнутом виде менять
        // этим нечего — панель просто чёрная полоска, — а хранилища живут по
        // своему расписанию: смена трека каждые несколько минут, копирование
        // когда его сделали. Раскрытие перерисовывает всё заново само, потому
        // что IsOpen тоже наблюдаемое.
        //
        // Хранилища с полем ввода — сниппеты и заметки — здесь нарочно
        // отсутствуют. Они меняются на каждое нажатие клавиши, а перерисовка
        // всей панели на букву стоит дороже устаревшего счётчика: она
        // пересобирает поле и роняет фокус, так что первая набранная буква
        // оказывается и последней дошедшей.
        foreach (var store in new Observable[] { Media, Shelf, Clipboard, Calendar })
        {
            store.PropertyChanged += (_, _) =>
            {
                if (IsExpanded) StoresChanged?.Invoke();
            };
        }
    }

    /// <summary>Что-то в хранилищах изменилось и шапку надо перечитать.</summary>
    public event Action? StoresChanged;

    private void OnTabChanged(PanelTab previous, PanelTab current)
    {
        // Открытие вкладки лишь перепроверяет статус. Диалог разрешения — это
        // нажатие человека на кнопку внутри панели: это единственное
        // разрешение, которое Popup вообще просит, и оно заслуживает
        // объяснения до системного окна, а не после.
        if (current == PanelTab.Calendar) Calendar.RefreshAccess();
        // Файл сниппетов правят снаружи, поэтому он читается на входе, а не
        // держится с запуска.
        if (current == PanelTab.Snippets) Snippets.Reload();
        // Уход с заметок подметает пустые.
        if (previous == PanelTab.Notes && current != PanelTab.Notes) Notes.Leave();
    }

    public void Start()
    {
        Media.Start();
        Shelf.Load();
        Snippets.Reload();
        // Подхватывает, где остановились, только если вход был выдан раньше;
        // сам никогда ничего не спрашивает.
        Calendar.Start();
        Plugins.Load();

        // Выключатель спрашивается хранилищем до того, как оно тронет данные
        // картинки, а не здесь постфактум.
        Clipboard.WantsImages = () => Settings.SaveClipboardImages;
        Clipboard.OnImage = image =>
        {
            var path = ScreenshotVault.Save(image);
            if (path is null) return;
            Shelf.Add([path]);
            Tab = PanelTab.Shelf;
        };
    }

    public void Stop()
    {
        Media.Stop();
        Calendar.Stop();
        Plugins.StopAll();
        // Набранное доезжает до диска даже при выходе посреди мысли.
        Notes.Flush();
    }

    /// <summary>Файлы, брошенные на панель.</summary>
    public bool Accept(IReadOnlyList<string> paths)
    {
        Shelf.Add(paths);
        Tab = PanelTab.Shelf;
        return true;
    }

    // MARK: - Рейлы

    public ObservableCollection<PanelTab> LeftRail { get; }

    /// <summary>
    /// Правый рейл достраивается на ходу: сюда становятся иконки плагинов,
    /// найденных в <c>%APPDATA%\Popup\plugins</c>.
    /// </summary>
    public ObservableCollection<PanelTab> RightRail { get; }

    public IEnumerable<PanelTab> AllTabs => LeftRail.Concat(RightRail);

    /// <summary>
    /// Пересобирает правый рейл после перезагрузки плагинов.
    ///
    /// Заметки и разработка остаются на своих местах, иконки плагинов встают
    /// под ними в том порядке, в котором лежат папки. Если открытая вкладка
    /// принадлежала плагину, который исчез, панель возвращается на музыку —
    /// показывать вкладку без содержимого хуже, чем сменить её.
    /// </summary>
    private void RebuildRightRail()
    {
        var openId = Tab.Id;

        RightRail.Clear();
        RightRail.Add(PanelTab.Notes);
        RightRail.Add(PanelTab.Dev);
        foreach (var plugin in Plugins.Plugins)
        {
            RightRail.Add(new PanelTab(
                $"plugin:{plugin.Manifest.Id}",
                plugin.Manifest.Name,
                plugin.Manifest.Icon)
            {
                PluginId = plugin.Manifest.Id,
            });
        }

        // Пишем в поле напрямую и объявляем об изменении вручную: вкладка может
        // называться так же, как до перезагрузки, и обычное присваивание сочло
        // бы, что ничего не произошло. Перерисоваться нужно в любом случае —
        // иначе на экране останется содержимое от процесса, которого больше нет.
        _tab = AllTabs.FirstOrDefault(t => t.Id == openId) ?? PanelTab.Media;
        Raise(nameof(Tab));
    }

    // MARK: - Открытость

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!Set(ref _isOpen, value)) return;
            Raise(nameof(IsExpanded));
            Raise(nameof(State));
            Raise(nameof(BodySize));
        }
    }

    private bool _isDropTargeted;
    public bool IsDropTargeted
    {
        get => _isDropTargeted;
        set
        {
            if (!Set(ref _isDropTargeted, value)) return;
            Raise(nameof(IsExpanded));
            Raise(nameof(State));
            Raise(nameof(BodySize));
        }
    }

    /// <summary>Панель развёрнута — по наведению или потому, что на неё тащат файл.</summary>
    public bool IsExpanded => IsOpen || IsDropTargeted;

    /// <summary>
    /// Во что развёрнута панель. Раскрытие важнее активности: человек навёл
    /// мышь, значит он пришёл за вкладками, а не за полоской.
    /// </summary>
    public PanelState State =>
        IsExpanded ? PanelState.Expanded :
        Activity.IsActive ? PanelState.Activity :
        PanelState.Collapsed;

    /// <summary>Позвать, когда панель должна перерисоваться в другой размер.</summary>
    public event Action<PanelState>? StateChanged;

    public Size BodySize => Geometry.BodySize(State);

    // MARK: - Вкладка

    private PanelTab _tab;
    public PanelTab Tab
    {
        get => _tab;
        set
        {
            var previous = _tab;
            if (!Set(ref _tab, value)) return;
            TabChanged?.Invoke(previous, value);
            // Уход со вкладки, которая печатает, сразу возвращает клавиатуру.
            if (!value.NeedsKeyboard) WantsKeyboard = false;
        }
    }

    /// <summary>
    /// Старая и новая вкладка. Через это событие хранилища узнают, что пора
    /// перечитать файл на входе или подмести пустые заметки на выходе, — без
    /// того чтобы модель знала о каждом из них поимённо.
    /// </summary>
    public event Action<PanelTab, PanelTab>? TabChanged;

    /// <summary>
    /// Держит ли панель клавиатуру.
    ///
    /// Отдельно от <see cref="Tab"/>, потому что эти два расходятся в одну
    /// сторону: клик в другое приложение снимает claim, не меняя вкладку, — и
    /// набранное остаётся на месте, а панель свободна схлопнуться. Попадание на
    /// вкладку, которая печатает, всегда поднимает флаг обратно: панели,
    /// показывающей поле, в которое нельзя печатать, не бывает.
    /// </summary>
    private bool _wantsKeyboard;
    public bool WantsKeyboard
    {
        get => _wantsKeyboard;
        set
        {
            if (Set(ref _wantsKeyboard, value)) KeyboardWanted?.Invoke(value);
        }
    }

    public event Action<bool>? KeyboardWanted;

    /// <summary>
    /// Сюда приходят и наведение, и клик. Вкладка с полем забирает клавиатуру в
    /// обоих случаях: показать поле, в которое нельзя печатать, хуже, чем на
    /// миг притушить курсор в окне под панелью, — а задержка на рейле и так не
    /// пускает сюда мышь, которая просто пролетала мимо.
    /// </summary>
    public void Select(PanelTab tab)
    {
        Tab = tab;
        if (tab.NeedsKeyboard) WantsKeyboard = true;
    }
}
