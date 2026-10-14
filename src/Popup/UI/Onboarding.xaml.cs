using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Popup.Services;
using Path = System.Windows.Shapes.Path;

namespace Popup.UI;

/// <summary>
/// Знакомство при первом запуске.
///
/// Нужно из-за одной особенности: панель невидима, пока к ней не подвести
/// курсор. Приложение, которое после установки не показывает ничего, выглядит
/// не запустившимся — и это единственное, что человек не может выяснить сам.
///
/// Поэтому карусель короткая и про главное. Всё остальное — какие есть вкладки,
/// что умеет полка — находится наведением мыши за полминуты, и пересказывать
/// это заранее значит держать человека в приветствии вместо работы.
/// </summary>
public partial class Onboarding : Window
{
    private sealed record Page(string Title, string Body, Func<UIElement> Draw);

    private readonly Page[] _pages;
    private int _index;

    public Onboarding()
    {
        InitializeComponent();

        _pages =
        [
            new Page(
                Strings.L("The panel lives at the top edge"),
                Strings.L("You cannot see it until you bring the pointer to it. Move the mouse to the middle of the top edge and pause — the panel unfolds. Move away and it folds back. If your hands are on the keyboard, there is Ctrl+Alt+Space."),
                DrawHover),
            new Page(
                Strings.L("Eight tabs along the sides"),
                Strings.L("Music, file shelf, clipboard history, snippets, calendar, translation, notes and the dev tab. Tabs switch on hover — no clicking needed. A file can simply be dropped on the top of the screen."),
                DrawTabs),
            new Page(
                Strings.L("You can see what is going on"),
                Strings.L("While a recording, a build or a download is running, a narrow strip with a timer sits at the top. The panel does not unfold and does not cover your work. The LocalWhisper pill moves up here on its own."),
                DrawActivity),
            new Page(
                Strings.L("It is built out to fit you"),
                Strings.L("The plugins folder gives you a tab of your own from a program in any language. The pipe lets any script show a strip, drop a file on the shelf or put text in the clipboard. All of it is in docs."),
                DrawExtend),
        ];

        SkipButton.Content = PaneKit.Text(Strings.L("Skip"), 11, "TextTertiary");
        SkipButton.Click += (_, _) => Close();
        NextButton.Click += (_, _) => Advance();

        // Знакомство закрывают, а не изучают: Esc и Enter должны работать
        // раньше, чем человек найдёт кнопку.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.Enter or Key.Space or Key.Right) Advance();
        };

