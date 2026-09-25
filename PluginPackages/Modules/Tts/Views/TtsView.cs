using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Plugin.Tts.Speech;

namespace ClipDesk.Plugin.Tts.Views;

/// <summary>
/// Main responsive WPF view for the ClipDesk Text-to-Speech (TTS) plugin.
/// Fully responsive down to square minimum size 240x240 without horizontal overflow.
/// Adheres to dark/light theme, dynamic accent contrast, and ClipDesk SDK controls.
/// </summary>
public sealed class TtsView : Grid, IDisposable
{
    private readonly WindowsPluginViewContext _context;
    private readonly HybridTtsEngine _engine = new();
    private readonly AudioPlayerControl _audioPlayer;

    private readonly TextBox _inputBox;
    private readonly Button _speakBtn;
    private readonly Button _saveToBoardBtn;
    private readonly Button _copyBtn;
    private readonly Button _clearBtn;
    private readonly Button _previewBtn;
    private readonly TextBlock _statusLabel;
    private readonly Grid _voiceRow;
    private readonly Grid _slidersRow;

    private FrameworkElement? _langDropdown;
    private FrameworkElement? _voiceDropdown;

    private CancellationTokenSource? _activeCts;
    private TtsAudioResult? _lastAudioResult;
    private bool _disposed;

