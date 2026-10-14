using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Popup.Plugins;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// Вкладка разработки — рабочее место для интеграций.
///
/// Того, что здесь есть, ровно столько, сколько нужно, чтобы писать плагин, не
/// выходя из панели: живой журнал разговора, список процессов с их состоянием,
/// перезагрузка без перезапуска Popup и строка, из которой можно отправить
/// плагину любой JSON руками.
///
/// Последнее важнее, чем кажется: пока плагин пишется, половина времени уходит
/// на вопрос «а что будет, если ему прислать вот это», и отвечать на него
/// правкой кода — самый долгий из возможных способов.
/// </summary>
public sealed class DevPane : Grid
{
    private readonly PluginHost _host;
    private readonly TextBox _console;
    private readonly TextBlock _trace;
    private readonly ScrollViewer _traceScroller;
    private readonly ComboBox _target;

    public DevPane(PluginHost host)
    {
        _host = host;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Левая колонка: плагины и кнопки
        var left = new StackPanel();
        left.Children.Add(BuildActions());
        left.Children.Add(BuildPluginList());
        SetColumn(left, 0);
        Children.Add(left);

        // Правая колонка: журнал и консоль
        var right = new Grid { Margin = new Thickness(10, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _trace = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 10,
            Foreground = PaneKit.Brush("TextSecondary"),
            TextWrapping = TextWrapping.NoWrap,
            Text = string.Join(Environment.NewLine, _host.Trace),
        };
        _traceScroller = new ScrollViewer
        {
            Content = _trace,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
        };
        var traceBox = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = PaneKit.Brush("Surface"),
            Padding = new Thickness(8, 6, 4, 6),
            Child = _traceScroller,
        };
        SetRow(traceBox, 0);
        right.Children.Add(traceBox);

        var consoleRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        consoleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        consoleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        consoleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _target = new ComboBox
        {
            Width = 92,
            FontSize = 10,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
        };
        RefreshTargets();
        SetColumn(_target, 0);
        consoleRow.Children.Add(_target);

        _console = PaneKit.Field();
        _console.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
        _console.FontSize = 10.5;
        _console.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            SendConsole();
            e.Handled = true;
        };
        var consoleBox = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = PaneKit.Brush("Surface"),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(6, 0, 6, 0),
            Child = PaneKit.WithPlaceholder(_console, "{\"type\":\"render\"}"),
        };
        SetColumn(consoleBox, 1);
        consoleRow.Children.Add(consoleBox);

        var send = PaneKit.IconButton("IconSend", SendConsole);
        SetColumn(send, 2);
        consoleRow.Children.Add(send);

        SetRow(consoleRow, 1);
        right.Children.Add(consoleRow);

        SetColumn(right, 1);
        Children.Add(right);

        _host.Traced += Append;
        _host.PluginsChanged += OnPluginsChanged;
        Unloaded += (_, _) =>
        {
            _host.Traced -= Append;
            _host.PluginsChanged -= OnPluginsChanged;
        };

        Dispatcher.BeginInvoke(() => _traceScroller.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private UIElement BuildActions()
    {
        var column = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var reload = PaneKit.TextButton(Strings.L("Reload plugins"), () => _host.Load());
        reload.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        reload.Margin = new Thickness(0, 0, 0, 4);
        column.Children.Add(reload);

        var open = PaneKit.TextButton(Strings.L("Open plugins folder"),
            () => Shell.OpenFolder(AppPaths.Plugins));
        open.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        column.Children.Add(open);

        return column;
    }

    private readonly StackPanel _pluginList = new();

    private UIElement BuildPluginList()
    {
        RefreshPluginList();
        return PaneKit.List(_pluginList);
    }

    private void RefreshPluginList()
    {
        _pluginList.Children.Clear();
        if (_host.Plugins.Count == 0)
        {
            _pluginList.Children.Add(PaneKit.Text(Strings.L("No plugins installed"), 11, "TextTertiary"));
            return;
        }

        foreach (var plugin in _host.Plugins)
        {
            var line = new StackPanel();
            line.Children.Add(PaneKit.Text(plugin.Manifest.Name, 11.5, "TextPrimary"));
            line.Children.Add(PaneKit.Text(plugin.Status, 10, "TextTertiary"));
            _pluginList.Children.Add(PaneKit.Row(line));
        }
    }

    private void OnPluginsChanged()
    {
        RefreshPluginList();
        RefreshTargets();
    }

    private void RefreshTargets()
    {
        _target.Items.Clear();
        foreach (var plugin in _host.Plugins) _target.Items.Add(plugin.Manifest.Id);
        if (_target.Items.Count > 0) _target.SelectedIndex = 0;
    }

    private void SendConsole()
    {
        var json = _console.Text.Trim();
        if (json.Length == 0) return;
        if (_target.SelectedItem is not string id) return;
        if (_host.Find(id) is not { } plugin) return;
        _host.SendRaw(plugin, json);
        _console.Clear();
    }

    private void Append(string line)
    {
        _trace.Text = _trace.Text.Length == 0 ? line : _trace.Text + Environment.NewLine + line;
        // Прокрутка вниз — иначе живой журнал живёт за нижним краем и смотреть
        // на него приходится, каждый раз догоняя колесом.
        _traceScroller.ScrollToEnd();
    }
}
