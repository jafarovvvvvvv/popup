using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Popup.Model;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// Календарь: сетка месяца слева, встречи выбранного дня справа.
///
/// У оригинала здесь только список ближайших встреч — вырез MacBook узкий, и
/// сетка в него не влезала. Здесь места хватает, а список без чисел отвечает
/// лишь на вопрос «что дальше» и молчит на вопрос «а что в четверг». Поэтому
/// слева настоящий месяц, который листается, а список стал тем, что показывает
/// выбранный день.
///
/// Кнопка входа в Google живёт тут же и нажимается рукой: это единственное
/// разрешение, которое панель просит, и оно заслуживает объяснения до
/// системного окна, а не после.
/// </summary>
public sealed class CalendarPane : Grid
{
    private const double CellWidth = 27;
    private const double CellHeight = 22;

    private readonly CalendarStore _store;
    private readonly PrivacyMode _privacy;
    private readonly CultureInfo _culture = Strings.Culture;

    private readonly Grid _root = new();
    private readonly TextBlock _monthTitle;
    private readonly Grid _cells = new();
    private readonly TextBlock _dayTitle;
    private readonly StackPanel _dayEvents = new();

    private DateTime _month = DateTime.Today;
    private DateTime _selected = DateTime.Today;

    public CalendarPane(CalendarStore store, PrivacyMode privacy)
    {
        _store = store;
        _privacy = privacy;

        _monthTitle = PaneKit.Text("", 11.5, "TextPrimary", bold: true);
        _monthTitle.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        _monthTitle.TextTrimming = TextTrimming.None;

        _dayTitle = PaneKit.Text("", 10, "TextTertiary", bold: true);
        _dayTitle.TextTrimming = TextTrimming.None;

        Children.Add(_root);

        _store.PropertyChanged += OnChanged;
        _privacy.PropertyChanged += OnChanged;
        Unloaded += (_, _) =>
        {
            _store.PropertyChanged -= OnChanged;
            _privacy.PropertyChanged -= OnChanged;
        };

        Rebuild();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    // MARK: - Состояния до календаря

    /// <summary>
    /// Сетка показывается всегда, даже когда никакой календарь не подключён.
    ///
    /// Числа и дни недели полезны сами по себе: «какое число в четверг» —
    /// вопрос, ради которого не нужен ни аккаунт, ни согласие. Подключение
    /// добавляет к сетке встречи, а не включает её, поэтому приглашение войти
    /// живёт в колонке дня и не занимает собой всю вкладку.
    /// </summary>
    private void Rebuild()
    {
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        _root.RowDefinitions.Clear();
        BuildCalendar();
    }

    /// <summary>Приглашение подключить источник встреч — под подписью дня.</summary>
    private UIElement SourceHint()
    {
        var column = new StackPanel { Margin = new Thickness(2, 6, 0, 0) };

        if (_store.IsMisconfigured)
        {
            // Ключ вписан, но неправильный. Сказать это здесь, а не отправлять
            // человека к Google за «401 invalid_client»: там он узнает только
            // то, что что-то не так, и ни слова о том, что именно.
            column.Children.Add(Wrapped(Strings.L("googleClientId looks wrong")));
            return column;
        }

        if (!_store.IsConfigured)
        {
            column.Children.Add(Wrapped(Strings.L("Set googleClientId in settings.json")));
            return column;
        }

        column.Children.Add(Wrapped(Strings.L("Meetings appear once a calendar is connected")));

        var button = PaneKit.TextButton(
            _store.IsBusy ? Strings.L("Connecting…") : Strings.L("Connect Google Calendar"),
            () => _ = _store.ConnectAsync());
        button.Margin = new Thickness(0, 8, 0, 0);
        button.IsEnabled = !_store.IsBusy;
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        column.Children.Add(button);

        return column;
    }

    private static TextBlock Wrapped(string text)
    {
        var label = PaneKit.Text(text, 11, "TextTertiary");
        label.TextTrimming = TextTrimming.None;
        label.TextWrapping = TextWrapping.Wrap;
        return label;
    }

    // MARK: - Календарь

    private void BuildCalendar()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = BuildMonth();
        SetColumn(left, 0);
        _root.Children.Add(left);

        var right = BuildDay();
        SetColumn(right, 1);
        _root.Children.Add(right);

        RefreshMonth();
        RefreshDay();
        LoadVisibleRange();
    }

