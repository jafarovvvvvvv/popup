using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Popup.UI;

/// <summary>
/// Три пляшущих столбика в углу шапки. Единственное место в панели, где
/// что-то движется само по себе, — и оно же единственный признак, по которому
/// со свёрнутой панели видно, играет музыка или стоит на паузе.
///
/// На паузе анимация останавливается совсем, а не замирает на месте: столбики
/// опускаются до ровной черты, чтобы состояние читалось, а не угадывалось.
/// </summary>
public sealed class EqualizerBars : StackPanel
{
    private static readonly double[] Periods = [0.62, 0.48, 0.75];
    private static readonly double[] Peaks = [9.0, 12.0, 7.0];

    private readonly Border[] _bars = new Border[3];

    public EqualizerBars()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        VerticalAlignment = System.Windows.VerticalAlignment.Center;
        Height = 12;

        for (var i = 0; i < _bars.Length; i++)
        {
            var bar = new Border
            {
                Width = 2,
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = (Brush)Application.Current.FindResource("TextSecondary"),
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0),
            };
            _bars[i] = bar;
            Children.Add(bar);
        }
    }

    private bool _animating;

    public bool IsAnimating
    {
        get => _animating;
        set
        {
            if (_animating == value) return;
            _animating = value;
            Apply();
        }
    }

    private void Apply()
    {
        for (var i = 0; i < _bars.Length; i++)
        {
            if (!_animating)
            {
                _bars[i].BeginAnimation(HeightProperty, null);
                _bars[i].Height = 2;
                continue;
            }

            var animation = new DoubleAnimation
            {
                From = 2,
                To = Peaks[i],
                Duration = new Duration(TimeSpan.FromSeconds(Periods[i])),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            _bars[i].BeginAnimation(HeightProperty, animation);
        }
    }
}
