using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Tts.Views;

/// <summary>
/// Embedded WPF audio player control supporting MP3 and WAV playback.
/// Includes Play/Pause, seekable timeline slider, mm:ss time display, and volume control.
/// </summary>
public sealed class AudioPlayerControl : Grid, IDisposable
{
    private readonly WindowsPluginViewContext _context;
    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private readonly Button _playPauseBtn;
    private readonly TextBlock _playPauseIcon;
    private readonly Slider _timelineSlider;
    private readonly TextBlock _timeText;
    private readonly Slider _volumeSlider;

    private bool _isPlaying;
    private bool _isUserSeeking;
    private string? _currentFilePath;
    private bool _disposed;

    public string? CurrentFilePath => _currentFilePath;
    public bool IsPlaying => _isPlaying;

    public AudioPlayerControl(WindowsPluginViewContext context)
    {
        _context = context;

        Margin = new Thickness(0, 4, 0, 4);

        // Layout rows/columns
        // Row 0: Play/Pause button | Timeline slider | Time label
        // Row 1: Volume icon + slider (compact)
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var foreground = GetBrush(context.IsDarkMode ? "#F3F6FA" : "#182230");
        var subtle = GetBrush(context.IsDarkMode ? "#8E9EB5" : "#64748B");
        var accent = GetBrush(context.AccentColor, "#2563EB");

        // Play/Pause button
        _playPauseBtn = PluginButtons.Create(context, "▶", primary: false);
        _playPauseBtn.Tag = "plugin-interactive";
        _playPauseBtn.Width = Math.Clamp(34 * context.Scale, 28, 48);
        _playPauseBtn.Height = Math.Clamp(34 * context.Scale, 28, 48);
        _playPauseBtn.Padding = new Thickness(0);
        _playPauseBtn.VerticalAlignment = VerticalAlignment.Center;
        _playPauseBtn.Margin = new Thickness(0, 0, 8, 0);

        _playPauseIcon = new TextBlock
        {
            Text = "\uE768", // Play
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(15 * context.Scale, 13, 24),
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _playPauseBtn.Content = _playPauseIcon;
        _playPauseBtn.ToolTip = "Reproduzir áudio";
        AutomationProperties.SetName(_playPauseBtn, "Reproduzir áudio");
        _playPauseBtn.Click += (_, e) =>
        {
            e.Handled = true;
            TogglePlayPause();
        };

        SetColumn(_playPauseBtn, 0);
        Children.Add(_playPauseBtn);

        // Center panel: Timeline Slider
        var centerPanel = new Grid { VerticalAlignment = VerticalAlignment.Center };
        centerPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        centerPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Right label: Time display (mm:ss / mm:ss)
        _timeText = new TextBlock
        {
            Text = "00:00 / 00:00",
            FontSize = Math.Clamp(11 * context.Scale, 9, 16),
            Foreground = subtle,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        SetColumn(_timeText, 2);
        Children.Add(_timeText);

        _timelineSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "plugin-interactive",
            Foreground = accent
        };
        AutomationProperties.SetName(_timelineSlider, "Posição do áudio");

        _timelineSlider.PreviewMouseDown += (_, _) => _isUserSeeking = true;
        _timelineSlider.PreviewMouseUp += (_, _) =>
        {
            _isUserSeeking = false;
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                var target = TimeSpan.FromSeconds(_timelineSlider.Value);
                _mediaPlayer.Position = target;
            }
        };
        _timelineSlider.ValueChanged += (_, e) =>
        {
            if (_isUserSeeking && _mediaPlayer.NaturalDuration.HasTimeSpan && _timeText is not null)
            {
                var target = TimeSpan.FromSeconds(e.NewValue);
                _timeText.Text = FormatTime(target, _mediaPlayer.NaturalDuration.TimeSpan);
            }
        };

        SetRow(_timelineSlider, 0);
        centerPanel.Children.Add(_timelineSlider);

        // Volume row inside center panel
        var volumePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var volIcon = new TextBlock
        {
            Text = "\uE767", // Volume
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(12 * context.Scale, 10, 18),
            Foreground = subtle,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        volumePanel.Children.Add(volIcon);

        _volumeSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            Width = Math.Clamp(70 * context.Scale, 55, 120),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "plugin-interactive"
        };
        AutomationProperties.SetName(_volumeSlider, "Volume do player");
        _volumeSlider.ValueChanged += (_, e) =>
        {
            _mediaPlayer.Volume = Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        };
        volumePanel.Children.Add(_volumeSlider);

        SetRow(volumePanel, 1);
        centerPanel.Children.Add(volumePanel);

        SetColumn(centerPanel, 1);
        Children.Add(centerPanel);

        // MediaPlayer & Timer event hooks
        _timer.Tick += OnTimerTick;

        _mediaPlayer.MediaOpened += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    _timelineSlider.Maximum = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                    _timeText.Text = FormatTime(TimeSpan.Zero, _mediaPlayer.NaturalDuration.TimeSpan);
                }
            });
        };

        _mediaPlayer.MediaEnded += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _timer.Stop();
                _playPauseIcon.Text = "\uE768"; // Play
                _timelineSlider.Value = 0;
                if (_mediaPlayer.NaturalDuration.HasTimeSpan)
                {
                    _timeText.Text = FormatTime(TimeSpan.Zero, _mediaPlayer.NaturalDuration.TimeSpan);
                }
            });
        };

        _mediaPlayer.MediaFailed += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _timer.Stop();
                _playPauseIcon.Text = "\uE768";
            });
        };

        Unloaded += (_, _) => Dispose();
    }

    /// <summary>
    /// Loads a local audio file (.mp3 or .wav) into the player.
    /// </summary>
    public void LoadAudio(string? filePath, bool autoPlay = false)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            Reset();
            return;
        }

        _currentFilePath = filePath;
        _mediaPlayer.Open(new Uri(filePath, UriKind.Absolute));
        _mediaPlayer.Volume = Math.Clamp(_volumeSlider.Value / 100.0, 0.0, 1.0);
        _timelineSlider.Value = 0;
        _timeText.Text = "00:00 / 00:00";

        if (autoPlay)
        {
            Play();
        }
        else
        {
            Pause();
        }
    }

    /// <summary>
    /// Starts playback.
    /// </summary>
    public void Play()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;

        _mediaPlayer.Play();
        _isPlaying = true;
        _playPauseIcon.Text = "\uE769"; // Pause
        _playPauseBtn.ToolTip = "Pausar áudio";
        AutomationProperties.SetName(_playPauseBtn, "Pausar áudio");
        _timer.Start();
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public void Pause()
    {
        _mediaPlayer.Pause();
        _isPlaying = false;
        _playPauseIcon.Text = "\uE768"; // Play
        _playPauseBtn.ToolTip = "Reproduzir áudio";
        AutomationProperties.SetName(_playPauseBtn, "Reproduzir áudio");
        _timer.Stop();
    }

    /// <summary>
    /// Stops playback and resets timeline.
    /// </summary>
    public void Stop()
    {
        _mediaPlayer.Stop();
        _isPlaying = false;
        _playPauseIcon.Text = "\uE768"; // Play
        _playPauseBtn.ToolTip = "Reproduzir áudio";
        AutomationProperties.SetName(_playPauseBtn, "Reproduzir áudio");
        _timer.Stop();
        _timelineSlider.Value = 0;
    }

    /// <summary>
    /// Resets player state and detaches file.
    /// </summary>
    public void Reset()
    {
        Stop();
        _mediaPlayer.Close();
        _currentFilePath = null;
        _timelineSlider.Value = 0;
        _timeText.Text = "00:00 / 00:00";
    }

    /// <summary>
    /// Sets the volume (0 to 100).
    /// </summary>
    public void SetVolume(int volume)
    {
        _volumeSlider.Value = Math.Clamp(volume, 0, 100);
        _mediaPlayer.Volume = Math.Clamp(volume / 100.0, 0.0, 1.0);
    }

    private void TogglePlayPause()
    {
        if (_isPlaying)
            Pause();
        else
            Play();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isUserSeeking && _mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            var current = _mediaPlayer.Position;
            var total = _mediaPlayer.NaturalDuration.TimeSpan;
            _timelineSlider.Value = current.TotalSeconds;
            _timeText.Text = FormatTime(current, total);
        }
    }

    private static string FormatTime(TimeSpan elapsed, TimeSpan total)
    {
        return $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2} / {total.Minutes:D2}:{total.Seconds:D2}";
    }

    private static Brush GetBrush(string color, string fallback = "#000000")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _timer.Stop();
            _mediaPlayer.Stop();
            _mediaPlayer.Close();
        }
    }
}
