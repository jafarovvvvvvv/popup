using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using Popup.Model;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// Заготовки текста. Порт <c>SnippetsPane.swift</c>.
///
/// Поиск наверху, список под ним, клик копирует. Добавляется то, что сейчас в
/// буфере: заготовку почти всегда сначала откуда-то копируют, и заставлять
/// набирать её второй раз руками — значит просить сделать работу дважды.
/// </summary>
public sealed class SnippetsPane : Grid
{
    private readonly SnippetStore _store;
    private readonly ClipboardStore _clipboard;
    private readonly PrivacyMode _privacy;
    private readonly StackPanel _rows = new();
    private readonly TextBox _search;

    public SnippetsPane(SnippetStore store, ClipboardStore clipboard, PrivacyMode privacy)
    {
        _store = store;
        _clipboard = clipboard;
        _privacy = privacy;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _search = PaneKit.Field();
        _search.Text = _store.Query;
        _search.TextChanged += (_, _) =>
        {
            _store.Query = _search.Text;
            Rebuild();
        };

        var top = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchBox = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = PaneKit.Brush("Surface"),
            Padding = new Thickness(8, 4, 8, 4),
            Child = PaneKit.WithPlaceholder(_search, "Search"),
        };
        SetColumn(searchBox, 0);
        top.Children.Add(searchBox);

        var add = PaneKit.IconButton("IconPlus", AddFromClipboard);
        add.Margin = new Thickness(6, 0, 0, 0);
        add.ToolTip = Strings.L("Add from clipboard");
        SetColumn(add, 1);
        top.Children.Add(add);

        SetRow(top, 0);
        Children.Add(top);

        var list = PaneKit.List(_rows);
        SetRow(list, 1);
        Children.Add(list);

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

    /// <summary>Заготовка из того, что сейчас в буфере.</summary>
    private void AddFromClipboard()
    {
        var latest = _clipboard.Items.FirstOrDefault(i => i.Text is not null)?.Text;
        if (latest is null) return;
        _store.Add("", latest);
    }

    private void Rebuild()
    {
        _rows.Children.Clear();

        if (_store.FileBroken)
        {
            // Сказать вслух, а не показать пустой список: молчание здесь — это
            // то, из-за чего случайная запятая в файле выглядит как потеря всех
            // заготовок.
            var warning = PaneKit.Text("snippets.json не читается", 12, "TextSecondary");
            warning.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            warning.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            warning.TextTrimming = TextTrimming.None;
            _rows.Children.Add(warning);
            return;
        }

        var items = _store.Filtered.ToList();
        if (items.Count == 0)
        {
            _rows.Children.Add(PaneKit.Empty("No snippets yet"));
            return;
        }

        foreach (var snippet in items)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = PaneKit.Icon(snippet.IconKey, "TextTertiary", 0.8);
            icon.Margin = new Thickness(0, 0, 8, 0);
            SetColumn(icon, 0);
            line.Children.Add(icon);

            var column = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            if (snippet.Label.Length > 0)
            {
                column.Children.Add(PaneKit.Text(snippet.Label, 12, "TextPrimary"));
                column.Children.Add(PaneKit.Text(snippet.Text, 10.5, "TextTertiary"));
            }
            else
            {
                column.Children.Add(PaneKit.Text(snippet.Text, 12, "TextPrimary"));
            }
            PaneKit.ApplyCover(column, _privacy.Hides(PrivacyMode.Section.Snippets, snippet.Id));
            SetColumn(column, 1);
            line.Children.Add(column);

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            if (_privacy.Covers(PrivacyMode.Section.Snippets))
            {
                var covered = _privacy.Hides(PrivacyMode.Section.Snippets, snippet.Id);
                var id = snippet.Id;
                actions.Children.Add(PaneKit.IconButton(
                    covered ? "IconEye" : "IconEyeOff", () => _privacy.Toggle(id)));
            }
            actions.Children.Add(PaneKit.IconButton("IconTrash", () => _store.Remove(snippet)));
            SetColumn(actions, 2);
            line.Children.Add(actions);

            _rows.Children.Add(PaneKit.Row(line, () => _clipboard.CopyText(snippet.Text)));
        }
    }
}
