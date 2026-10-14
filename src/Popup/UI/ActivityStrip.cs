using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Popup.Services;
// Здесь Path — это фигура, а не путь к файлу.
using Path = System.Windows.Shapes.Path;

namespace Popup.UI;

/// <summary>
/// Полоска живой активности.
///
/// Визуальный язык взят у пилюли LocalWhisper, которую она заменяет: красная
/// точка слева, бегущая волна в середине, моноширинный таймер справа. Это
/// сделано нарочно — человек уже научился читать эту картинку, и менять её при
/// переезде наверх экрана значило бы отбирать выученное без всякой выгоды.
///
/// Живёт в двух местах: телом панели, когда та свёрнута, и чипом в шапке, когда
/// раскрыта. Разница ровно одна — в шапке у неё есть кнопки, а на свёрнутой
/// полоске их нет: свёрнутая панель пропускает клики насквозь, и кнопка на ней
/// была бы обманом.
/// </summary>
public sealed class ActivityStrip : Grid
{
    private const int WaveDots = 8;

    private readonly ActivityStore _store;
    private readonly bool _interactive;

    private readonly Ellipse _indicator;
    private readonly Grid _wave;
    private readonly Ellipse[] _dots = new Ellipse[WaveDots];
    private readonly Path _check;
    private readonly TextBlock _label;
    private readonly StackPanel _actions;
    private Storyboard? _running;

    public ActivityStrip(ActivityStore store, bool interactive)
    {
        _store = store;
        _interactive = interactive;

        VerticalAlignment = System.Windows.VerticalAlignment.Center;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _indicator = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.FromRgb(0xD6, 0x34, 0x34)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0),
        };
        SetColumn(_indicator, 0);
        Children.Add(_indicator);

        // Волна и галка занимают одну и ту же ячейку: они никогда не видны
        // вместе, а общее место избавляет от прыжка раскладки при переходе от
        // «идёт» к «готово».
        _wave = new Grid { VerticalAlignment = System.Windows.VerticalAlignment.Center, Height = 12 };
        _wave.ColumnDefinitions.Add(new ColumnDefinition());
        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        for (var i = 0; i < WaveDots; i++)
        {
            var dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0),
            };
            _dots[i] = dot;
            row.Children.Add(dot);
        }
        _wave.Children.Add(row);

        _check = new Path
        {
            Data = Geometry.Parse("M0,5 L4,9 L11,1"),
            Stroke = Brushes.White,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _wave.Children.Add(_check);

        SetColumn(_wave, 1);
        Children.Add(_wave);

        _label = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        SetColumn(_label, 2);
        Children.Add(_label);

        _actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        SetColumn(_actions, 3);
        Children.Add(_actions);

        _store.PropertyChanged += OnChanged;
        Unloaded += (_, _) =>
        {
            _store.PropertyChanged -= OnChanged;
            StopWave();
        };

        Refresh();
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_store.Current is not { } item)
        {
            // Погасить целиком, а не просто остановить волну: чип в шапке живёт
            // дольше полоски — панель может быть раскрыта, когда работа
            // закончилась, — и оставшаяся на месте галка врала бы о том, что
            // что-то ещё идёт.
            StopWave();
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        _label.Text = item.Label;
        _label.Visibility = item.Label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var (indicator, dots) = item.Tone switch
        {
            ActivityStore.Tone.Live => (Color.FromRgb(0xD6, 0x34, 0x34), Colors.White),
            ActivityStore.Tone.Busy => (Color.FromRgb(0x64, 0x64, 0x64), Color.FromRgb(0xC8, 0xC8, 0xC8)),
            ActivityStore.Tone.Ok => (Color.FromRgb(0x34, 0xC7, 0x59), Colors.White),
            _ => (Color.FromRgb(0xD6, 0x34, 0x34), Colors.White),
        };
        _indicator.Fill = new SolidColorBrush(indicator);
        foreach (var dot in _dots) dot.Fill = new SolidColorBrush(dots);
        _check.Stroke = new SolidColorBrush(dots);

        var finished = item.Tone is ActivityStore.Tone.Ok or ActivityStore.Tone.Fail;
        _check.Visibility = finished ? Visibility.Visible : Visibility.Collapsed;
        foreach (var dot in _dots) dot.Visibility = finished ? Visibility.Collapsed : Visibility.Visible;

        if (finished) StopWave();
        else StartWave(item.Tone);

        RefreshActions(item);
    }

    private void RefreshActions(ActivityStore.Item item)
    {
        _actions.Children.Clear();
        if (!_interactive) return;

        foreach (var action in item.Actions)
        {
            var id = action.Id;
            var button = PaneKit.IconButton(id == "stop" ? "IconClose" : "IconLink",
                () => _store.Invoke(id), 20);
            button.ToolTip = action.Label;
            _actions.Children.Add(button);
        }
    }

    // MARK: - Волна

    /// <summary>
    /// Бегущая волна.
    ///
    /// Восемь точек с общей раскадровкой и разным смещением по времени: это то
    /// же самое, что синус со сдвигом фазы у пилюли, только считает его
    /// композитор, а не таймер на каждом кадре. Панель при этом не будит
    /// раскладку — анимируется только высота, и та отрисовкой.
    /// </summary>
    private void StartWave(ActivityStore.Tone tone)
    {
        StopWave();

        var period = tone == ActivityStore.Tone.Live ? 0.9 : 1.6;
        var peak = tone == ActivityStore.Tone.Live ? 10.0 : 7.0;

        var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        for (var i = 0; i < _dots.Length; i++)
        {
            var animation = new DoubleAnimation
            {
                From = 4,
                To = peak,
                Duration = new Duration(TimeSpan.FromSeconds(period / 2)),
                AutoReverse = true,
                // Смещение по времени вместо смещения фазы: волна выходит той
                // же, а считать синус вручную не надо.
                BeginTime = TimeSpan.FromSeconds(period / 2 * i / _dots.Length),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Storyboard.SetTarget(animation, _dots[i]);
            Storyboard.SetTargetProperty(animation, new PropertyPath(HeightProperty));
            storyboard.Children.Add(animation);
        }

        _running = storyboard;
        storyboard.Begin();
    }

    private void StopWave()
    {
        if (_running is null) return;
        _running.Stop();
        _running = null;
        foreach (var dot in _dots)
        {
            dot.BeginAnimation(HeightProperty, null);
            dot.Height = 4;
        }
    }
}
