using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Popup.Model;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>
/// Черновики. Порт <c>NotesPane.swift</c>.
///
/// Слева столбик заметок, справа поле. Всё, что нужно, — записать и потом
/// унести через буфер: ни папок, ни форматирования здесь нет намеренно.
/// </summary>
public sealed class NotesPane : Grid
{
    private const double ListWidth = 132;

    private readonly NoteStore _store;
    private readonly PrivacyMode _privacy;
    private readonly StackPanel _rows = new();
    private readonly TextBox _editor;

    /// <summary>
    /// Поднят, пока текст в поле меняется нашей же рукой. Без него запись
    /// выбранной заметки в поле выглядела бы для хранилища как правка от
    /// человека и обнуляла бы время изменения при каждом переключении.
    /// </summary>
    private bool _syncing;

    public NotesPane(NoteStore store, PrivacyMode privacy)
    {
        _store = store;
        _privacy = privacy;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ListWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Поле заводится первым: на него ссылается кнопка «новая заметка»,
        // которая сразу отдаёт ему фокус.
        _editor = PaneKit.Field(multiline: true);
        _editor.FontSize = 12.5;
        _editor.TextChanged += (_, _) =>
        {
            if (_syncing || _store.SelectedId is not { } id) return;
            _store.Update(id, _editor.Text);
            RefreshRowTitles();
        };

        // Левая колонка: кнопка «новая» и список
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var add = PaneKit.TextButton(Strings.L("New note"), () =>
        {
            _store.Add();
            Rebuild();
            _editor.Focus();
        });
        add.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        add.Margin = new Thickness(0, 0, 0, 6);
        SetRow(add, 0);
        left.Children.Add(add);

        var list = PaneKit.List(_rows);
        SetRow(list, 1);
        left.Children.Add(list);

        SetColumn(left, 0);
        Children.Add(left);

        // Правая колонка: поле
        var editorBox = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = PaneKit.Brush("Surface"),
            Padding = new Thickness(10, 8, 6, 8),
            Margin = new Thickness(10, 0, 0, 0),
            Child = PaneKit.WithPlaceholder(_editor, "New note"),
        };
        SetColumn(editorBox, 1);
        Children.Add(editorBox);

        _store.PropertyChanged += OnChanged;
        _privacy.PropertyChanged += OnChanged;
        Unloaded += (_, _) =>
        {
            _store.PropertyChanged -= OnChanged;
            _privacy.PropertyChanged -= OnChanged;
        };

        // Чистый лист сразу, без нажатия «Новая заметка».
        //
        // Черновики заводят, чтобы записать мысль немедленно, и вкладка, на
        // которую пришли и начали печатать в никуда, ровно эту задачу и
        // проваливает. Ничего не стоит: пустая заметка подметается сама при
        // уходе со вкладки.
        if (_store.Selected is null) _store.Add();

        Rebuild();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void Select(string id)
    {
        _store.SelectedId = id;
        Rebuild();
        _editor.Focus();
        _editor.CaretIndex = _editor.Text.Length;
    }

    private void Rebuild()
    {
        _rows.Children.Clear();

        _syncing = true;
        _editor.Text = _store.Selected?.Text ?? "";
        _syncing = false;
        _editor.IsEnabled = _store.Selected is not null;

        if (_store.Notes.Count == 0)
        {
            _rows.Children.Add(PaneKit.Empty("No notes"));
            return;
        }

        foreach (var note in _store.Notes)
        {
            var selected = note.Id == _store.SelectedId;

            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = PaneKit.Text(FirstLine(note.Text), 11.5, selected ? "TextPrimary" : "TextSecondary");
            title.Tag = note.Id;
            PaneKit.ApplyCover(title, _privacy.Hides(PrivacyMode.Section.Notes, note.Id));
            SetColumn(title, 0);
            line.Children.Add(title);

            var id = note.Id;
            var remove = PaneKit.IconButton("IconClose", () => { _store.Remove(id); Rebuild(); }, 18);
            SetColumn(remove, 1);
            line.Children.Add(remove);

            var row = PaneKit.Row(line, () => Select(id));
            if (selected) row.Background = PaneKit.Brush("SurfaceHover");
            _rows.Children.Add(row);
        }
    }

    /// <summary>
    /// Заголовком служит первая строка. Заметки здесь живут час, и придумывать
    /// им имена — работа, ради которой их бы просто не заводили.
    /// </summary>
    private static string FirstLine(string text)
    {
        var line = text.Split('\n').FirstOrDefault()?.Trim() ?? "";
        return line.Length > 0 ? line : Strings.L("New note");
    }

    /// <summary>
    /// Обновляет только подписи в списке. Полная пересборка на каждое нажатие
    /// клавиши пересоздала бы и поле ввода вместе с фокусом — то есть первая
    /// набранная буква была бы и последней.
    /// </summary>
    private void RefreshRowTitles()
    {
        foreach (var child in _rows.Children)
        {
            if (child is not Border { Child: Grid line }) continue;
            if (line.Children.Count == 0 || line.Children[0] is not TextBlock title) continue;
            if (title.Tag is not string id) continue;
            var note = _store.Notes.FirstOrDefault(n => n.Id == id);
            if (note is not null) title.Text = FirstLine(note.Text);
        }
    }
}