    private UIElement BuildMonth()
    {
        var column = new Grid { Width = CellWidth * 7 };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Заголовок со стрелками
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var back = Arrow("‹", () => Page(-1));
        SetColumn(back, 0);
        header.Children.Add(back);

        SetColumn(_monthTitle, 1);
        header.Children.Add(_monthTitle);

        var forward = Arrow("›", () => Page(1));
        SetColumn(forward, 2);
        header.Children.Add(forward);

        SetRow(header, 0);
        column.Children.Add(header);

        // Подписи дней недели
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 4, 0, 2) };
        foreach (var name in MonthGrid.WeekdayNames(_culture))
        {
            var label = PaneKit.Text(name.ToLower(_culture), 8.5, "TextTertiary");
            label.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            label.TextTrimming = TextTrimming.None;
            weekdays.Children.Add(label);
        }
        SetRow(weekdays, 1);
        column.Children.Add(weekdays);

        _cells.ColumnDefinitions.Clear();
        _cells.RowDefinitions.Clear();
        for (var c = 0; c < 7; c++)
            _cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
        for (var r = 0; r < MonthGrid.Weeks; r++)
            _cells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });

        SetRow(_cells, 2);
        column.Children.Add(_cells);

        // Колесо листает месяцы: до стрелок ещё надо доехать, а колесо уже под
        // пальцем — и это движение в календаре означает ровно одно.
        column.MouseWheel += (_, e) =>
        {
            Page(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        };

        return column;
    }

    private Button Arrow(string glyph, Action onClick)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("NotchButton"),
            Width = 18,
            Height = 18,
            Content = PaneKit.Text(glyph, 14, "TextSecondary"),
        };
        ((TextBlock)button.Content).HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        button.Click += (_, _) => onClick();
        return button;
    }

    private void Page(int months)
    {
        _month = _month.AddMonths(months);
        RefreshMonth();
        LoadVisibleRange();
    }

    /// <summary>
    /// Подгружает то, что сейчас на экране. Сетка листается дальше загруженного,
    /// и без этого соседний месяц выглядел бы пустым, хотя встречи в нём есть.
    /// </summary>
    private void LoadVisibleRange()
    {
        if (_store.NeedsSignIn || !_store.IsConfigured) return;
        var first = MonthGrid.FirstCell(_month, _culture);
        _ = _store.EnsureRangeAsync(first, first.AddDays(MonthGrid.Days));
    }

    private void RefreshMonth()
    {
        _monthTitle.Text = MonthGrid.Title(_month, _culture);
        _cells.Children.Clear();

        var index = 0;
        foreach (var day in MonthGrid.Cells(_month, _culture))
        {
            var cell = BuildCell(day);
            SetColumn(cell, index % 7);
            SetRow(cell, index / 7);
            _cells.Children.Add(cell);
            index++;
        }
    }

    private UIElement BuildCell(DateTime day)
    {
        var isToday = day.Date == DateTime.Today;
        var isSelected = day.Date == _selected.Date;
        // Дни соседних месяцев показываем, но приглушённо: без них сетка
        // разрывается, а с ними видно, что неделя продолжается.
        var isOutside = day.Month != _month.Month;

        var content = new Grid();

        var number = PaneKit.Text(day.Day.ToString(CultureInfo.InvariantCulture), 10.5,
            isSelected ? "TextPrimary" : isOutside ? "TextTertiary" : "TextSecondary");
        number.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        number.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        number.Margin = new Thickness(0, 0, 0, 3);
        number.TextTrimming = TextTrimming.None;
        if (isToday) number.FontWeight = FontWeights.Bold;
        content.Children.Add(number);

        if (_store.HasAnythingOn(day))
        {
            // Точка под числом — единственное, что нужно знать о дне, не
            // открывая его: есть там что-то или нет.
            content.Children.Add(new Ellipse
            {
                Width = 3,
                Height = 3,
                Fill = PaneKit.Brush(isSelected ? "TextPrimary" : "TextSecondary"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 3),
            });
        }

        var cell = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(1),
            Cursor = Cursors.Hand,
            Background = isSelected ? PaneKit.Brush("SurfaceHover") : Brushes.Transparent,
        };

        // Сегодняшнее число обведено, а не залито: заливка — это выбор, и два
        // разных состояния не должны выглядеть одинаково.
        if (isToday && !isSelected)
        {
            cell.BorderThickness = new Thickness(1);
            cell.BorderBrush = PaneKit.Brush("SurfaceHover");
        }

        cell.MouseEnter += (_, _) =>
        {
            if (day.Date != _selected.Date) cell.Background = PaneKit.Brush("Surface");
        };
        cell.MouseLeave += (_, _) =>
        {
            if (day.Date != _selected.Date) cell.Background = Brushes.Transparent;
        };
        cell.MouseLeftButtonUp += (_, _) => Select(day);

        return cell;
    }

    private void Select(DateTime day)
    {
        _selected = day.Date;
        // Клик по числу соседнего месяца заодно туда и переходит: иначе выбор
        // оказывается за пределами показанной сетки.
        if (day.Month != _month.Month) _month = new DateTime(day.Year, day.Month, 1);
        RefreshMonth();
        RefreshDay();
    }

    // MARK: - День

    private UIElement BuildDay()
    {
        var column = new Grid { Margin = new Thickness(12, 0, 0, 0) };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new Grid { Margin = new Thickness(2, 1, 0, 5) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        SetColumn(_dayTitle, 0);
        head.Children.Add(_dayTitle);

        var today = PaneKit.TextButton(Strings.L("Today"), () =>
        {
            _month = DateTime.Today;
            Select(DateTime.Today);
        });
        today.Height = 18;
        SetColumn(today, 1);
        head.Children.Add(today);

        SetRow(head, 0);
        column.Children.Add(head);

        var list = PaneKit.List(_dayEvents);
        SetRow(list, 1);
        column.Children.Add(list);

        return column;
    }

    private void RefreshDay()
    {
        _dayTitle.Text = MonthGrid.DayTitle(_selected, _culture);
        _dayEvents.Children.Clear();

        // Источника встреч нет — говорим об этом здесь, один раз, вместо того
        // чтобы на каждый день писать «ничего»: это была бы неправда.
        if (_store.NeedsSignIn || !_store.IsConfigured || _store.IsMisconfigured)
        {
            _dayEvents.Children.Add(SourceHint());
            return;
        }

        var meetings = _store.On(_selected).ToList();
        if (meetings.Count == 0)
        {
            var empty = PaneKit.Text(Strings.L("Nothing on this day"), 11.5, "TextTertiary");
            empty.Margin = new Thickness(2, 6, 0, 0);
            empty.TextTrimming = TextTrimming.None;
            _dayEvents.Children.Add(empty);
            return;
        }

        foreach (var meeting in meetings) _dayEvents.Children.Add(Row(meeting));
    }

    private UIElement Row(CalendarStore.Meeting meeting)
    {
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var time = PaneKit.Text(
            meeting.AllDay ? Strings.L("All day") : meeting.Start.ToString("HH:mm", _culture),
            10.5,
            meeting.IsRunning ? "TextPrimary" : "TextTertiary");
        time.TextTrimming = TextTrimming.None;
        SetColumn(time, 0);
        line.Children.Add(time);

        var title = PaneKit.Text(meeting.Title, 11.5, "TextPrimary");
        PaneKit.ApplyCover(title, _privacy.Hides(PrivacyMode.Section.Calendar, meeting.Id));
        SetColumn(title, 1);
        line.Children.Add(title);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };

        // Обратный отсчёт только у ближайшей: у всех остальных он превращает
        // список в стену цифр, из которой не видно ни одной встречи.
        if (ReferenceEquals(meeting, _store.Next))
        {
            var countdown = PaneKit.Text(CalendarStore.Countdown(meeting), 9.5,
                meeting.IsRunning ? "TextSecondary" : "TextTertiary");
            countdown.Margin = new Thickness(0, 0, 4, 0);
            countdown.TextTrimming = TextTrimming.None;
            actions.Children.Add(countdown);
        }

        if (meeting.JoinUrl is { } url)
        {
            var join = PaneKit.IconButton("IconLink", () => Shell.OpenUrl(url), 20);
            join.ToolTip = Strings.L("Join");
            actions.Children.Add(join);
        }

        if (_privacy.Covers(PrivacyMode.Section.Calendar))
        {
            var covered = _privacy.Hides(PrivacyMode.Section.Calendar, meeting.Id);
            var id = meeting.Id;
            actions.Children.Add(PaneKit.IconButton(
                covered ? "IconEye" : "IconEyeOff", () => _privacy.Toggle(id), 20));
        }

        SetColumn(actions, 2);
        line.Children.Add(actions);

        return PaneKit.Row(line, meeting.JoinUrl is { } target ? () => Shell.OpenUrl(target) : null);
    }

    private static UIElement Centered(string text)
    {
        var label = PaneKit.Text(text, 12, "TextTertiary");
        label.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        label.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        label.TextTrimming = TextTrimming.None;
        label.TextWrapping = TextWrapping.Wrap;
        label.TextAlignment = TextAlignment.Center;
        return label;
    }
}
