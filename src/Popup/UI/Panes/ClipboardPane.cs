using System.Windows.Controls;
using System.Windows.Media;
using Popup.Model;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// История буфера обмена. Порт <c>ClipboardPane.swift</c>.
///
/// Клик по строке возвращает её в буфер — это единственное действие, ради
/// которого вкладку открывают, и оно не спрятано ни за какой кнопкой.
/// </summary>
public sealed class ClipboardPane : Grid
{
    private readonly ClipboardStore _store;
    private readonly PrivacyMode _privacy;
    private readonly StackPanel _rows = new();

    public ClipboardPane(ClipboardStore store, PrivacyMode privacy)
    {
        _store = store;
        _privacy = privacy;

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

        var list = PaneKit.List(_rows);
        SetRow(list, 1);
        Children.Add(list);

        // Панель пересоздаётся на каждом переключении вкладки, поэтому подписки
        // обязаны сниматься: иначе к вечеру на одно хранилище накапливается
        // сотня мёртвых обработчиков, каждый из которых перерисовывает список,
        // которого больше нет на экране.
        _store.PropertyChanged += OnChanged;
        _privacy.PropertyChanged += OnChanged;
        Unloaded += (_, _) =>
        {
            _store.PropertyChanged -= OnChanged;
            _privacy.PropertyChanged -= OnChanged;
        };

        Rebuild();
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _rows.Children.Clear();

        if (_store.Items.Count == 0)
        {
            _rows.Children.Add(PaneKit.Empty("Clipboard is empty"));
            return;
        }

        foreach (var entry in _store.Items)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = PaneKit.Icon(entry.IsImage ? "IconShelf" : "IconCopy", "TextTertiary", 0.8);
            icon.Margin = new Thickness(0, 0, 8, 0);
            SetColumn(icon, 0);
            line.Children.Add(icon);

            var preview = PaneKit.Text(entry.Preview, 12, "TextPrimary");
            PaneKit.ApplyCover(preview, _privacy.Hides(PrivacyMode.Section.Clipboard, entry.Id));
            SetColumn(preview, 1);
            line.Children.Add(preview);

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            if (_privacy.Covers(PrivacyMode.Section.Clipboard))
            {
                var covered = _privacy.Hides(PrivacyMode.Section.Clipboard, entry.Id);
                actions.Children.Add(PaneKit.IconButton(
                    covered ? "IconEye" : "IconEyeOff",
                    () => _privacy.Toggle(entry.Id)));
            }
            actions.Children.Add(PaneKit.IconButton("IconClose", () => _store.Remove(entry)));
            SetColumn(actions, 2);
            line.Children.Add(actions);

            _rows.Children.Add(PaneKit.Row(line, () => _store.Copy(entry)));
        }
    }
}
