using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Popup.Model;
// Здесь Path — это фигура, а не путь к файлу.
using Path = System.Windows.Shapes.Path;

namespace Popup.UI;

/// <summary>
/// Колонка иконок, переключающая вкладки. Порт <c>Rail</c> из
/// <c>NotchContentView.swift</c>.
///
/// Наведение переключает вкладку, но только после того, как курсор
/// остановился: мышь, пересекающая рейл по дороге куда-то ещё, уходит за
/// несколько десятков миллисекунд, а пришедшая выбирать — стоит. Тот же порог
/// отделяет «мышь швырнули через верх экрана» от «мышь пришла к панели» в
/// <see cref="Notch.PointerWatcher"/>.
///
/// Собран кодом, а не разметкой: задержка наведения и отмена уже назначенного
/// переключения при переходе на соседнюю иконку — это состояние на каждую
/// кнопку, и в XAML оно было бы длиннее, чем здесь.
/// </summary>
public sealed class Rail : StackPanel
{
    private const double ItemWidth = 30;
    private const double ItemHeight = 24;

    private readonly NotchViewModel _vm;
    private readonly IReadOnlyList<PanelTab> _fixedTabs;
    private readonly DispatcherTimer _dwell = new() { Interval = Theme.RailDwell };
    private readonly Dictionary<PanelTab, Border> _items = [];

    private PanelTab? _hovered;
    private PanelTab? _pending;

    public Rail(NotchViewModel vm, IReadOnlyList<PanelTab> tabs, bool observeChanges = false)
    {
        _vm = vm;
        _fixedTabs = tabs;
        // Полные имена: у StackPanel есть свойства с теми же названиями, что и
        // у перечислений, и короткая запись разрешилась бы в свойство.
        Orientation = System.Windows.Controls.Orientation.Vertical;
        Width = ItemWidth;
        VerticalAlignment = System.Windows.VerticalAlignment.Center;

        _dwell.Tick += (_, _) =>
        {
            _dwell.Stop();
            if (_pending is { } tab && tab != _vm.Tab) _vm.Select(tab);
        };

        Rebuild();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NotchViewModel.Tab) or "") UpdateSelection();
        };

        // Правый рейл достраивается плагинами уже во время работы.
        if (observeChanges && tabs is INotifyCollectionChanged live)
            live.CollectionChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        _items.Clear();

        foreach (var tab in _fixedTabs)
        {
            var icon = new Path
            {
                Data = (Geometry)(Application.Current.TryFindResource(tab.IconKey) ?? Geometry.Empty),
                StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 16,
                Height = 16,
                Stretch = Stretch.None,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };

            var cell = new Border
            {
                Width = ItemWidth,
                Height = ItemHeight,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 2, 0, 2),
                Child = icon,
                Cursor = System.Windows.Input.Cursors.Hand,
                // Растяжение на наведении — трансформ на этапе отрисовки.
                // Увеличивать сам размер значило бы пересчитывать раскладку
                // рейла на каждое движение мыши, а раскладка, которая
                // пересчитывается по курсору, — это ровно то, что видно как
                // подёргивание.
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };

            cell.MouseEnter += (_, _) =>
            {
                _hovered = tab;
                _pending = tab;
                Scale(cell, 1.15);
                UpdateSelection();
                // Переход на соседнюю иконку перезапускает отсчёт, поэтому
                // выигрывает только та, на которой действительно остановились.
                _dwell.Stop();
                if (tab != _vm.Tab) _dwell.Start();
            };
            cell.MouseLeave += (_, _) =>
            {
                if (_hovered == tab) _hovered = null;
                if (_pending == tab) { _pending = null; _dwell.Stop(); }
                Scale(cell, 1.0);
                UpdateSelection();
            };
            cell.MouseLeftButtonUp += (_, _) =>
            {
                _dwell.Stop();
                _vm.Select(tab);
            };

            _items[tab] = cell;
            Children.Add(cell);
        }

        UpdateSelection();
    }

    private static void Scale(Border cell, double to)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = to,
            Duration = Theme.ContentDuration,
        };
        var transform = (ScaleTransform)cell.RenderTransform;
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void UpdateSelection()
    {
        foreach (var (tab, cell) in _items)
        {
            var selected = _vm.Tab == tab;
            cell.Background = selected
                ? (Brush)Application.Current.FindResource("SurfaceHover")
                : _hovered == tab
                    ? (Brush)Application.Current.FindResource("Surface")
                    : Brushes.Transparent;

            if (cell.Child is Path path)
            {
                path.Stroke = selected
                    ? (Brush)Application.Current.FindResource("TextPrimary")
                    : (Brush)Application.Current.FindResource("TextTertiary");
            }
        }
    }
}
