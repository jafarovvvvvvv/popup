using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using Popup.Services;

namespace Popup.UI.Panes;

/// <summary>Что играет. Порт <c>MediaPane.swift</c>.</summary>
public partial class MediaPane : UserControl
{
    private readonly MediaController _media;

    /// <summary>
    /// Заполнено, пока полоску тащат: в это время она следует за пальцем, а не
    /// за часами.
    /// </summary>
    private double? _scrubbing;

    public MediaPane(MediaController media)
    {
        _media = media;
        InitializeComponent();

        EmptyLabel.Text = Strings.L("Nothing playing");

        _media.PropertyChanged += OnMediaChanged;
        Unloaded += (_, _) => _media.PropertyChanged -= OnMediaChanged;

        PreviousButton.Click += (_, _) => _media.Previous();
        NextButton.Click += (_, _) => _media.Next();
        PlayButton.Click += (_, _) => _media.TogglePlayPause();

        ScrubTrack.MouseEnter += (_, _) => SetScrubHover(true);
        ScrubTrack.MouseLeave += (_, _) => { if (_scrubbing is null) SetScrubHover(false); };
        ScrubTrack.MouseLeftButtonDown += OnScrubDown;
        ScrubTrack.MouseMove += OnScrubMove;
        ScrubTrack.MouseLeftButtonUp += OnScrubUp;
        ScrubTrack.SizeChanged += (_, _) => UpdateScrubber();

        Refresh();
    }

    private void OnMediaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaController.Position)) UpdateScrubber();
        else Refresh();
    }

    private void Refresh()
    {
        var track = _media.Track;
        var playing = track is not null;
        EmptyState.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
        Playing.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        if (track is null) return;

        TitleText.Text = track.Title;
        SubtitleText.Text = Subtitle(track);

        ArtworkImage.Source = _media.Artwork;
        ArtworkPlaceholder.Visibility = _media.Artwork is null ? Visibility.Visible : Visibility.Collapsed;

        PlayGlyph.Data = (System.Windows.Media.Geometry)FindResource(
            _media.IsPlaying ? "IconPause" : "IconPlay");

        UpdateScrubber();
    }

    /// <summary>
    /// Система часто повторяет название трека в поле альбома, и «Исполнитель —
    /// Название» дважды подряд читается как ошибка.
    /// </summary>
    private static string Subtitle(MediaController.TrackInfo track)
    {
        var parts = new List<string> { track.Artist };
        if (track.Album.Length > 0 && track.Album != track.Title) parts.Add(track.Album);
        return string.Join(" — ", parts.Where(p => p.Length > 0));
    }

    // MARK: - Полоска

    private double Progress
    {
        get
        {
            if (_scrubbing is { } value) return value;
            if (_media.Duration <= TimeSpan.Zero) return 0;
            return Math.Clamp(_media.Position / _media.Duration, 0, 1);
        }
    }

    private void UpdateScrubber()
    {
        var width = ScrubTrack.ActualWidth;
        var filled = width * Progress;
        TrackFill.Width = double.IsFinite(filled) ? Math.Max(0, filled) : 0;
        ScrubKnob.Margin = new Thickness(Math.Clamp(filled - 5.5, 0, Math.Max(0, width - 11)), 0, 0, 0);

        ElapsedText.Text = Format.Time(_media.Duration * Progress);
        DurationText.Text = Format.Time(_media.Duration);
    }

    private void SetScrubHover(bool on)
    {
        TrackBase.Height = TrackFill.Height = on ? 6 : 4;
        ScrubKnob.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnScrubDown(object sender, MouseButtonEventArgs e)
    {
        ScrubTrack.CaptureMouse();
        _scrubbing = Fraction(e);
        UpdateScrubber();
    }

    private void OnScrubMove(object sender, MouseEventArgs e)
    {
        if (_scrubbing is null || e.LeftButton != MouseButtonState.Pressed) return;
        _scrubbing = Fraction(e);
        UpdateScrubber();
    }

    private void OnScrubUp(object sender, MouseButtonEventArgs e)
    {
        if (_scrubbing is null) return;
        var target = Fraction(e);
        ScrubTrack.ReleaseMouseCapture();
        // Сначала перемотка, потом сброс: очистить _scrubbing раньше — значит
        // на один кадр вернуть полоску на старое место, прежде чем приедет новое.
        _media.Seek(_media.Duration * target);
        _scrubbing = null;
        if (!ScrubTrack.IsMouseOver) SetScrubHover(false);
        UpdateScrubber();
    }

    private double Fraction(MouseEventArgs e)
    {
        var width = ScrubTrack.ActualWidth;
        if (width <= 0) return 0;
        return Math.Clamp(e.GetPosition(ScrubTrack).X / width, 0, 1);
    }
}