    public TtsView(WindowsPluginViewContext context)
    {
        _context = context;

        // Container margin adaptation
        UpdateMargin();
        SizeChanged += (_, _) => UpdateMargin();
        context.LayoutChanged += UpdateMargin;

        var foreground = GetBrush(context.IsDarkMode ? "#F3F6FA" : "#182230");
        var subtle = GetBrush(context.IsDarkMode ? "#8E9EB5" : "#64748B");
        var surface = GetBrush(context.IsDarkMode ? "#1B222D" : "#F8FAFC");
        var borderBrush = GetBrush(context.IsDarkMode ? "#2E3848" : "#E2E8F0");
        var accent = GetBrush(context.AccentColor, "#2563EB");

        // Row layout definitions:
        // Row 0: Language & Voice selection row + inline Preview button
        // Row 1: Text input box (star height)
        // Row 2: Sliders row (Speed & Volume)
        // Row 3: Embedded audio player
        // Row 4: Actions bar (Falar, Salvar na mesa, Copiar, Limpar)
        // Row 5: Status text label
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 50 }); // 1
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5

        // =====================================================================
        // Row 0: Language & Voice selection + Preview
        // =====================================================================
        _voiceRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        _voiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _voiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        _voiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Inline preview button
        _previewBtn = PluginButtons.Create(context, "🔊", primary: false);
        _previewBtn.Tag = "plugin-interactive";
        _previewBtn.Width = Math.Clamp(32 * context.Scale, 28, 44);
        _previewBtn.Height = Math.Clamp(32 * context.Scale, 28, 44);
        _previewBtn.Padding = new Thickness(0);
        _previewBtn.Margin = new Thickness(4, 0, 0, 0);
        _previewBtn.ToolTip = "Ouvir prévia desta voz";
        AutomationProperties.SetName(_previewBtn, "Ouvir prévia desta voz");
        _previewBtn.Content = new TextBlock
        {
            Text = "\uE767", // Speaker icon
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(14 * context.Scale, 12, 22),
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _previewBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await PlayVoicePreviewAsync();
        };

        SetColumn(_previewBtn, 2);
        _voiceRow.Children.Add(_previewBtn);

        SetRow(_voiceRow, 0);
        Children.Add(_voiceRow);

        RebuildDropdowns();

        // =====================================================================
        // Row 1: Text Input Box
        // =====================================================================
        _inputBox = new TextBox
        {
            Text = context.State.GetText(),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8, 6, 8, 6),
            Foreground = foreground,
            Background = surface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            FontSize = Math.Clamp(14 * context.Scale, 12, 24),
            Tag = "plugin-interactive"
        };
        AutomationProperties.SetName(_inputBox, "Texto para sintetizar");
        _inputBox.TextChanged += async (_, _) =>
        {
            var text = _inputBox.Text;
            var currentLang = _context.State.GetLanguage();
            if (currentLang.Equals("auto", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(text))
            {
                var detected = LanguageDetector.DetectLanguage(text);
                await _context.ExecuteAsync(new PluginCommand("set-text", new Dictionary<string, string>
                {
                    ["text"] = text,
                    ["language"] = detected
                }), rebuild: false);
                RebuildDropdowns();
            }
            else
            {
                await _context.ExecuteAsync(new PluginCommand("set-text", new Dictionary<string, string>
                {
                    ["text"] = text
                }), rebuild: false);
            }
            UpdateActionStates();
        };

        SetRow(_inputBox, 1);
        Children.Add(_inputBox);

        // =====================================================================
        // Row 3: Embedded Audio Player
        // =====================================================================
        _audioPlayer = new AudioPlayerControl(context);
        SetRow(_audioPlayer, 3);
        Children.Add(_audioPlayer);

        // Check if there was existing audio in state
        var existingAudio = context.State.GetAudioFileName();
        if (!string.IsNullOrWhiteSpace(existingAudio))
        {
            var defaultDir = PluginStoragePaths.GetDefaultDirectory("clipdesk.tts");
            var existingPath = Path.Combine(defaultDir, existingAudio);
            if (File.Exists(existingPath))
            {
                _audioPlayer.LoadAudio(existingPath);
                _audioPlayer.Visibility = Visibility.Visible;
            }
        }

        // =====================================================================
        // Row 2: Sliders Row (Rate & Volume)
        // =====================================================================
        _slidersRow = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        _slidersRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _slidersRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Rate slider (-10 to +10)
        var ratePanel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var rateLabel = new TextBlock
        {
            Text = $"Velocidade: {context.State.GetRate():+0;-0;0}",
            FontSize = Math.Clamp(11 * context.Scale, 9, 16),
            Foreground = subtle,
            Margin = new Thickness(0, 0, 0, 2)
        };
        var rateSlider = PluginSliders.Create(context, -10, 10, context.State.GetRate(), async val =>
        {
            int r = (int)Math.Round(val);
            rateLabel.Text = $"Velocidade: {r:+0;-0;0}";
            await context.ExecuteAsync(new PluginCommand("set-rate", new Dictionary<string, string> { ["rate"] = r.ToString() }), rebuild: false);
        }, step: 1);
        rateSlider.InteractionStarted += context.NotifyBeforeChange;
        ratePanel.Children.Add(rateLabel);
        ratePanel.Children.Add(rateSlider);
        SetColumn(ratePanel, 0);
        _slidersRow.Children.Add(ratePanel);

        // Volume slider (0 to 100)
        var volPanel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        var volLabel = new TextBlock
        {
            Text = $"Volume: {context.State.GetVolume()}%",
            FontSize = Math.Clamp(11 * context.Scale, 9, 16),
            Foreground = subtle,
            Margin = new Thickness(0, 0, 0, 2)
        };
        var volSlider = PluginSliders.Create(context, 0, 100, context.State.GetVolume(), async val =>
        {
            int v = (int)Math.Round(val);
            volLabel.Text = $"Volume: {v}%";
            _audioPlayer.SetVolume(v);
            await context.ExecuteAsync(new PluginCommand("set-volume", new Dictionary<string, string> { ["volume"] = v.ToString() }), rebuild: false);
        }, step: 1);
        volSlider.InteractionStarted += context.NotifyBeforeChange;
        volPanel.Children.Add(volLabel);
        volPanel.Children.Add(volSlider);
        SetColumn(volPanel, 1);
        _slidersRow.Children.Add(volPanel);

        SetRow(_slidersRow, 2);
        Children.Add(_slidersRow);

        // =====================================================================
        // Row 4: Action Buttons Bar
        // =====================================================================
        var actionsBar = new Grid { Margin = new Thickness(0, 4, 0, 2) };
        actionsBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionsBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionsBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionsBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Primary Speak button ("Falar")
        _speakBtn = PluginButtons.Create(context, "▶  Falar", primary: true);
        _speakBtn.Tag = "plugin-interactive";
        _speakBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        _speakBtn.Margin = new Thickness(0, 0, 4, 0);
        _speakBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await SynthesizeSpeechAsync();
        };
        SetColumn(_speakBtn, 0);
        actionsBar.Children.Add(_speakBtn);

        // Save to Board button ("Salvar na mesa")
        _saveToBoardBtn = PluginButtons.Create(context, "Mesa", primary: false);
        _saveToBoardBtn.Tag = "plugin-interactive";
        _saveToBoardBtn.Margin = new Thickness(2, 0, 2, 0);
        _saveToBoardBtn.ToolTip = "Salvar áudio na mesa";
        AutomationProperties.SetName(_saveToBoardBtn, "Salvar áudio na mesa");
        _saveToBoardBtn.Content = new TextBlock
        {
            Text = "\uE7C3", // Save/Pin to board
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(14 * context.Scale, 12, 22),
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _saveToBoardBtn.Width = Math.Clamp(34 * context.Scale, 30, 48);
        _saveToBoardBtn.Click += (_, e) =>
        {
            e.Handled = true;
            SaveAudioToBoard();
        };
        SetColumn(_saveToBoardBtn, 1);
        actionsBar.Children.Add(_saveToBoardBtn);

        // Persistent Copy button ("Copiar texto")
        _copyBtn = PluginButtons.Create(context, "Copiar", primary: false);
        _copyBtn.Tag = "plugin-interactive";
        _copyBtn.Margin = new Thickness(2, 0, 2, 0);
        _copyBtn.ToolTip = "Copiar texto";
        AutomationProperties.SetName(_copyBtn, "Copiar texto");
        _copyBtn.Content = new TextBlock
        {
            Text = "\uE8C8", // Copy icon
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(14 * context.Scale, 12, 22),
            Foreground = foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _copyBtn.Width = Math.Clamp(34 * context.Scale, 30, 48);
        _copyBtn.Click += (_, e) =>
        {
            e.Handled = true;
            CopyTextToClipboard();
        };
        SetColumn(_copyBtn, 2);
        actionsBar.Children.Add(_copyBtn);

        // Clear button ("Limpar")
        _clearBtn = PluginButtons.Create(context, "Limpar", primary: false);
        _clearBtn.Tag = "plugin-interactive";
        _clearBtn.Margin = new Thickness(2, 0, 0, 0);
        _clearBtn.ToolTip = "Limpar texto e áudio";
        AutomationProperties.SetName(_clearBtn, "Limpar");
        _clearBtn.Content = new TextBlock
        {
            Text = "\uE74D", // Delete/Clear trash icon
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(13 * context.Scale, 11, 20),
            Foreground = subtle,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _clearBtn.Width = Math.Clamp(34 * context.Scale, 30, 48);
        _clearBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await ClearContentAsync();
        };
        SetColumn(_clearBtn, 3);
        actionsBar.Children.Add(_clearBtn);

        SetRow(actionsBar, 4);
        Children.Add(actionsBar);

        // =====================================================================
        // Row 5: Status Label
        // =====================================================================
        _statusLabel = new TextBlock
        {
            Text = "Pronto",
            FontSize = Math.Clamp(11 * context.Scale, 9, 14),
            Foreground = subtle,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 2, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        SetRow(_statusLabel, 5);
        Children.Add(_statusLabel);

        UpdateActionStates();

        Unloaded += (_, _) => Dispose();
    }

    private void UpdateMargin()
    {
        Margin = _context.Width < 260 || ActualWidth is > 0 and < 260
            ? new Thickness(6, 5, 6, 5)
            : new Thickness(10, 8, 10, 8);
    }

    private void RebuildDropdowns()
    {
        // 1. Language Dropdown
        if (_langDropdown is not null)
        {
            _voiceRow.Children.Remove(_langDropdown);
        }

        var currentLang = _context.State.GetLanguage();
        List<PluginDropdownOption> langOptions =
        [
            new("auto", "Auto", "detectar automatico"),
            new("pt", "PT", "portugues"),
            new("en", "EN", "ingles english"),
            new("es", "ES", "espanhol spanish")
        ];

        _langDropdown = PluginDropdowns.Create(
            _context,
            langOptions,
            currentLang,
            async newLang =>
            {
                var nextVoice = VoiceCatalog.GetDefaultVoiceId(newLang);
                await _context.ExecuteAsync(new PluginCommand("set-voice", new Dictionary<string, string>
                {
                    ["voice_id"] = nextVoice,
                    ["language"] = newLang
                }), rebuild: false);
                RebuildDropdowns();
            },
            searchPlaceholder: "Idioma…");
        _langDropdown.Tag = "plugin-interactive";
        _langDropdown.Margin = new Thickness(0, 0, 4, 0);
        SetColumn(_langDropdown, 0);
        _voiceRow.Children.Add(_langDropdown);

        // 2. Voice Dropdown
        if (_voiceDropdown is not null)
        {
            _voiceRow.Children.Remove(_voiceDropdown);
        }

        var effectiveLang = currentLang.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? LanguageDetector.DetectLanguage(_context.State.GetText())
            : currentLang;

        var availableVoices = _engine.GetAllVoices(effectiveLang);
        var voiceOptions = availableVoices.Select(v => new PluginDropdownOption(
            v.Id,
            v.DisplayName,
            $"{v.Language} {v.DisplayName}")).ToList();

        var currentVoiceId = _context.State.GetVoiceId();
        if (!voiceOptions.Any(o => o.Value.Equals(currentVoiceId, StringComparison.OrdinalIgnoreCase)) && voiceOptions.Count > 0)
        {
            currentVoiceId = voiceOptions[0].Value;
        }

        _voiceDropdown = PluginDropdowns.Create(
            _context,
            voiceOptions,
            currentVoiceId,
            async newVoice =>
            {
                await _context.ExecuteAsync(new PluginCommand("set-voice", new Dictionary<string, string>
                {
                    ["voice_id"] = newVoice
                }), rebuild: false);
            },
            searchPlaceholder: "Pesquisar voz…");
        _voiceDropdown.Tag = "plugin-interactive";
        _voiceDropdown.Margin = new Thickness(0, 0, 4, 0);
        SetColumn(_voiceDropdown, 1);
        _voiceRow.Children.Add(_voiceDropdown);
    }

    private void UpdateActionStates()
    {
        var text = _context.State.GetText();
        var audioFile = _context.State.GetAudioFileName();

        _speakBtn.IsEnabled = !string.IsNullOrWhiteSpace(text);
        _saveToBoardBtn.IsEnabled = !string.IsNullOrWhiteSpace(audioFile) || _lastAudioResult is not null;
        _copyBtn.IsEnabled = !string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(_context.Module.GetClipboardText(_context.State));
        _clearBtn.IsEnabled = !string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(audioFile);
    }

    private async Task SynthesizeSpeechAsync()
    {
        var text = _inputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        _speakBtn.IsEnabled = false;
        _statusLabel.Text = "Sintetizando voz…";

        try
        {
            _context.NotifyBeforeChange();

            var lang = _context.State.GetLanguage();
            if (lang.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                lang = LanguageDetector.DetectLanguage(text);
            }

            var voiceId = _context.State.GetVoiceId();
            var rate = _context.State.GetRate();
            var volume = _context.State.GetVolume();
            bool hasNetwork = _context.Execution.HasPermission("network");

            var result = await _engine.SynthesizeAsync(text, voiceId, lang, rate, volume, hasNetwork, token);
            _lastAudioResult = result;

            // Commit to state via generate-speech
            await _context.ExecuteAsync(new PluginCommand("generate-speech", new Dictionary<string, string>
            {
                ["text"] = text,
                ["audio_file_name"] = result.FileName
            }), rebuild: false);

            _audioPlayer.LoadAudio(result.FilePath, autoPlay: true);
            _audioPlayer.Visibility = Visibility.Visible;

            _statusLabel.Text = result.IsFallback
                ? "Concluído (Síntese local Windows SAPI)"
                : "Concluído (Voz neural online)";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Síntese cancelada.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Erro na síntese: {ex.Message}";
        }
        finally
        {
            _speakBtn.IsEnabled = true;
            UpdateActionStates();
        }
    }

    private async Task PlayVoicePreviewAsync()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        _previewBtn.IsEnabled = false;
        _statusLabel.Text = "Carregando prévia da voz…";

        try
        {
            var lang = _context.State.GetLanguage();
            if (lang.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                lang = LanguageDetector.DetectLanguage(_context.State.GetText());
            }

            var voiceId = _context.State.GetVoiceId();
            var rate = _context.State.GetRate();
            var volume = _context.State.GetVolume();
            bool hasNetwork = _context.Execution.HasPermission("network");

            var previewResult = await _engine.SynthesizePreviewAsync(voiceId, lang, rate, volume, hasNetwork, token);
            _audioPlayer.LoadAudio(previewResult.FilePath, autoPlay: true);
            _audioPlayer.Visibility = Visibility.Visible;
            _statusLabel.Text = "Reproduzindo prévia da voz";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Prévia cancelada.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Erro na prévia: {ex.Message}";
        }
        finally
        {
            _previewBtn.IsEnabled = true;
        }
    }

    private void SaveAudioToBoard()
    {
        string? targetPath = _lastAudioResult?.FilePath;
        string? targetName = _lastAudioResult?.FileName;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var audioFile = _context.State.GetAudioFileName();
            if (!string.IsNullOrWhiteSpace(audioFile))
            {
                var dir = PluginStoragePaths.GetDefaultDirectory("clipdesk.tts");
                targetPath = Path.Combine(dir, audioFile);
                targetName = audioFile;
            }
        }

        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            _context.RequestHostAction(new PluginHostAction(PluginHostActionKind.ShowMessage, "Nenhum arquivo de áudio disponível para salvar na mesa."));
            return;
        }

        var boardFile = PluginBoardFile.FromPath(targetPath, targetName);
        var request = new PluginBoardFileRequest(new[] { boardFile });
        _context.RequestHostAction(PluginHostAction.AddFiles(request));

        _statusLabel.Text = "Áudio adicionado como cartão na mesa!";
    }

    private void CopyTextToClipboard()
    {
        var clipboardText = _context.Module.GetClipboardText(_context.State);
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            clipboardText = _inputBox.Text.Trim();
        }

        if (!string.IsNullOrWhiteSpace(clipboardText))
        {
            _context.RequestHostAction(new PluginHostAction(PluginHostActionKind.CopyToClipboard, clipboardText));
            _statusLabel.Text = "Texto copiado para a área de transferência!";
        }
    }

    private async Task ClearContentAsync()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = null;

        _audioPlayer.Reset();
        _inputBox.Text = "";
        _lastAudioResult = null;

        await _context.ExecuteAsync(new PluginCommand("clear"), rebuild: false);
        _statusLabel.Text = "Limpo";
        UpdateActionStates();
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
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = null;
            _audioPlayer.Dispose();
            _engine.Dispose();
        }
    }
}