        BuildDots();
        Show(0, animate: false);
    }

    private void Advance()
    {
        if (_index + 1 >= _pages.Length) Close();
        else Show(_index + 1, animate: true);
    }

    private void BuildDots()
    {
        for (var i = 0; i < _pages.Length; i++)
        {
            Dots.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(4, 0, 4, 0),
                Fill = PaneKit.Brush("TextTertiary"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            });
        }
    }

    private void Show(int index, bool animate)
    {
        _index = index;
        var page = _pages[index];

        TitleText.Text = page.Title;
        BodyText.Text = page.Body;
        Illustration.Content = page.Draw();

        for (var i = 0; i < Dots.Children.Count; i++)
        {
            ((Ellipse)Dots.Children[i]).Fill =
                PaneKit.Brush(i == index ? "TextPrimary" : "TextTertiary");
        }

        NextButton.Content = PaneKit.Text(
            index + 1 >= _pages.Length ? Strings.L("Got it") : Strings.L("Next"), 11, "TextSecondary");
        SkipButton.Visibility = index + 1 >= _pages.Length ? Visibility.Hidden : Visibility.Visible;

        if (!animate) return;
        // Страница приезжает справа — так видно, что их несколько и что это
        // одна лента, а не четыре разных окна.
        var slide = new ThicknessAnimation
        {
            From = new Thickness(28, 0, -28, 0),
            To = new Thickness(0),
            Duration = new Duration(TimeSpan.FromSeconds(0.22)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Illustration.BeginAnimation(MarginProperty, slide);
        TitleText.BeginAnimation(MarginProperty, slide);
        Illustration.BeginAnimation(OpacityProperty, Theme.Fade(1, Theme.PaneInDuration));
    }

    // MARK: - Картинки
    //
    // Рисуются кодом, а не лежат файлами: их четыре, они из тех же фигур, что
    // и сама панель, и картинка, которую нельзя поправить в редакторе кода,
    // устареет первой же сменой силуэта.

    /// <summary>Экран, полоска у верхнего края и курсор, идущий к ней.</summary>
    private static UIElement DrawHover()
    {
        var canvas = new Canvas { Width = 260, Height = 120 };

        canvas.Children.Add(Screen(out var screen));
        Canvas.SetLeft(screen, 0);
        Canvas.SetTop(screen, 0);

        var strip = new Notch.NotchShape
        {
            Fill = Brushes.White,
            TopRadius = 4,
            BottomRadius = 6,
            Width = 74,
            Height = 7,
        };
        Canvas.SetLeft(strip, 93);
        Canvas.SetTop(strip, 6);
        canvas.Children.Add(strip);

        var cursor = MouseArrow();
        Canvas.SetLeft(cursor, 128);
        Canvas.SetTop(cursor, 62);
        canvas.Children.Add(cursor);

        // Курсор едет вверх и обратно: одно движение объясняет больше, чем
        // абзац текста под ним.
        var travel = new DoubleAnimation
        {
            From = 62,
            To = 18,
            Duration = new Duration(TimeSpan.FromSeconds(1.1)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        cursor.BeginAnimation(Canvas.TopProperty, travel);

        return canvas;
    }

    /// <summary>Раскрытая панель с рейлами по бокам.</summary>
    private static UIElement DrawTabs()
    {
        var canvas = new Canvas { Width = 260, Height = 120 };

        var body = new Notch.NotchShape
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            TopRadius = 8,
            BottomRadius = 14,
            Width = 220,
            Height = 96,
        };
        Canvas.SetLeft(body, 20);
        Canvas.SetTop(body, 6);
        canvas.Children.Add(body);

        for (var i = 0; i < 6; i++)
        {
            var pip = new Border
            {
                Width = 16,
                Height = 9,
                CornerRadius = new CornerRadius(3),
                Background = PaneKit.Brush(i == 0 ? "TextPrimary" : "SurfaceHover"),
            };
            Canvas.SetLeft(pip, 34);
            Canvas.SetTop(pip, 24 + i * 12);
            canvas.Children.Add(pip);
        }

        for (var i = 0; i < 2; i++)
        {
            var pip = new Border
            {
                Width = 16,
                Height = 9,
                CornerRadius = new CornerRadius(3),
                Background = PaneKit.Brush("SurfaceHover"),
            };
            Canvas.SetLeft(pip, 208);
            Canvas.SetTop(pip, 42 + i * 12);
            canvas.Children.Add(pip);
        }

        for (var i = 0; i < 3; i++)
        {
            var line = new Border
            {
                Width = 128 - i * 26,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = PaneKit.Brush("Surface"),
            };
            Canvas.SetLeft(line, 62);
            Canvas.SetTop(line, 32 + i * 18);
            canvas.Children.Add(line);
        }

        return canvas;
    }

    /// <summary>Полоска живой активности с бегущими точками.</summary>
    private static UIElement DrawActivity()
    {
        var canvas = new Canvas { Width = 260, Height = 120 };

        canvas.Children.Add(Screen(out var screen));
        Canvas.SetLeft(screen, 0);
        Canvas.SetTop(screen, 0);

        var strip = new Notch.NotchShape
        {
            Fill = Brushes.White,
            TopRadius = 5,
            BottomRadius = 9,
            Width = 104,
            Height = 20,
        };
        Canvas.SetLeft(strip, 78);
        Canvas.SetTop(strip, 6);
        canvas.Children.Add(strip);

        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = new SolidColorBrush(Color.FromRgb(0xD6, 0x34, 0x34)),
        };
        Canvas.SetLeft(dot, 88);
        Canvas.SetTop(dot, 12);
        canvas.Children.Add(dot);

        for (var i = 0; i < 6; i++)
        {
            var bar = new Border
            {
                Width = 3,
                Height = 4,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            };
            Canvas.SetLeft(bar, 104 + i * 7);
            Canvas.SetTop(bar, 14);
            canvas.Children.Add(bar);

            bar.BeginAnimation(HeightProperty, new DoubleAnimation
            {
                From = 3,
                To = 10,
                Duration = new Duration(TimeSpan.FromSeconds(0.45)),
                BeginTime = TimeSpan.FromSeconds(0.075 * i),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }

        return canvas;
    }

    /// <summary>Плагин и канал: две стрелки, ведущие в панель.</summary>
    private static UIElement DrawExtend()
    {
        var canvas = new Canvas { Width = 260, Height = 120 };

        var body = new Notch.NotchShape
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            TopRadius = 7,
            BottomRadius = 12,
            Width = 150,
            Height = 40,
        };
        Canvas.SetLeft(body, 55);
        Canvas.SetTop(body, 6);
        canvas.Children.Add(body);

        foreach (var (x, label) in new[] { (24.0, "py"), (196.0, "{ }") })
        {
            var chip = new Border
            {
                Width = 40,
                Height = 26,
                CornerRadius = new CornerRadius(7),
                Background = PaneKit.Brush("Surface"),
                BorderThickness = new Thickness(1),
                BorderBrush = PaneKit.Brush("Hairline"),
                Child = PaneKit.Text(label, 11, "TextSecondary"),
            };
            ((TextBlock)chip.Child).HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            Canvas.SetLeft(chip, x - 20);
            Canvas.SetTop(chip, 84);
            canvas.Children.Add(chip);

            var arrow = new Path
            {
                Data = Geometry.Parse(x < 130
                    ? "M0,26 C0,10 34,16 46,4"
                    : "M46,26 C46,10 12,16 0,4"),
                Stroke = PaneKit.Brush("TextTertiary"),
                StrokeThickness = 1.4,
                StrokeDashArray = [3, 3],
            };
            Canvas.SetLeft(arrow, x < 130 ? 26 : 168);
            Canvas.SetTop(arrow, 54);
            canvas.Children.Add(arrow);
        }

        return canvas;
    }

    /// <summary>Условный экран — рамка, в которой всё происходит.</summary>
    private static UIElement Screen(out Border border)
    {
        border = new Border
        {
            Width = 260,
            Height = 108,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)),
            BorderThickness = new Thickness(1),
            BorderBrush = PaneKit.Brush("Hairline"),
        };
        return border;
    }

    private static Path MouseArrow() => new()
    {
        Data = Geometry.Parse("M0,0 L0,13 L3.4,10 L5.6,15 L8,14 L5.8,9.2 L10,9 Z"),
        Fill = Brushes.White,
        Stroke = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
        StrokeThickness = 1,
    };
}
