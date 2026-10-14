using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// Переводчик. Порт <c>TranslatePane.swift</c>.
///
/// Две колонки, слева набирают — справа появляется. Языки не выбираются:
/// направление читается по алфавиту, и в панели, которая живёт ровно столько,
/// сколько на ней курсор, лишний выпадающий список стоит дороже, чем изредка
/// угаданное не то направление.
/// </summary>
public sealed class TranslatePane : Grid
{
    private readonly Translator _translator;
    private readonly ClipboardStore _clipboard;
    private readonly TextBox _input;
    private readonly TextBox _output;
    private readonly TextBlock _fromLabel;
    private readonly TextBlock _toLabel;
    private readonly TextBlock _status;

    private TextBox? _keyBox;
    private Border? _keyRow;

    private bool _syncing;

    public TranslatePane(Translator translator, ClipboardStore clipboard)
    {
        _translator = translator;
        _clipboard = clipboard;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _input = PaneKit.Field(multiline: true);
        _input.FontSize = 12.5;
        _input.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            _translator.Source = _input.Text;
        };
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            // Esc очищает поле — это единственный способ выбросить набранное,
            // не выделяя его мышью. Но только пока есть что выбрасывать: над
            // пустым полем то же нажатие должно закрывать панель, и для этого
            // его надо пропустить дальше.
            if (_input.Text.Length == 0) return;
            _translator.Clear();
            e.Handled = true;
        };

        _fromLabel = PaneKit.Text(_translator.FromLanguage, 9, "TextTertiary", bold: true);
        _toLabel = PaneKit.Text(_translator.ToLanguage, 9, "TextTertiary", bold: true);

        _output = PaneKit.Field(multiline: true);
        _output.FontSize = 12.5;
        _output.IsReadOnly = true;
        _output.Foreground = PaneKit.Brush("TextPrimary");

        _status = PaneKit.Text("", 9.5, "TextTertiary");
        _status.TextTrimming = TextTrimming.CharacterEllipsis;

        var left = Column(_fromLabel, _input, null);
        SetColumn(left, 0);
        Children.Add(left);

        var right = Column(_toLabel, _output, _status);
        SetColumn(right, 1);
        right.Margin = new Thickness(10, 0, 0, 0);
        Children.Add(right);

        _translator.PropertyChanged += OnChanged;
        Unloaded += (_, _) => _translator.PropertyChanged -= OnChanged;

        Refresh();
    }

    /// <summary>Заголовок языка, поле и строка состояния — одинаково с обеих сторон.</summary>
    private Grid Column(TextBlock label, TextBox field, TextBlock? status)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid { Margin = new Thickness(2, 0, 0, 4) };
        head.Children.Add(label);
        SetRow(head, 0);
        grid.Children.Add(head);

        var box = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = PaneKit.Brush("Surface"),
            Padding = new Thickness(9, 7, 5, 7),
            Child = field,
        };
        SetRow(box, 1);
        grid.Children.Add(box);

        if (status is not null)
        {
            var footer = new Grid { Margin = new Thickness(2, 4, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SetColumn(status, 0);
            footer.Children.Add(status);

            var key = PaneKit.IconButton("IconKey", ToggleKeyField, 18);
            key.ToolTip = Strings.L("Translation key");
            SetColumn(key, 1);
            footer.Children.Add(key);

            var copy = PaneKit.IconButton("IconCopy", () =>
            {
                if (_output.Text.Length > 0) _clipboard.CopyText(_output.Text);
            }, 18);
            SetColumn(copy, 2);
            footer.Children.Add(copy);

            SetRow(footer, 2);
            grid.Children.Add(footer);

            // Поле ключа живёт под строкой состояния и показывается по кнопке
            // или само, когда ключа нет и переводить нечем.
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _keyBox = PaneKit.Field();
            _keyBox.FontSize = 11;
            _keyBox.PreviewKeyDown += OnKeyBoxKeyDown;
            _keyRow = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = PaneKit.Brush("Surface"),
                Padding = new Thickness(9, 4, 5, 4),
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
                Child = PaneKit.WithPlaceholder(_keyBox, "Translation API key — Enter to save"),
            };
            SetRow(_keyRow, 3);
            grid.Children.Add(_keyRow);
        }

        return grid;
    }

    private void ToggleKeyField()
    {
        var show = _keyRow!.Visibility != Visibility.Visible;
        _keyRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) _keyBox!.Focus();
    }

    private void OnKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _translator.SetKey(_keyBox!.Text);
            _keyBox.Clear();
            _keyRow!.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _keyBox!.Clear();
            _keyRow!.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        _fromLabel.Text = _translator.FromLanguage;
        _toLabel.Text = _translator.ToLanguage;

        _syncing = true;
        if (_input.Text != _translator.Source) _input.Text = _translator.Source;
        _syncing = false;

        _output.Text = _translator.Result;
        _status.Text = _translator.Error ?? (_translator.IsBusy ? "…" : "");
        _status.Foreground = PaneKit.Brush(_translator.Error is null ? "TextTertiary" : "TextSecondary");
        if (!_translator.HasKey && _keyRow is not null) _keyRow.Visibility = Visibility.Visible;
    }
}
