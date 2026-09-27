using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.AudioRecorder;

public sealed class AudioRecorderPlugin : IWindowsPluginRenderer
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily MonoFont = new("Segoe UI Variable Text, Consolas, Segoe UI");

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        // ----------------------------------------------------
        // Paleta de cores responsiva e elegante
        // ----------------------------------------------------
        bool dark = context.IsDarkMode;
        var foreground = GetBrush(dark ? "#F8FAFC" : "#0F172A");
        var muted = GetBrush(dark ? "#94A3B8" : "#64748B");
        var borderBrush = GetBrush(dark ? "#334155" : "#E2E8F0");
        var cardSurface = GetBrush(dark ? "#162032" : "#FFFFFF");
        var toggleBg = GetBrush(dark ? "#141D2E" : "#F1F5F9");
        var sliderTrack = GetBrush(dark ? "#26334D" : "#E2E8F0");
        var recordRed = GetBrush("#EF4444");
        var warningAmber = GetBrush("#F59E0B");
        var successGreen = GetBrush("#10B981");

        var accent = GetBrush(context.AccentColor, "#EF4444");
        var accentForeground = GetContrastBrush(context.AccentColor);

        // Gradientes para waveform encorpado
        var recGradient = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString("#FF6B6B"),
            (Color)ColorConverter.ConvertFromString("#EF4444"),
            new Point(0.5, 0),
            new Point(0.5, 1));

        var idleGradient = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString("#38BDF8"),
            (Color)ColorConverter.ConvertFromString("#6366F1"),
            new Point(0.5, 0),
            new Point(0.5, 1));

        var previewGradient = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString("#34D399"),
            (Color)ColorConverter.ConvertFromString("#059669"),
            new Point(0.5, 0),
            new Point(0.5, 1));

        var audioService = new AudioCaptureService();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var waveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30 fps
        int secondsRecorded = 0;
        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        var captureSession = Guid.NewGuid().ToString("N");
        bool disposed = false;
        bool restored = false;

        // Container raiz
        var root = new Grid
        {
            Margin = new Thickness(8 * context.Scale),
            Background = Brushes.Transparent
        };

        // =========================================================================
        // COLUNA 0: PAINEL DE FONTES DE ÁUDIO COM TOGGLE M/S EM CIMA DE CADA ENTRADA
        // =========================================================================
        var sourcesGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        sourcesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Microfone
        sourcesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8 * context.Scale) });      // Espaço entre fontes
        sourcesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Som do Sistema

        bool micActive = string.Equals(context.State.GetString("micEnabled"), "true", StringComparison.OrdinalIgnoreCase);
        bool sysActive = string.Equals(context.State.GetString("systemEnabled"), "true", StringComparison.OrdinalIgnoreCase);
        int micVol = context.State.GetInt32("micVolume", 100);
        int sysVol = context.State.GetInt32("systemVolume", 100);

        bool micIsStereo = string.Equals(context.State.GetString("micChannelMode"), "stereo", StringComparison.OrdinalIgnoreCase);
        bool sysIsStereo = !string.Equals(context.State.GetString("sysChannelMode"), "mono", StringComparison.OrdinalIgnoreCase);

        Action? updateMasterFormatText = null;

        // --- SUBCOLUNA 0: MICROFONE ---
        var micCol = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        micCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 0: Toggle M / S
        micCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4 * context.Scale) });
        micCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 2: Ícone Grande
        micCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3 * context.Scale) });
        micCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 4: Texto Pequeno + %
        micCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3 * context.Scale) });
        micCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 6: Slider Moderno

        var micToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("mono", "M", ToolTip: "Gravar entrada em Mono"),
            new PluginToggleOption("stereo", "S", ToolTip: "Gravar entrada em Estéreo")
        }, micIsStereo ? "stereo" : "mono", async mode =>
        {
            if (audioService.IsRecording) return;
            micIsStereo = mode == "stereo";
            updateMasterFormatText?.Invoke();
            await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string> { ["micChannelMode"] = mode }), rebuild: false);
        });

        var micBtn = CreateLargeIconButton("\ue720", micActive, accent, accentForeground, cardSurface, foreground, borderBrush, context.Scale);
        micBtn.ToolTip = "Ativar / Silenciar Microfone";

        var micLabelPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var micNameText = new TextBlock
        {
            Text = "Microfone",
            FontFamily = TextFont,
            FontSize = Math.Clamp(10 * context.Scale, 9, 13),
            FontWeight = FontWeights.Medium,
            Foreground = foreground,
            Tag = "plugin-font:10",
            VerticalAlignment = VerticalAlignment.Center
        };
        var micVolText = new TextBlock
        {
            Text = $" {micVol}%",
            FontFamily = MonoFont,
            FontSize = Math.Clamp(9.5 * context.Scale, 8.5, 12),
            FontWeight = FontWeights.SemiBold,
            Foreground = muted,
            Tag = "plugin-font:9.5",
            VerticalAlignment = VerticalAlignment.Center
        };
        micLabelPanel.Children.Add(micNameText);
        micLabelPanel.Children.Add(micVolText);

        var micSlider = PluginSliders.Create(context, 0, 100, micVol, newVal =>
        {
            micVol = (int)newVal;
            micVolText.Text = $" {micVol}%";
        }, step: 1);
        micSlider.IsEnabled = micActive;
        micSlider.InteractionCompleted += async () =>
        {
            await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string> { ["micVolume"] = micVol.ToString(CultureInfo.InvariantCulture) }), rebuild: false);
        };

        Grid.SetRow(micToggle, 0);
        Grid.SetRow(micBtn, 2);
        Grid.SetRow(micLabelPanel, 4);
        Grid.SetRow(micSlider, 6);
        micCol.Children.Add(micToggle);
        micCol.Children.Add(micBtn);
        micCol.Children.Add(micLabelPanel);
        micCol.Children.Add(micSlider);

        Grid.SetColumn(micCol, 0);
        sourcesGrid.Children.Add(micCol);

        // --- SUBCOLUNA 2: SOM DO SISTEMA ---
        var sysCol = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        sysCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 0: Toggle M / S
        sysCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4 * context.Scale) });
        sysCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 2: Ícone Grande
        sysCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3 * context.Scale) });
        sysCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 4: Texto Pequeno + %
        sysCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3 * context.Scale) });
        sysCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Linha 6: Slider Moderno

        var sysToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("mono", "M", ToolTip: "Gravar entrada em Mono"),
            new PluginToggleOption("stereo", "S", ToolTip: "Gravar entrada em Estéreo")
        }, sysIsStereo ? "stereo" : "mono", async mode =>
        {
            if (audioService.IsRecording) return;
            sysIsStereo = mode == "stereo";
            updateMasterFormatText?.Invoke();
            await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string> { ["sysChannelMode"] = mode }), rebuild: false);
        });

        var sysBtn = CreateLargeIconButton("\ue7f4", sysActive, accent, accentForeground, cardSurface, foreground, borderBrush, context.Scale);
        sysBtn.ToolTip = "Ativar / Silenciar Áudio do PC";

        var sysLabelPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var sysNameText = new TextBlock
        {
            Text = "Sistema",
            FontFamily = TextFont,
            FontSize = Math.Clamp(10 * context.Scale, 9, 13),
            FontWeight = FontWeights.Medium,
            Foreground = foreground,
            Tag = "plugin-font:10",
            VerticalAlignment = VerticalAlignment.Center
        };
        var sysVolText = new TextBlock
        {
            Text = $" {sysVol}%",
            FontFamily = MonoFont,
            FontSize = Math.Clamp(9.5 * context.Scale, 8.5, 12),
            FontWeight = FontWeights.SemiBold,
            Foreground = muted,
            Tag = "plugin-font:9.5",
            VerticalAlignment = VerticalAlignment.Center
        };
        sysLabelPanel.Children.Add(sysNameText);
        sysLabelPanel.Children.Add(sysVolText);

        var sysSlider = PluginSliders.Create(context, 0, 100, sysVol, newVal =>
        {
            sysVol = (int)newVal;
            sysVolText.Text = $" {sysVol}%";
        }, step: 1);
        sysSlider.IsEnabled = sysActive;
        sysSlider.InteractionCompleted += async () =>
        {
            await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string> { ["systemVolume"] = sysVol.ToString(CultureInfo.InvariantCulture) }), rebuild: false);
        };

        Grid.SetRow(sysToggle, 0);
        Grid.SetRow(sysBtn, 2);
        Grid.SetRow(sysLabelPanel, 4);
        Grid.SetRow(sysSlider, 6);
        sysCol.Children.Add(sysToggle);
        sysCol.Children.Add(sysBtn);
        sysCol.Children.Add(sysLabelPanel);
        sysCol.Children.Add(sysSlider);

        Grid.SetColumn(sysCol, 2);
        sourcesGrid.Children.Add(sysCol);

        Grid.SetColumn(sourcesGrid, 0);
        root.Children.Add(sourcesGrid);

        // =========================================================================
        // DIVISOR 1
        // =========================================================================
        var div1 = CreateDivider(borderBrush, context.Scale);
        Grid.SetColumn(div1, 1);
        root.Children.Add(div1);

        // =========================================================================
        // COLUNA 2: TRANSPORTE (GRAVAR, PAUSAR, PARAR) & MODO PREVIEW
        // =========================================================================
        var transportContainer = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // 1. Grid de Transporte Principal (Ordem tradicional: Gravar, Pausar, Parar)
        var transportGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        transportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Gravar
        transportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6 * context.Scale) });
        transportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Pausar
        transportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6 * context.Scale) });
        transportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Parar

        var recordBtn = CreateTransportControl("Gravar", "\ue7c8", recordRed, Brushes.White, cardSurface, foreground, null, context.Scale, isFilled: true);
        var pauseBtn = CreateTransportControl("Pausar", "\ue769", accent, accentForeground, cardSurface, foreground, borderBrush, context.Scale);
        var stopBtn = CreateTransportControl("Parar", "\ue71a", accent, accentForeground, cardSurface, foreground, borderBrush, context.Scale);

        pauseBtn.IsEnabled = false;
        pauseBtn.Opacity = 0.45;
        stopBtn.IsEnabled = false;
        stopBtn.Opacity = 0.45;

        Grid.SetColumn(recordBtn, 0);
        Grid.SetColumn(pauseBtn, 2);
        Grid.SetColumn(stopBtn, 4);
        transportGrid.Children.Add(recordBtn);
        transportGrid.Children.Add(pauseBtn);
        transportGrid.Children.Add(stopBtn);
        transportContainer.Children.Add(transportGrid);

        // 2. Grid de Prévia & Confirmação (Ouvir, Descartar, Confirmar)
        var previewGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Ouvir
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6 * context.Scale) });
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Descartar
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6 * context.Scale) });
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Confirmar

        var playPreviewBtn = CreateTransportControl("Ouvir", "\ue768", accent, accentForeground, cardSurface, foreground, borderBrush, context.Scale);
        var discardBtn = CreateTransportControl("Descartar", "\ue711", accent, accentForeground, cardSurface, muted, borderBrush, context.Scale);
        var confirmBtn = CreateTransportControl("Confirmar", "\ue73e", accent, accentForeground, cardSurface, foreground, null, context.Scale, isFilled: true);

        Grid.SetColumn(playPreviewBtn, 0);
        Grid.SetColumn(discardBtn, 2);
        Grid.SetColumn(confirmBtn, 4);
        previewGrid.Children.Add(playPreviewBtn);
        previewGrid.Children.Add(discardBtn);
        previewGrid.Children.Add(confirmBtn);
        transportContainer.Children.Add(previewGrid);

        Grid.SetColumn(transportContainer, 2);
        root.Children.Add(transportContainer);

        // =========================================================================
        // DIVISOR 2
        // =========================================================================
        var div2 = CreateDivider(borderBrush, context.Scale);
        Grid.SetColumn(div2, 3);
        root.Children.Add(div2);

        // =========================================================================
        // COLUNA 4: "QUADRADINHO" OLED (MONITOR COM WAVEFORM FFT REAL E CRONÔMETRO)
        // =========================================================================
        const int BarCount = 16;
        var monitorCard = new Border
        {
            Background = GetBrush(dark ? "#090E1A" : "#F8FAFC"),
            BorderBrush = GetBrush(dark ? "#1E293B" : "#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12 * context.Scale),
            Padding = new Thickness(8 * context.Scale, 7 * context.Scale, 8 * context.Scale, 7 * context.Scale),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var monitorGrid = new Grid();
        monitorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header com Status / Timer
        monitorGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Waveform Bars

        // Cabeçalho do Monitor
        var monitorHeader = new Grid();
        monitorHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        monitorHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        monitorHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusPill = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        var statusDot = new Ellipse
        {
            Width = 7 * context.Scale,
            Height = 7 * context.Scale,
            Fill = muted,
            Margin = new Thickness(0, 0, 5 * context.Scale, 0)
        };

        var statusLabel = new TextBlock
        {
            Text = "PRONTO",
            FontFamily = MonoFont,
            FontSize = Math.Clamp(9.5 * context.Scale, 8.5, 12),
            FontWeight = FontWeights.SemiBold,
            Foreground = muted,
            Tag = "plugin-font:9.5",
            VerticalAlignment = VerticalAlignment.Center
        };

        statusPill.Children.Add(statusDot);
        statusPill.Children.Add(statusLabel);
        Grid.SetColumn(statusPill, 0);
        monitorHeader.Children.Add(statusPill);

        var formatLabel = new TextBlock
        {
            Text = "44.1 kHz",
            FontFamily = MonoFont,
            FontSize = Math.Clamp(8.5 * context.Scale, 7.5, 11),
            Foreground = muted,
            Opacity = 0.8,
            Tag = "plugin-font:8.5",
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(formatLabel, 1);
        monitorHeader.Children.Add(formatLabel);

        var copyIconBtn = PluginButtons.Create(context, "Copiar");
        copyIconBtn.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(10 * context.Scale, 9, 14)
        };
        copyIconBtn.MinWidth = 20 * context.Scale;
        copyIconBtn.MinHeight = 20 * context.Scale;
        copyIconBtn.Padding = new Thickness(2 * context.Scale, 0, 2 * context.Scale, 0);
        copyIconBtn.ToolTip = "Copiar caminho do áudio gravado";
        AutomationProperties.SetName(copyIconBtn, "Copiar caminho do áudio gravado");
        string initialLastFile = context.State.GetString("audioFileName") ?? string.Empty;
        copyIconBtn.IsEnabled = !string.IsNullOrWhiteSpace(initialLastFile);
        copyIconBtn.Margin = new Thickness(4 * context.Scale, 0, 0, 0);
        copyIconBtn.Click += (_, e) =>
        {
            e.Handled = true;
            var clipboardText = context.Module.GetClipboardText(context.State);
            if (!string.IsNullOrWhiteSpace(clipboardText))
            {
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, clipboardText));
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Caminho do áudio copiado!"));
            }
        };
        Grid.SetColumn(copyIconBtn, 2);
        monitorHeader.Children.Add(copyIconBtn);

        Grid.SetRow(monitorHeader, 0);
        monitorGrid.Children.Add(monitorHeader);

        // Painel do Waveform Encorpado com FFT Real
        var waveBarsPanel = new UniformGrid
        {
            Rows = 1,
            Columns = BarCount,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4 * context.Scale, 0, 0)
        };

        var waveBars = new Border[BarCount];
        double barWidth = Math.Clamp(5.5 * context.Scale, 4.2, 7.5);
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Border
            {
                Background = idleGradient,
                CornerRadius = new CornerRadius(2.5 * context.Scale),
                Width = barWidth,
                Height = 3 * context.Scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.4
            };
            waveBars[i] = bar;
            waveBarsPanel.Children.Add(bar);
        }

        Grid.SetRow(waveBarsPanel, 1);
        monitorGrid.Children.Add(waveBarsPanel);

        monitorCard.Child = monitorGrid;
        Grid.SetColumn(monitorCard, 4);
        root.Children.Add(monitorCard);

        // Responsividade adaptativa para tamanhos mínimos e proporções quadradas (160x160 a 540x180)
        bool isCompactLayout = false;

        void ApplyLayout(bool compact)
        {
            isCompactLayout = compact;

            root.ColumnDefinitions.Clear();
            root.RowDefinitions.Clear();

            micSlider.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            sysSlider.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            if (compact)
            {
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Monitor
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Transporte
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Fontes

                div1.Visibility = Visibility.Collapsed;
                div2.Visibility = Visibility.Collapsed;

                Grid.SetRow(monitorCard, 0);
                Grid.SetColumn(monitorCard, 0);

                Grid.SetRow(transportContainer, 1);
                Grid.SetColumn(transportContainer, 0);

                Grid.SetRow(sourcesGrid, 2);
                Grid.SetColumn(sourcesGrid, 0);

                monitorCard.Margin = new Thickness(0, 0, 0, 3 * context.Scale);
                transportContainer.Margin = new Thickness(0, 2 * context.Scale, 0, 2 * context.Scale);
                sourcesGrid.Margin = new Thickness(0, 3 * context.Scale, 0, 0);
            }
            else
            {
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.75, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

                div1.Visibility = Visibility.Visible;
                div2.Visibility = Visibility.Visible;

                Grid.SetRow(sourcesGrid, 0);
                Grid.SetColumn(sourcesGrid, 0);

                Grid.SetRow(div1, 0);
                Grid.SetColumn(div1, 1);

                Grid.SetRow(transportContainer, 0);
                Grid.SetColumn(transportContainer, 2);

                Grid.SetRow(div2, 0);
                Grid.SetColumn(div2, 3);

                Grid.SetRow(monitorCard, 0);
                Grid.SetColumn(monitorCard, 4);

                monitorCard.Margin = new Thickness(0);
                transportContainer.Margin = new Thickness(0);
                sourcesGrid.Margin = new Thickness(0);
            }
        }

        void CheckLayout()
        {
            var w = root.ActualWidth;
            if (w <= 0) w = context.Width;
            bool compact = w < 420;
            if (compact != isCompactLayout || root.ColumnDefinitions.Count == 0 && root.RowDefinitions.Count == 0)
            {
                ApplyLayout(compact);
            }

            var isVerySmall = w < 240 || (root.ActualHeight > 0 && root.ActualHeight < 240);
            root.Margin = new Thickness(isVerySmall ? 4 * context.Scale : 8 * context.Scale);
        }

        ApplyLayout(false);
        root.SizeChanged += (_, _) => CheckLayout();
        context.LayoutChanged += CheckLayout;

        // Animação de pulso no indicador de gravação
        var pulseAnim = new DoubleAnimation(1.0, 0.25, new Duration(TimeSpan.FromMilliseconds(500)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        // =========================================================================
        // INTERAÇÃO E EVENTOS
        // =========================================================================
        void UpdateMasterFormatText()
        {
            bool masterIsStereo = (micActive && micIsStereo) || (sysActive && sysIsStereo);
            if (context.SharedAssets is null)
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Atualize o ClipDesk para compartilhar gravações deste plugin."));
                return;
            }
            if (!micActive && !sysActive) masterIsStereo = micIsStereo || sysIsStereo;
            formatLabel.Text = masterIsStereo ? "44.1 kHz • ST" : "44.1 kHz • MN";
        }
        updateMasterFormatText = UpdateMasterFormatText;
        UpdateMasterFormatText();

        // Alternância do Microfone
        micBtn.Click += (_, e) =>
        {
            e.Handled = true;
            micActive = !micActive;
            UpdateIconButtonVisual(micBtn, micActive, accent, accentForeground, cardSurface, foreground, borderBrush);
            micSlider.IsEnabled = micActive;
            micNameText.Foreground = micActive ? foreground : muted;
            updateMasterFormatText?.Invoke();
            _ = context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string> { ["micEnabled"] = micActive ? "true" : "false" }), rebuild: false);
        };

        // Alternância do Som do Sistema
        sysBtn.Click += (_, e) =>
        {
            e.Handled = true;
            sysActive = !sysActive;
            UpdateIconButtonVisual(sysBtn, sysActive, accent, accentForeground, cardSurface, foreground, borderBrush);
            sysSlider.IsEnabled = sysActive;
            sysNameText.Foreground = sysActive ? foreground : muted;
            updateMasterFormatText?.Invoke();
            _ = context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string> { ["systemEnabled"] = sysActive ? "true" : "false" }), rebuild: false);
        };

        async Task PublishCaptureStateAsync(string status)
        {
            if (disposed) return;
            await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string>
            {
                ["status"] = status,
                ["duration"] = secondsRecorded.ToString(CultureInfo.InvariantCulture),
                ["captureSession"] = captureSession,
                ["captureUpdatedUtc"] = DateTimeOffset.UtcNow.ToString("O")
            }), rebuild: false, cancellationToken: token, recordUndo: false);
        }

        // Timer de Gravação
        timer.Tick += async (_, _) =>
        {
            secondsRecorded++;
            int m = secondsRecorded / 60;
            int s = secondsRecorded % 60;
            statusLabel.Text = $"REC {m:D2}:{s:D2}";
            if (secondsRecorded % 5 == 0) await PublishCaptureStateAsync("recording");
        };

        // Timer do Waveform FFT Real (30 fps)
        double maxWaveHeight = Math.Max(26, 36 * context.Scale);
        waveTimer.Tick += (_, _) =>
        {
            float[] bars = audioService.GetLiveWaveform(BarCount);
            for (int i = 0; i < BarCount; i++)
            {
                double targetHeight = Math.Clamp(bars[i] * maxWaveHeight, 3 * context.Scale, maxWaveHeight);
                var bar = waveBars[i];
                bar.Height = targetHeight;

                if (audioService.IsPlaying)
                {
                    bar.Background = previewGradient;
                    bar.Opacity = Math.Clamp(0.35 + bars[i] * 0.65, 0.35, 1.0);
                }
                else if (audioService.IsRecording)
                {
                    bar.Background = recGradient;
                    bar.Opacity = audioService.IsPaused ? 0.35 : Math.Clamp(0.35 + bars[i] * 0.65, 0.35, 1.0);
                }
                else
                {
                    bar.Background = idleGradient;
                    bar.Opacity = 0.35;
                }
            }
        };
        waveTimer.Start();

        // --- GRAVAR ---
        recordBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!micActive && !sysActive)
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Ative o Microfone ou o Áudio do Sistema para gravar."));
                return;
            }

            if (!audioService.IsRecording)
            {
                bool started = audioService.StartRecording(micActive, sysActive);
                if (!started)
                {
                    context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Não foi possível iniciar a gravação dos dispositivos selecionados."));
                    return;
                }

                secondsRecorded = 0;
                await PublishCaptureStateAsync("recording");
                timer.Start();

                statusDot.Fill = recordRed;
                statusDot.BeginAnimation(UIElement.OpacityProperty, pulseAnim);
                statusLabel.Text = "REC 00:00";
                statusLabel.Foreground = recordRed;

                recordBtn.IsEnabled = false;
                recordBtn.Opacity = 0.5;

                pauseBtn.IsEnabled = true;
                pauseBtn.Opacity = 1.0;
                UpdateControlLabel(pauseBtn, "Pausar", "\ue769");

                stopBtn.IsEnabled = true;
                stopBtn.Opacity = 1.0;

                micToggle.IsEnabled = false;
                sysToggle.IsEnabled = false;

                micSlider.IsEnabled = false;
                sysSlider.IsEnabled = false;
                micBtn.IsEnabled = false;
                sysBtn.IsEnabled = false;
            }
        };

        // --- PAUSAR / RETOMAR ---
        pauseBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!audioService.IsRecording) return;

            if (audioService.IsPaused)
            {
                audioService.ResumeRecording();
                await PublishCaptureStateAsync("recording");
                timer.Start();
                UpdateControlLabel(pauseBtn, "Pausar", "\ue769");
                statusDot.Fill = recordRed;
                statusDot.BeginAnimation(UIElement.OpacityProperty, pulseAnim);
                statusLabel.Text = $"REC {secondsRecorded / 60:D2}:{secondsRecorded % 60:D2}";
                statusLabel.Foreground = recordRed;
            }
            else
            {
                audioService.PauseRecording();
                await PublishCaptureStateAsync("paused");
                timer.Stop();
                UpdateControlLabel(pauseBtn, "Retomar", "\ue768");
                statusDot.BeginAnimation(UIElement.OpacityProperty, null);
                statusDot.Fill = warningAmber;
                statusDot.Opacity = 1.0;
                statusLabel.Text = $"PAUSA {secondsRecorded / 60:D2}:{secondsRecorded % 60:D2}";
                statusLabel.Foreground = warningAmber;
            }
        };

        // --- PARAR (Transição para modo Preview) ---
        stopBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            timer.Stop();
            statusDot.BeginAnimation(UIElement.OpacityProperty, null);

            float mVol = micActive ? (micVol / 100f) : 0f;
            float sVol = sysActive ? (sysVol / 100f) : 0f;
            string? resultFile = audioService.StopRecording(mVol, sVol, micIsStereo, sysIsStereo);

            if (string.IsNullOrEmpty(resultFile) || secondsRecorded == 0)
            {
                ResetToIdle("Nenhum áudio");
                return;
            }

            try
            {
                var name = "Gravacao_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".wav";
                var asset = await context.SharedAssets!.ImportAsync(WindowsPluginFile.FromPath(resultFile, name), token);
                await root.Dispatcher.InvokeAsync(async () =>
                {
                    if (disposed) return;
                    await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string>
                    {
                        ["status"] = "preview", ["duration"] = secondsRecorded.ToString(CultureInfo.InvariantCulture),
                        ["audioAssetId"] = asset.Id, ["audioFileName"] = asset.Name,
                        ["captureSession"] = "", ["captureUpdatedUtc"] = DateTimeOffset.UtcNow.ToString("O")
                    }), rebuild: false, cancellationToken: token);
                    copyIconBtn.IsEnabled = true;
                }).Task.Unwrap();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                await root.Dispatcher.InvokeAsync(() => context.RequestHostAction(new(PluginHostActionKind.ShowMessage,
                    "O áudio permanece neste computador, mas não foi possível compartilhá-lo: " + ex.Message)));
            }
            if (disposed) return;
            statusDot.Fill = successGreen;
            statusDot.Opacity = 1.0;
            statusLabel.Text = $"PREVIEW {secondsRecorded / 60:D2}:{secondsRecorded % 60:D2}";
            statusLabel.Foreground = successGreen;

            transportGrid.Visibility = Visibility.Collapsed;
            previewGrid.Visibility = Visibility.Visible;
        };

        // --- OUVIR PRÉVIA ---
        playPreviewBtn.Click += (_, e) =>
        {
            e.Handled = true;
            if (audioService.IsPlaying)
            {
                audioService.PausePreview();
                UpdateControlLabel(playPreviewBtn, "Ouvir", "\ue768");
            }
            else
            {
                audioService.PlayPreview(() =>
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        UpdateControlLabel(playPreviewBtn, "Ouvir", "\ue768");
                    });
                });
                UpdateControlLabel(playPreviewBtn, "Pausar", "\ue769");
            }
        };

        // --- DESCARTAR ---
        discardBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            audioService.Discard();
            await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string>
                { ["status"] = "idle", ["audioAssetId"] = "", ["audioFileName"] = "", ["captureSession"] = "" }), rebuild: false);
            ResetToIdle("Descartado");
        };

        // --- CONFIRMAR (Adiciona à mesa com API oficial V2) ---
        confirmBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            try
            {
                if (context.SharedAssets is null) throw new InvalidOperationException("Atualize o host para compartilhar áudio.");
                string savedPath = audioService.CommitToDocumentsFolder();
                var assetId = context.State.GetString("audioAssetId") ?? "";
                var assetName = context.State.GetString("audioFileName") ?? "";
                if (!Guid.TryParse(assetId, out var parsedAssetId))
                {
                    var asset = await context.SharedAssets.ImportAsync(WindowsPluginFile.FromPath(savedPath), token);
                    assetId = asset.Id;
                    assetName = asset.Name;
                }

                // 1. Atualizar estado persistido
                await context.ExecuteAsync(new PluginCommand("set-state", new Dictionary<string, string>
                {
                    ["status"] = "idle",
                    ["duration"] = secondsRecorded.ToString(CultureInfo.InvariantCulture),
                    ["audioAssetId"] = assetId,
                    ["audioFileName"] = assetName,
                    ["captureSession"] = "",
                    ["micChannelMode"] = micIsStereo ? "stereo" : "mono",
                    ["sysChannelMode"] = sysIsStereo ? "stereo" : "mono"
                }), rebuild: false);

                // 2. Adicionar arquivo à mesa usando o contrato oficial V2 do host
                var boardFile = WindowsPluginFiles.FromPath(savedPath);
                context.RequestHostAction(PluginHostAction.AddFiles(new PluginBoardFileRequest(new[] { boardFile })));

                // 3. Notificação nativa
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Áudio adicionado à mesa com sucesso!"));

                ResetToIdle("PRONTO");
            }
            catch (Exception ex)
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, $"Erro ao salvar áudio: {ex.Message}"));
            }
        };

        void ResetToIdle(string status)
        {
            timer.Stop();
            audioService.Discard();
            secondsRecorded = 0;
            statusDot.BeginAnimation(UIElement.OpacityProperty, null);
            statusDot.Fill = muted;
            statusDot.Opacity = 0.7;
            statusLabel.Text = status;
            statusLabel.Foreground = muted;

            recordBtn.IsEnabled = true;
            recordBtn.Opacity = 1.0;
            pauseBtn.IsEnabled = false;
            pauseBtn.Opacity = 0.45;
            UpdateControlLabel(pauseBtn, "Pausar", "\ue769");
            stopBtn.IsEnabled = false;
            stopBtn.Opacity = 0.45;

            micToggle.IsEnabled = true;
            sysToggle.IsEnabled = true;

            micSlider.IsEnabled = micActive;
            sysSlider.IsEnabled = sysActive;
            micBtn.IsEnabled = true;
            sysBtn.IsEnabled = true;

            UpdateControlLabel(playPreviewBtn, "Ouvir", "\ue768");
            previewGrid.Visibility = Visibility.Collapsed;
            transportGrid.Visibility = Visibility.Visible;
            UpdateMasterFormatText();
            copyIconBtn.IsEnabled = !string.IsNullOrWhiteSpace(context.State.GetString("audioFileName"));
        }

        root.Loaded += async (_, _) =>
        {
            if (restored || disposed) return;
            restored = true;
            try
            {
                var path = await WindowsSharedAssetFiles.MaterializeAsync(context, context.State.GetString("audioAssetId"), token);
                await root.Dispatcher.InvokeAsync(() =>
                {
                    if (disposed) return;
                    if (path is not null)
                    {
                        audioService.LoadSharedPreview(path);
                        secondsRecorded = context.State.GetInt32("duration", 0);
                        transportGrid.Visibility = Visibility.Collapsed;
                        previewGrid.Visibility = Visibility.Visible;
                        statusLabel.Text = "ÁUDIO COMPARTILHADO";
                        copyIconBtn.IsEnabled = true;
                    }
                    var remoteStatus = context.State.GetString("status");
                    if (remoteStatus is "recording" or "paused")
                        statusLabel.Text = remoteStatus == "recording" ? "GRAVAÇÃO EM OUTRO CLIENTE" : "GRAVAÇÃO PAUSADA EM OUTRO CLIENTE";
                });
            }
            catch (OperationCanceledException) { }
            catch
            {
                await root.Dispatcher.InvokeAsync(() => { if (!disposed) statusLabel.Text = "ÁUDIO COMPARTILHADO INDISPONÍVEL"; });
            }
        };

        // Limpeza de recursos
        root.Unloaded += (_, _) =>
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            timer.Stop();
            waveTimer.Stop();
            context.LayoutChanged -= CheckLayout;
            // A remote renderer replacement must not silently erase an active local recording.
            if (audioService.IsRecording)
            {
                try
                {
                    var draft = audioService.StopRecording(micActive ? micVol / 100f : 0f, sysActive ? sysVol / 100f : 0f, micIsStereo, sysIsStereo);
                    if (draft is not null)
                    {
                        var saved = audioService.CommitToDocumentsFolder();
                        context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "A gravação interrompida foi preservada em: " + saved));
                    }
                }
                catch { }
            }
            audioService.Dispose();
            lifetime.Dispose();
        };

        return root;
    }

    #region Helpers de Criação de Controles

    /// <summary>
    /// Botão de ícone grande (superior) com bordas suaves para fontes de áudio
    /// </summary>
    private static Button CreateLargeIconButton(
        string glyph,
        bool active,
        Brush accent,
        Brush accentForeground,
        Brush surface,
        Brush foreground,
        Brush? border,
        double scale)
    {
        double btnSize = Math.Max(46, 50 * scale);
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = Math.Clamp(23 * scale, 20, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var btn = new Button
        {
            Content = icon,
            Tag = "plugin-interactive",
            Cursor = Cursors.Hand,
            Width = btnSize,
            Height = btnSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(border != null ? 1 : 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };

        AttachHoverScale(btn);
        UpdateIconButtonVisual(btn, active, accent, accentForeground, surface, foreground, border);
        return btn;
    }

    private static void UpdateIconButtonVisual(
        Button btn,
        bool active,
        Brush accent,
        Brush accentForeground,
        Brush surface,
        Brush foreground,
        Brush? border)
    {
        btn.Background = active ? accent : surface;
        btn.BorderBrush = active ? accent : (border ?? Brushes.Transparent);

        if (btn.Content is TextBlock icon)
        {
            icon.Foreground = active ? accentForeground : foreground;
        }
    }

    /// <summary>
    /// Botão de transporte estilizado com ícone proeminente e legenda inferior
    /// </summary>
    private static Button CreateTransportControl(
        string label,
        string glyph,
        Brush accent,
        Brush accentForeground,
        Brush surface,
        Brush foreground,
        Brush? border,
        double scale,
        bool isFilled = false)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = Math.Clamp(18 * scale, 16, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2 * scale)
        };

        var text = new TextBlock
        {
            Text = label,
            FontFamily = TextFont,
            FontSize = Math.Clamp(9.5 * scale, 8.5, 12),
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "plugin-font:9.5",
            TextAlignment = TextAlignment.Center
        };

        panel.Children.Add(icon);
        panel.Children.Add(text);

        var btn = new Button
        {
            Content = panel,
            Tag = "plugin-interactive",
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = Math.Max(46, 50 * scale),
            Padding = new Thickness(4 * scale, 4 * scale, 4 * scale, 4 * scale),
            BorderThickness = new Thickness(border != null ? 1 : 0),
            BorderBrush = border ?? Brushes.Transparent,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };

        AttachHoverScale(btn);

        if (isFilled)
        {
            btn.Background = accent;
            btn.BorderBrush = accent;
            icon.Foreground = accentForeground;
            text.Foreground = accentForeground;
            text.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            btn.Background = surface;
            btn.BorderBrush = border ?? Brushes.Transparent;
            icon.Foreground = foreground;
            text.Foreground = foreground;
        }

        return btn;
    }

    private static void UpdateControlLabel(Button btn, string label, string glyph)
    {
        if (btn.Content is StackPanel panel && panel.Children.Count >= 2)
        {
            if (panel.Children[0] is TextBlock icon) icon.Text = glyph;
            if (panel.Children[1] is TextBlock text) text.Text = label;
        }
    }

    private static Border CreateDivider(Brush borderBrush, double scale)
    {
        return new Border
        {
            Background = borderBrush,
            Width = 1,
            Margin = new Thickness(8 * scale, 4 * scale, 8 * scale, 4 * scale),
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    private static void AttachHoverScale(Button btn)
    {
        btn.MouseEnter += (_, _) =>
        {
            if (btn.IsEnabled && btn.RenderTransform is ScaleTransform st)
            {
                var anim = new DoubleAnimation(1.04, TimeSpan.FromMilliseconds(70));
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
        };
        btn.MouseLeave += (_, _) =>
        {
            if (btn.RenderTransform is ScaleTransform st)
            {
                var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(70));
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
        };
    }

    private static Brush GetBrush(string color, string fallback = "#000000")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

    private static Brush GetContrastBrush(string color)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            var luminance = (0.299 * parsed.R) + (0.587 * parsed.G) + (0.114 * parsed.B);
            return GetBrush(luminance > 160 ? "#0F172A" : "#FFFFFF");
        }
        catch { return Brushes.White; }
    }

    #endregion
}
