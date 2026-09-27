using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Timer;

public sealed class TimerPlugin : IWindowsPluginRenderer
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI, sans-serif");
    private static readonly FontFamily MonoFont = new("Segoe UI Variable Display, Consolas, Segoe UI, monospace");

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var scale = Math.Clamp(context.Scale, 0.75, 2.0);

        // Theme palette
        var foreground = GetBrush(context.IsDarkMode ? "#F0F6FC" : "#1F2328");
        var muted = GetBrush(context.IsDarkMode ? "#8B949E" : "#656D76");
        var cardSurface = GetBrush(context.IsDarkMode ? "#161B22" : "#F6F8FA");
        var controlSurface = GetBrush(context.IsDarkMode ? "#21262D" : "#FFFFFF");
        var borderBrush = GetBrush(context.IsDarkMode ? "#30363D" : "#D0D7DE");
        var accent = GetBrush(context.AccentColor, "#F59E0B");
        var accentForeground = GetContrastBrush(context.AccentColor);
        var accentTrack = GetAlphaBrush(context.AccentColor, 0.18, "#2E2415");
        var completedGlow = GetAlphaBrush(context.AccentColor, 0.25, "#3D2B10");

        var state = context.State;
        var status = state.GetString("status") ?? "idle";
        var duration = state.GetInt32("durationSeconds", TimerModule.DefaultDuration);
        var selectedPreset = state.GetString("selectedPreset") ?? "";
        var label = state.GetString("label") ?? "Temporizador";
        var remaining = TimerModule.CalculateRemainingSeconds(state, DateTimeOffset.UtcNow);

        // Root container
        var root = new Border
        {
            Background = cardSurface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12 * scale),
            Padding = new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale)
        };

        void UpdateSpacing()
        {
            root.Padding = (context.Width < 280 || root.ActualWidth is > 0 and < 280)
                ? new Thickness(6 * scale, 5 * scale, 6 * scale, 5 * scale)
                : new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale);
        }
        root.SizeChanged += (_, _) => UpdateSpacing();
        context.LayoutChanged += UpdateSpacing;
        UpdateSpacing();

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Progress ring & digits
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Preset chips
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Custom duration editor (if open)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Action buttons

        // --- ROW 0: Header (NO redundant title) ---
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4 * scale) };
        if (!string.IsNullOrWhiteSpace(label) && !string.Equals(label, "Temporizador", StringComparison.OrdinalIgnoreCase))
        {
            var titleText = new TextBlock
            {
                Text = label,
                FontFamily = TextFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = Math.Clamp(12 * scale, 10, 18),
                Foreground = muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 200 * scale
            };
            header.Children.Add(titleText);
        }
        Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // --- ROW 1: Center Circular Progress Ring & Digits ---
        var centerContainer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2 * scale, 0, 6 * scale)
        };

        double ringRadius = Math.Clamp(46 * scale, 36, 75);
        double strokeThickness = Math.Clamp(6 * scale, 4, 8);
        double ringSize = (ringRadius * 2) + strokeThickness + (8 * scale);
        double centerCoord = ringSize / 2.0;

        var ringCanvas = new Canvas
        {
            Width = ringSize,
            Height = ringSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Background track circle
        var trackCircle = new Ellipse
        {
            Width = ringRadius * 2,
            Height = ringRadius * 2,
            Stroke = accentTrack,
            StrokeThickness = strokeThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Canvas.SetLeft(trackCircle, centerCoord - ringRadius);
        Canvas.SetTop(trackCircle, centerCoord - ringRadius);
        ringCanvas.Children.Add(trackCircle);

        // Foreground active progress arc
        double fraction = duration > 0 ? (double)remaining / duration : 0.0;
        var progressArc = new Path
        {
            Stroke = accent,
            StrokeThickness = strokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = CreateProgressArcGeometry(centerCoord, ringRadius, fraction)
        };
        ringCanvas.Children.Add(progressArc);
        centerContainer.Children.Add(ringCanvas);

        // Digits and Status Overlay inside ring
        var textOverlay = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var digitsBlock = new TextBlock
        {
            Text = TimerModule.FormatTime(remaining),
            FontFamily = MonoFont,
            FontWeight = FontWeights.Bold,
            FontSize = Math.Clamp(28 * scale, 20, 40),
            Foreground = (status == "completed") ? accent : foreground,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var digitsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        digitsRow.Children.Add(digitsBlock);

        var copyBtn = PluginButtons.Create(context, "Copiar");
        copyBtn.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(13 * scale, 11, 20)
        };
        copyBtn.MinWidth = 26 * scale;
        copyBtn.MinHeight = 26 * scale;
        copyBtn.ToolTip = "Copiar tempo do temporizador";
        AutomationProperties.SetName(copyBtn, "Copiar tempo do temporizador");
        copyBtn.IsEnabled = remaining > 0 || status == "completed";
        copyBtn.VerticalAlignment = VerticalAlignment.Center;
        copyBtn.Margin = new Thickness(6 * scale, 0, 0, 0);
        copyBtn.Click += (_, e) =>
        {
            e.Handled = true;
            var text = context.Module.GetClipboardText(context.State);
            if (!string.IsNullOrEmpty(text))
            {
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, text));
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Tempo copiado!"));
            }
        };
        digitsRow.Children.Add(copyBtn);
        textOverlay.Children.Add(digitsRow);

        var statusSubtext = new TextBlock
        {
            Text = status switch
            {
                "running" => "em andamento",
                "paused" => "pausado",
                "completed" => "concluído!",
                _ => $"total {TimerModule.FormatTime(duration)}"
            },
            FontFamily = TextFont,
            FontSize = Math.Clamp(11 * scale, 9, 16),
            Foreground = (status == "completed") ? accent : muted,
            FontWeight = (status == "completed") ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2 * scale, 0, 0)
        };
        textOverlay.Children.Add(statusSubtext);

        if (status == "completed")
        {
            var badgeBorder = new Border
            {
                Background = completedGlow,
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10 * scale),
                Padding = new Thickness(8 * scale, 2 * scale, 8 * scale, 2 * scale),
                Margin = new Thickness(0, 4 * scale, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var badgeText = new TextBlock
            {
                Text = "🎉 Tempo Finalizado!",
                FontFamily = TextFont,
                FontSize = Math.Clamp(10 * scale, 9, 14),
                FontWeight = FontWeights.Bold,
                Foreground = accent
            };
            badgeBorder.Child = badgeText;
            textOverlay.Children.Add(badgeBorder);
        }

        centerContainer.Children.Add(textOverlay);
        Grid.SetRow(centerContainer, 1);
        mainGrid.Children.Add(centerContainer);

        // --- ROW 2: Preset Chips ---
        var presetsContainer = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8 * scale)
        };

        foreach (var presetKey in new[] { "5m", "10m", "15m", "25m", "45m", "60m" })
        {
            bool isCurrent = string.Equals(selectedPreset, presetKey, StringComparison.OrdinalIgnoreCase);
            var chip = CreatePresetChip(presetKey, isCurrent, accent, accentForeground, controlSurface, foreground, borderBrush, scale);
            chip.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand("select-preset", new Dictionary<string, string>
                {
                    ["preset"] = presetKey
                }));
            };
            presetsContainer.Children.Add(chip);
        }

        // Custom duration chip button
        bool isCustom = string.IsNullOrEmpty(selectedPreset) && status == "idle";
        var customChip = CreatePresetChip("Personalizar", isCustom, accent, accentForeground, controlSurface, foreground, borderBrush, scale);
        presetsContainer.Children.Add(customChip);

        Grid.SetRow(presetsContainer, 2);
        mainGrid.Children.Add(presetsContainer);

        // --- ROW 3: Custom Duration Editor (Collapsible) ---
        var customEditor = new Border
        {
            Background = controlSurface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(8 * scale),
            Margin = new Thickness(0, 0, 0, 8 * scale),
            Visibility = (context.IsEditing || isCustom) ? Visibility.Visible : Visibility.Collapsed
        };

        var customGrid = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var customLabel = new TextBlock
        {
            Text = "Minutos:",
            FontFamily = TextFont,
            FontSize = Math.Clamp(11 * scale, 9, 15),
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6 * scale, 0)
        };
        var customInput = new TextBox
        {
            Text = (duration / 60).ToString(CultureInfo.InvariantCulture),
            FontFamily = MonoFont,
            FontSize = Math.Clamp(12 * scale, 10, 16),
            Foreground = foreground,
            Background = cardSurface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Width = 48 * scale,
            Padding = new Thickness(4 * scale, 2 * scale, 4 * scale, 2 * scale),
            TextAlignment = TextAlignment.Center,
            Tag = "plugin-interactive"
        };
        var applyCustomBtn = PluginButtons.Create(context, "Definir", primary: true);
        applyCustomBtn.Margin = new Thickness(8 * scale, 0, 0, 0);
        applyCustomBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            if (int.TryParse(customInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
            {
                var totalSeconds = minutes * 60;
                await context.ExecuteAsync(new PluginCommand("set-duration", new Dictionary<string, string>
                {
                    ["seconds"] = totalSeconds.ToString(CultureInfo.InvariantCulture)
                }));
            }
            else
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Por favor insira um número inteiro de minutos maior que zero."));
            }
        };

        customGrid.Children.Add(customLabel);
        customGrid.Children.Add(customInput);
        customGrid.Children.Add(applyCustomBtn);
        customEditor.Child = customGrid;

        customChip.Click += (_, e) =>
        {
            e.Handled = true;
            customEditor.Visibility = customEditor.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        };

        Grid.SetRow(customEditor, 3);
        mainGrid.Children.Add(customEditor);

        // --- ROW 4: Action Controls ---
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        if (status is "idle" or "completed")
        {
            var startBtn = PluginButtons.Create(context, (status == "completed") ? "Reiniciar" : "Iniciar", primary: true);
            startBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand("start"));
            };
            actionsPanel.Children.Add(startBtn);
        }
        else if (status == "running")
        {
            var pauseBtn = PluginButtons.Create(context, "Pausar", primary: true);
            pauseBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand("pause"));
            };
            actionsPanel.Children.Add(pauseBtn);

            var resetBtn = PluginButtons.Create(context, "Zerar", primary: false);
            resetBtn.Margin = new Thickness(8 * scale, 0, 0, 0);
            resetBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand("reset"));
            };
            actionsPanel.Children.Add(resetBtn);
        }
        else if (status == "paused")
        {
            var resumeBtn = PluginButtons.Create(context, "Continuar", primary: true);
            resumeBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand("resume"));
            };
            actionsPanel.Children.Add(resumeBtn);

            var resetBtn = PluginButtons.Create(context, "Reiniciar", primary: false);
            resetBtn.Margin = new Thickness(8 * scale, 0, 0, 0);
            resetBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand("reset"));
            };
            actionsPanel.Children.Add(resetBtn);
        }

        Grid.SetRow(actionsPanel, 4);
        mainGrid.Children.Add(actionsPanel);

        root.Child = mainGrid;

        // Lifecycle & Real-time Update via DispatcherTimer
        var timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };

        timer.Tick += async (_, _) =>
        {
            var curStatus = context.State.GetString("status") ?? "idle";
            if (curStatus != "running")
            {
                return;
            }

            var currentRemaining = TimerModule.CalculateRemainingSeconds(context.State, DateTimeOffset.UtcNow);

            if (currentRemaining <= 0)
            {
                timer.Stop();
                await context.ExecuteAsync(new PluginCommand("check-completion"), rebuild: true, recordUndo: false);
            }
            else
            {
                digitsBlock.Text = TimerModule.FormatTime(currentRemaining);
                double currentFraction = duration > 0 ? (double)currentRemaining / duration : 0.0;
                progressArc.Data = CreateProgressArcGeometry(centerCoord, ringRadius, currentFraction);
                copyBtn.IsEnabled = currentRemaining > 0;
            }
        };

        if (status == "running")
        {
            timer.Start();
        }

        root.Unloaded += (_, _) =>
        {
            timer.Stop();
        };

        return root;
    }

    private static Geometry CreateProgressArcGeometry(double center, double radius, double fraction)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        if (fraction <= 0.0005)
        {
            return Geometry.Empty;
        }

        if (fraction >= 0.999)
        {
            return new EllipseGeometry(new Point(center, center), radius, radius);
        }

        double angleDeg = fraction * 360.0;
        double angleRad = (angleDeg - 90.0) * Math.PI / 180.0;

        var startPoint = new Point(center, center - radius);
        var endPoint = new Point(
            center + radius * Math.Cos(angleRad),
            center + radius * Math.Sin(angleRad));

        bool isLargeArc = angleDeg > 180.0;

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment(
            endPoint,
            new Size(radius, radius),
            0.0,
            isLargeArc,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Button CreatePresetChip(
        string label,
        bool isActive,
        Brush accent,
        Brush accentForeground,
        Brush surface,
        Brush foreground,
        Brush borderBrush,
        double scale)
    {
        var button = new Button
        {
            Content = label,
            FontFamily = TextFont,
            FontSize = Math.Clamp(11 * scale, 9, 15),
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            Background = isActive ? accent : surface,
            Foreground = isActive ? accentForeground : foreground,
            BorderBrush = isActive ? accent : borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8 * scale, 3 * scale, 8 * scale, 3 * scale),
            Margin = new Thickness(3 * scale),
            Template = CreateRoundedButtonTemplate(12 * scale),
            Tag = "plugin-interactive",
            Cursor = Cursors.Hand
        };
        return button;
    }

    private static Button CreateActionButton(
        string iconGlyph,
        string label,
        Brush background,
        Brush foreground,
        Brush? borderBrush,
        double scale,
        bool isPrimary)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = iconGlyph,
            FontFamily = IconFont,
            FontSize = Math.Clamp(13 * scale, 11, 18),
            Foreground = foreground,
            Margin = new Thickness(0, 0, 6 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = TextFont,
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = Math.Clamp(12 * scale, 10, 16),
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new Button
        {
            Content = content,
            Background = background,
            Foreground = foreground,
            BorderBrush = borderBrush ?? background,
            BorderThickness = new Thickness(borderBrush is not null ? 1 : 0),
            Padding = new Thickness(16 * scale, 6 * scale, 16 * scale, 6 * scale),
            MinHeight = Math.Max(32, 34 * scale),
            MinWidth = Math.Max(80, 90 * scale),
            Template = CreateRoundedButtonTemplate(8 * scale),
            Tag = "plugin-interactive",
            Cursor = Cursors.Hand
        };
        return button;
    }

    private static Button CreateIconButton(
        string iconGlyph,
        string tooltip,
        Brush background,
        Brush foreground,
        double scale)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = iconGlyph,
                FontFamily = IconFont,
                FontSize = Math.Clamp(12 * scale, 10, 16),
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            ToolTip = tooltip,
            Background = background,
            BorderThickness = new Thickness(0),
            Width = Math.Max(26, 28 * scale),
            Height = Math.Max(26, 28 * scale),
            Padding = new Thickness(0),
            Template = CreateRoundedButtonTemplate(6 * scale),
            Tag = "plugin-interactive",
            Cursor = Cursors.Hand
        };
        return button;
    }

    private static ControlTemplate CreateRoundedButtonTemplate(double cornerRadius)
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "bd";
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Button.Background)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Button.BorderBrush)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Button.BorderThickness)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding(nameof(Button.Padding)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(cp);

        template.VisualTree = borderFactory;
        return template;
    }

    private static Brush GetBrush(string color, string fallback = "#000000")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

    private static Brush GetAlphaBrush(string color, double opacity, string fallback = "#3B82F6")
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color);
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
        }
        catch
        {
            var f = (Color)ColorConverter.ConvertFromString(fallback);
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), f.R, f.G, f.B));
        }
    }

    private static Brush GetContrastBrush(string color)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            var luminance = (0.299 * parsed.R) + (0.587 * parsed.G) + (0.114 * parsed.B);
            return GetBrush(luminance > 160 ? "#152033" : "#FFFFFF");
        }
        catch { return Brushes.White; }
    }
}
