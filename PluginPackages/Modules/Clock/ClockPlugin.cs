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

namespace ClipDesk.Plugin.Clock;

public sealed class ClockPlugin : IWindowsPluginRenderer
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI, sans-serif");
    private static readonly FontFamily MonoFont = new("Segoe UI Variable Text, Consolas, Segoe UI, monospace");

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        double scale = Math.Clamp(context.Scale, 0.8, 2.5);
        bool dark = context.IsDarkMode;

        // Color palette
        var foreground = GetBrush(dark ? "#F8FAFC" : "#0F172A");
        var muted = GetBrush(dark ? "#94A3B8" : "#64748B");
        var borderBrush = GetBrush(dark ? "#334155" : "#E2E8F0");
        var cardSurface = GetBrush(dark ? "#162032" : "#FFFFFF");
        var altSurface = GetBrush(dark ? "#0F172A" : "#F1F5F9");
        var dayBadgeBg = GetBrush(dark ? "#2D2615" : "#FEF3C7");
        var dayBadgeFg = GetBrush(dark ? "#F59E0B" : "#D97706");
        var nightBadgeBg = GetBrush(dark ? "#172554" : "#DBEAFE");
        var nightBadgeFg = GetBrush(dark ? "#60A5FA" : "#2563EB");

        var accent = GetBrush(context.AccentColor, "#10B981");
        var accentForeground = GetContrastBrush(context.AccentColor);

        // State parameters
        var is24Hour = string.Equals(context.State.GetString("is24Hour"), "true", StringComparison.OrdinalIgnoreCase);
        var showSeconds = string.Equals(context.State.GetString("showSeconds"), "true", StringComparison.OrdinalIgnoreCase);
        var isAnalog = string.Equals(context.State.GetString("displayMode"), "analog", StringComparison.OrdinalIgnoreCase);
        var primaryZoneId = context.State.GetString("primaryZone");

        var cities = ClockModule.GetCities(context.State);

        // Root container
        var root = new Grid
        {
            Margin = new Thickness(8 * scale),
            Background = Brushes.Transparent
        };

        void UpdateSpacing()
        {
            root.Margin = (context.Width < 280 || root.ActualWidth is > 0 and < 280)
                ? new Thickness(6 * scale, 4 * scale, 6 * scale, 5 * scale)
                : new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale);
        }
        root.SizeChanged += (_, _) => UpdateSpacing();
        context.LayoutChanged += UpdateSpacing;
        UpdateSpacing();

        // Layout rows:
        // Row 0: Actions toolbar (mode, format, seconds) - NO redundant title
        // Row 1: Primary Clock Face (Vector Analog or Digital) with Copy icon
        // Row 2: World Cities Header & Quick Add bar
        // Row 3: Scrollable World Cities List
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 40 * scale });

        // =========================================================================
        // ROW 0: TOP ACTIONS TOOLBAR (NO REDUNDANT TITLE)
        // =========================================================================
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6 * scale) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var actionsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Toggle Analog/Digital Mode
        var modeToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("analog", "Analógico", "\ue823"),
            new PluginToggleOption("digital", "Digital", "\ue8ef")
        }, isAnalog ? "analog" : "digital", async mode =>
        {
            await context.ExecuteAsync(new PluginCommand("set-display-mode", new Dictionary<string, string>
            {
                ["mode"] = mode
            }));
        });
        modeToggle.Margin = new Thickness(0, 0, 4 * scale, 2 * scale);

        // Toggle 12h / 24h
        var formatToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("12h", "12h"),
            new PluginToggleOption("24h", "24h")
        }, is24Hour ? "24h" : "12h", async mode =>
        {
            await context.ExecuteAsync(new PluginCommand("toggle-format"));
        });
        formatToggle.Margin = new Thickness(0, 0, 4 * scale, 2 * scale);

        // Toggle Seconds
        var secBtn = PluginButtons.Create(context, showSeconds ? "Sem :ss" : "Com :ss");
        secBtn.ToolTip = showSeconds ? "Ocultar segundos" : "Exibir segundos";
        secBtn.Margin = new Thickness(0, 0, 0, 2 * scale);
        secBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await context.ExecuteAsync(new PluginCommand("toggle-seconds"));
        };

        actionsPanel.Children.Add(modeToggle);
        actionsPanel.Children.Add(formatToggle);
        actionsPanel.Children.Add(secBtn);

        Grid.SetColumn(actionsPanel, 0);
        header.Children.Add(actionsPanel);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // =========================================================================
        // ROW 1: PRIMARY CLOCK FACE
        // =========================================================================
        var primaryCard = new Border
        {
            Background = cardSurface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10 * scale),
            Padding = new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale),
            Margin = new Thickness(0, 0, 0, 8 * scale)
        };

        // Elements for updating via DispatcherTimer
        Line? hourHand = null;
        Line? minuteHand = null;
        Line? secondHand = null;
        TextBlock? primaryTimeLabel = null;
        TextBlock? primaryDateLabel = null;
        TextBlock? primaryZoneLabel = null;

        var initialNow = DateTimeOffset.UtcNow;
        var initialPrimaryTime = TimeZoneResolver.GetLocalTime(initialNow, primaryZoneId);

        if (isAnalog)
        {
            // Vector Analog Clock Face with Shapes
            var analogContainer = new Grid();
            analogContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            analogContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            double dialSize = Math.Clamp(95 * scale, 75, 140);
            double cx = dialSize / 2.0;
            double cy = dialSize / 2.0;
            double radius = (dialSize / 2.0) - (4 * scale);

            var canvas = new Canvas
            {
                Width = dialSize,
                Height = dialSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2 * scale, 0, 4 * scale)
            };

            // Dial Background & Rim
            var dialRim = new Ellipse
            {
                Width = dialSize,
                Height = dialSize,
                Fill = altSurface,
                Stroke = borderBrush,
                StrokeThickness = Math.Max(1.0, 1.5 * scale)
            };
            Canvas.SetLeft(dialRim, 0);
            Canvas.SetTop(dialRim, 0);
            canvas.Children.Add(dialRim);

            // 12 Radial Hour Markers
            for (int i = 0; i < 12; i++)
            {
                double angleDeg = i * 30.0;
                double angleRad = angleDeg * (Math.PI / 180.0);
                bool isMajor = (i % 3 == 0);

                double innerR = isMajor ? (radius - 7 * scale) : (radius - 4 * scale);
                double outerR = radius - 1.5 * scale;

                double x1 = cx + (innerR * Math.Sin(angleRad));
                double y1 = cy - (innerR * Math.Cos(angleRad));
                double x2 = cx + (outerR * Math.Sin(angleRad));
                double y2 = cy - (outerR * Math.Cos(angleRad));

                var tick = new Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = isMajor ? foreground : muted,
                    StrokeThickness = isMajor ? Math.Max(1.5, 2.0 * scale) : Math.Max(1.0, 1.0 * scale),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Opacity = isMajor ? 0.9 : 0.6
                };
                canvas.Children.Add(tick);
            }

            // Hour Hand
            double hourLength = radius * 0.52;
            hourHand = new Line
            {
                X1 = cx,
                Y1 = cy + (4 * scale),
                X2 = cx,
                Y2 = cy - hourLength,
                Stroke = foreground,
                StrokeThickness = Math.Max(2.5, 3.2 * scale),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new RotateTransform(0, cx, cy)
            };
            canvas.Children.Add(hourHand);

            // Minute Hand
            double minuteLength = radius * 0.78;
            minuteHand = new Line
            {
                X1 = cx,
                Y1 = cy + (6 * scale),
                X2 = cx,
                Y2 = cy - minuteLength,
                Stroke = foreground,
                StrokeThickness = Math.Max(1.8, 2.2 * scale),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new RotateTransform(0, cx, cy),
                Opacity = 0.95
            };
            canvas.Children.Add(minuteHand);

            // Second Hand
            if (showSeconds)
            {
                double secLength = radius * 0.86;
                double tailLength = radius * 0.18;
                secondHand = new Line
                {
                    X1 = cx,
                    Y1 = cy + tailLength,
                    X2 = cx,
                    Y2 = cy - secLength,
                    Stroke = accent,
                    StrokeThickness = Math.Max(1.2, 1.5 * scale),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    RenderTransform = new RotateTransform(0, cx, cy)
                };
                canvas.Children.Add(secondHand);
            }

            // Pivot Center Dots
            var pivotOuter = new Ellipse
            {
                Width = 8 * scale,
                Height = 8 * scale,
                Fill = accent
            };
            Canvas.SetLeft(pivotOuter, cx - (4 * scale));
            Canvas.SetTop(pivotOuter, cy - (4 * scale));
            canvas.Children.Add(pivotOuter);

            var pivotInner = new Ellipse
            {
                Width = 3 * scale,
                Height = 3 * scale,
                Fill = altSurface
            };
            Canvas.SetLeft(pivotInner, cx - (1.5 * scale));
            Canvas.SetTop(pivotInner, cy - (1.5 * scale));
            canvas.Children.Add(pivotInner);

            Grid.SetRow(canvas, 0);
            analogContainer.Children.Add(canvas);

            // Digital Subtitle under Analog Face
            var subPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3 * scale, 0, 0)
            };

            primaryTimeLabel = new TextBlock
            {
                Text = TimeZoneResolver.FormatTime(initialPrimaryTime, is24Hour, showSeconds),
                FontFamily = MonoFont,
                FontSize = Math.Clamp(16 * scale, 14, 24),
                FontWeight = FontWeights.Bold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var dateText = initialPrimaryTime.ToString("D", new CultureInfo("pt-BR"));
            primaryDateLabel = new TextBlock
            {
                Text = char.ToUpperInvariant(dateText[0]) + dateText[1..],
                FontFamily = TextFont,
                FontSize = Math.Clamp(10.5 * scale, 9.5, 14),
                Foreground = muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1 * scale, 0, 0)
            };

            var offsetStr = TimeZoneResolver.FormatOffset(initialPrimaryTime.Offset);
            var tzName = string.IsNullOrWhiteSpace(primaryZoneId) ? "Horário Local" : primaryZoneId;
            primaryZoneLabel = new TextBlock
            {
                Text = $"{tzName} • {offsetStr}",
                FontFamily = TextFont,
                FontSize = Math.Clamp(9.5 * scale, 8.5, 12),
                Foreground = muted,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var timeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            timeRow.Children.Add(primaryTimeLabel);
            timeRow.Children.Add(CreateCopyIconButton(context, scale));

            subPanel.Children.Add(timeRow);
            subPanel.Children.Add(primaryDateLabel);
            subPanel.Children.Add(primaryZoneLabel);

            Grid.SetRow(subPanel, 1);
            analogContainer.Children.Add(subPanel);

            primaryCard.Child = analogContainer;
        }
        else
        {
            // High-Legibility Digital Clock Face
            var digitalContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6 * scale, 0, 6 * scale)
            };

            primaryTimeLabel = new TextBlock
            {
                Text = TimeZoneResolver.FormatTime(initialPrimaryTime, is24Hour, showSeconds),
                FontFamily = MonoFont,
                FontSize = Math.Clamp(34 * scale, 24, 46),
                FontWeight = FontWeights.Bold,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var timeRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            timeRow.Children.Add(primaryTimeLabel);
            timeRow.Children.Add(CreateCopyIconButton(context, scale));

            var dateText = initialPrimaryTime.ToString("D", new CultureInfo("pt-BR"));
            primaryDateLabel = new TextBlock
            {
                Text = char.ToUpperInvariant(dateText[0]) + dateText[1..],
                FontFamily = TextFont,
                FontSize = Math.Clamp(12.5 * scale, 11, 17),
                FontWeight = FontWeights.Medium,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2 * scale, 0, 0)
            };

            var offsetStr = TimeZoneResolver.FormatOffset(initialPrimaryTime.Offset);
            var tzName = string.IsNullOrWhiteSpace(primaryZoneId) ? "Horário Local" : primaryZoneId;
            primaryZoneLabel = new TextBlock
            {
                Text = $"{tzName} • {offsetStr}",
                FontFamily = MonoFont,
                FontSize = Math.Clamp(10.5 * scale, 9, 14),
                Foreground = muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3 * scale, 0, 0)
            };

            digitalContainer.Children.Add(timeRow);
            digitalContainer.Children.Add(primaryDateLabel);
            digitalContainer.Children.Add(primaryZoneLabel);

            primaryCard.Child = digitalContainer;
        }

        Grid.SetRow(primaryCard, 1);
        root.Children.Add(primaryCard);

        // =========================================================================
        // ROW 2: WORLD CITIES HEADER & EXPANDABLE QUICK ADD BAR
        // =========================================================================
        var citiesSection = new StackPanel { Margin = new Thickness(0, 0, 0, 6 * scale) };

        var citiesHeader = new Grid();
        citiesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        citiesHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var citiesTitle = new TextBlock
        {
            Text = $"Cidades Mundiais ({cities.Count})",
            FontFamily = TextFont,
            FontSize = Math.Clamp(11.5 * scale, 10, 16),
            FontWeight = FontWeights.SemiBold,
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(citiesTitle, 0);
        citiesHeader.Children.Add(citiesTitle);

        var quickAddPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 6 * scale, 0, 4 * scale)
        };

        var addCityBtn = PluginButtons.Create(context, "+ Adicionar");
        addCityBtn.ToolTip = "Adicionar nova cidade mundial";
        addCityBtn.Click += (_, e) =>
        {
            e.Handled = true;
            quickAddPanel.Visibility = quickAddPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        };
        Grid.SetColumn(addCityBtn, 1);
        citiesHeader.Children.Add(addCityBtn);
        citiesSection.Children.Add(citiesHeader);

        var presets = new (string City, string Zone)[]
        {
            ("São Paulo", "America/Sao_Paulo"),
            ("Nova York", "America/New_York"),
            ("Londres", "Europe/London"),
            ("Paris", "Europe/Paris"),
            ("Tóquio", "Asia/Tokyo"),
            ("Sydney", "Australia/Sydney"),
            ("Dubai", "Asia/Dubai"),
            ("Los Angeles", "America/Los_Angeles"),
            ("Hong Kong", "Asia/Hong_Kong"),
            ("Berlim", "Europe/Berlin"),
            ("Buenos Aires", "America/Argentina/Buenos_Aires"),
            ("Toronto", "America/Toronto"),
            ("Roma", "Europe/Rome"),
            ("Madri", "Europe/Madrid"),
            ("Singapura", "Asia/Singapore"),
            ("Seul", "Asia/Seoul"),
            ("Cidade do México", "America/Mexico_City"),
            ("Chicago", "America/Chicago"),
            ("UTC", "UTC")
        };

        var cityDropdownOptions = presets
            .Where(p => !cities.Any(c => string.Equals(c.City, p.City, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new PluginDropdownOption($"{p.Zone}|{p.City}", p.City, p.Zone))
            .ToList();

        if (cityDropdownOptions.Count > 0)
        {
            var cityDropdown = PluginDropdowns.Create(context, cityDropdownOptions, "", async selected =>
            {
                var parts = selected.Split('|');
                if (parts.Length == 2)
                {
                    await context.ExecuteAsync(new PluginCommand("add-world-clock", new Dictionary<string, string>
                    {
                        ["zone"] = parts[0],
                        ["city"] = parts[1]
                    }));
                }
            }, "Selecionar cidade mundial…");
            cityDropdown.Margin = new Thickness(0, 0, 0, 6 * scale);
            quickAddPanel.Children.Add(cityDropdown);
        }

        // Preset Chips for popular cities
        var presetChipsGrid = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4 * scale, 0, 4 * scale)
        };

        foreach (var preset in presets.Take(8))
        {
            bool alreadyAdded = cities.Any(c => string.Equals(c.City, preset.City, StringComparison.OrdinalIgnoreCase));
            if (alreadyAdded) continue;

            var chip = CreatePillButton($"+ {preset.City}", null, cardSurface, foreground, scale * 0.9);
            chip.Margin = new Thickness(0, 0, 4 * scale, 4 * scale);
            chip.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand("add-world-clock", new Dictionary<string, string>
                {
                    ["city"] = preset.City,
                    ["zone"] = preset.Zone
                }));
            };
            presetChipsGrid.Children.Add(chip);
        }

        // Custom City input bar
        var customInputGrid = new Grid { Margin = new Thickness(0, 3 * scale, 0, 0) };
        customInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        customInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        customInputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cityBox = new TextBox
        {
            Text = "",
            Tag = "plugin-interactive",
            Background = cardSurface,
            Foreground = foreground,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6 * scale, 4 * scale, 6 * scale, 4 * scale),
            FontSize = Math.Clamp(11 * scale, 10, 14),
            Margin = new Thickness(0, 0, 4 * scale, 0)
        };

        var zoneBox = new TextBox
        {
            Text = "",
            Tag = "plugin-interactive",
            Background = cardSurface,
            Foreground = foreground,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6 * scale, 4 * scale, 6 * scale, 4 * scale),
            FontSize = Math.Clamp(11 * scale, 10, 14),
            Margin = new Thickness(0, 0, 4 * scale, 0)
        };

        var confirmAddBtn = PluginButtons.Create(context, "Adicionar", primary: true);
        confirmAddBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(cityBox.Text))
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Por favor, digite o nome da cidade."));
                return;
            }
            var zone = string.IsNullOrWhiteSpace(zoneBox.Text) ? "UTC" : zoneBox.Text.Trim();
            var result = await context.ExecuteAsync(new PluginCommand("add-world-clock", new Dictionary<string, string>
            {
                ["city"] = cityBox.Text.Trim(),
                ["zone"] = zone
            }));
            if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, result.Message));
            }
        };

        Grid.SetColumn(cityBox, 0);
        Grid.SetColumn(zoneBox, 1);
        Grid.SetColumn(confirmAddBtn, 2);
        customInputGrid.Children.Add(cityBox);
        customInputGrid.Children.Add(zoneBox);
        customInputGrid.Children.Add(confirmAddBtn);

        var hintText = new TextBlock
        {
            Text = "Dica: Digite a cidade e o fuso (ex: Madri / Europe/Madrid ou UTC+1)",
            FontFamily = TextFont,
            FontSize = Math.Clamp(9.5 * scale, 8.5, 12),
            Foreground = muted,
            Margin = new Thickness(0, 2 * scale, 0, 4 * scale)
        };

        quickAddPanel.Children.Add(presetChipsGrid);
        quickAddPanel.Children.Add(hintText);
        quickAddPanel.Children.Add(customInputGrid);

        citiesSection.Children.Add(quickAddPanel);

        Grid.SetRow(citiesSection, 2);
        root.Children.Add(citiesSection);

        // =========================================================================
        // ROW 3: SCROLLABLE WORLD CITIES LIST
        // =========================================================================
        var cityUpdaters = new List<Action<DateTimeOffset, DateTimeOffset, bool, bool>>();

        var citiesScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly
        };

        var citiesList = new StackPanel { Orientation = Orientation.Vertical };

        if (cities.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "Nenhuma cidade adicionada. Clique em '+ Adicionar' acima para incluir cidades.",
                FontFamily = TextFont,
                FontSize = Math.Clamp(11 * scale, 10, 15),
                Foreground = muted,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 15 * scale, 0, 0)
            };
            citiesList.Children.Add(emptyText);
        }
        else
        {
            foreach (var city in cities)
            {
                var cityCard = new Border
                {
                    Background = cardSurface,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7 * scale),
                    Padding = new Thickness(8 * scale, 5 * scale, 8 * scale, 5 * scale),
                    Margin = new Thickness(0, 0, 0, 5 * scale)
                };

                var cityGrid = new Grid();
                cityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Day/Night badge
                cityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name + offset
                cityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Time
                cityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Remove button

                // Day / Night Indicator Icon
                var dayNightBadge = new Border
                {
                    CornerRadius = new CornerRadius(5 * scale),
                    Padding = new Thickness(5 * scale, 2 * scale, 5 * scale, 2 * scale),
                    Margin = new Thickness(0, 0, 8 * scale, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var dayNightIcon = new TextBlock
                {
                    FontSize = Math.Clamp(12 * scale, 10, 18),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                dayNightBadge.Child = dayNightIcon;

                // City Name & Offset / Day Shift
                var nameStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var cityNameText = new TextBlock
                {
                    Text = city.City,
                    FontFamily = TextFont,
                    FontSize = Math.Clamp(12 * scale, 11, 16),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = foreground
                };

                var detailsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var offsetLabel = new TextBlock
                {
                    FontFamily = MonoFont,
                    FontSize = Math.Clamp(9.5 * scale, 8.5, 12),
                    Foreground = muted
                };
                var dayDiffBadge = new TextBlock
                {
                    FontFamily = MonoFont,
                    FontSize = Math.Clamp(9.5 * scale, 8.5, 12),
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    Margin = new Thickness(4 * scale, 0, 0, 0)
                };
                detailsPanel.Children.Add(offsetLabel);
                detailsPanel.Children.Add(dayDiffBadge);

                nameStack.Children.Add(cityNameText);
                nameStack.Children.Add(detailsPanel);

                // City Local Time Text
                var timeText = new TextBlock
                {
                    FontFamily = MonoFont,
                    FontSize = Math.Clamp(14 * scale, 12, 20),
                    FontWeight = FontWeights.Bold,
                    Foreground = foreground,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6 * scale, 0, 8 * scale, 0)
                };

                // Remove Button
                var removeBtn = CreateIconButton("\ue711", altSurface, muted, scale);
                removeBtn.ToolTip = $"Remover {city.City}";
                removeBtn.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await context.ExecuteAsync(new PluginCommand("remove-world-clock", new Dictionary<string, string>
                    {
                        ["id"] = city.Id
                    }));
                };

                Grid.SetColumn(dayNightBadge, 0);
                Grid.SetColumn(nameStack, 1);
                Grid.SetColumn(timeText, 2);
                Grid.SetColumn(removeBtn, 3);

                cityGrid.Children.Add(dayNightBadge);
                cityGrid.Children.Add(nameStack);
                cityGrid.Children.Add(timeText);
                cityGrid.Children.Add(removeBtn);

                cityCard.Child = cityGrid;
                citiesList.Children.Add(cityCard);

                // Initial render for this city
                var initialCityTime = TimeZoneResolver.GetLocalTime(initialNow, city.Zone);
                bool isDay = TimeZoneResolver.IsDaytime(initialCityTime);
                dayNightIcon.Text = isDay ? "☀️" : "🌙";
                dayNightBadge.Background = isDay ? dayBadgeBg : nightBadgeBg;
                dayNightBadge.BorderBrush = isDay ? dayBadgeFg : nightBadgeFg;
                timeText.Text = TimeZoneResolver.FormatTime(initialCityTime, is24Hour, showSeconds);
                offsetLabel.Text = TimeZoneResolver.FormatOffset(initialCityTime.Offset);
                var diff = TimeZoneResolver.GetDayDifference(initialCityTime, initialPrimaryTime);
                dayDiffBadge.Text = diff == "Hoje" ? "" : $"({diff})";

                // Register updater for timer ticks
                cityUpdaters.Add((utcNow, refTime, is24H, showSec) =>
                {
                    var cTime = TimeZoneResolver.GetLocalTime(utcNow, city.Zone);
                    bool day = TimeZoneResolver.IsDaytime(cTime);
                    dayNightIcon.Text = day ? "☀️" : "🌙";
                    dayNightBadge.Background = day ? dayBadgeBg : nightBadgeBg;
                    dayNightBadge.BorderBrush = day ? dayBadgeFg : nightBadgeFg;
                    timeText.Text = TimeZoneResolver.FormatTime(cTime, is24H, showSec);
                    offsetLabel.Text = TimeZoneResolver.FormatOffset(cTime.Offset);
                    var d = TimeZoneResolver.GetDayDifference(cTime, refTime);
                    dayDiffBadge.Text = d == "Hoje" ? "" : $"({d})";
                });
            }
        }

        citiesScroll.Content = citiesList;
        Grid.SetRow(citiesScroll, 3);
        root.Children.Add(citiesScroll);

        // =========================================================================
        // DISPATCHER TIMER LIFECYCLE & TICK
        // =========================================================================
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        void UpdateClockFace()
        {
            var now = DateTimeOffset.UtcNow;
            var primaryTime = TimeZoneResolver.GetLocalTime(now, primaryZoneId);

            if (isAnalog)
            {
                if (hourHand?.RenderTransform is RotateTransform hRot)
                {
                    double h = primaryTime.Hour % 12;
                    double m = primaryTime.Minute;
                    double s = primaryTime.Second;
                    hRot.Angle = (h + (m / 60.0) + (s / 3600.0)) * 30.0;
                }

                if (minuteHand?.RenderTransform is RotateTransform mRot)
                {
                    double m = primaryTime.Minute;
                    double s = primaryTime.Second;
                    mRot.Angle = (m + (s / 60.0)) * 6.0;
                }

                if (secondHand?.RenderTransform is RotateTransform sRot)
                {
                    sRot.Angle = primaryTime.Second * 6.0;
                }
            }

            if (primaryTimeLabel != null)
            {
                primaryTimeLabel.Text = TimeZoneResolver.FormatTime(primaryTime, is24Hour, showSeconds);
            }

            if (primaryDateLabel != null)
            {
                var dtText = primaryTime.ToString("D", new CultureInfo("pt-BR"));
                primaryDateLabel.Text = char.ToUpperInvariant(dtText[0]) + dtText[1..];
            }

            // Update world city cards
            foreach (var updater in cityUpdaters)
            {
                updater(now, primaryTime, is24Hour, showSeconds);
            }
        }

        // Run initial tick
        UpdateClockFace();

        timer.Tick += (_, _) => UpdateClockFace();
        timer.Start();

        // Guaranteed cleanup on Unloaded
        root.Unloaded += (_, _) =>
        {
            timer.Stop();
        };

        return root;
    }

    #region Helper Controls

    private static Button CreatePillButton(string label, string? iconGlyph, Brush background, Brush foreground, double scale)
    {
        var btn = new Button
        {
            Background = background,
            Foreground = foreground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8 * scale, 3 * scale, 8 * scale, 3 * scale),
            Margin = new Thickness(3 * scale, 0, 0, 0),
            Cursor = Cursors.Hand,
            Tag = "plugin-interactive"
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(iconGlyph))
        {
            sp.Children.Add(new TextBlock
            {
                Text = iconGlyph,
                FontFamily = IconFont,
                FontSize = Math.Clamp(11 * scale, 9, 15),
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4 * scale, 0)
            });
        }
        sp.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = TextFont,
            FontSize = Math.Clamp(10.5 * scale, 9, 14),
            FontWeight = FontWeights.Medium,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        });

        btn.Content = sp;
        btn.Template = CreateRoundedButtonTemplate(5 * scale);
        return btn;
    }

    private static Button CreateIconButton(string iconGlyph, Brush background, Brush foreground, double scale)
    {
        var btn = new Button
        {
            Background = background,
            Foreground = foreground,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4 * scale),
            Width = Math.Max(22, 24 * scale),
            Height = Math.Max(22, 24 * scale),
            Cursor = Cursors.Hand,
            Tag = "plugin-interactive",
            Content = new TextBlock
            {
                Text = iconGlyph,
                FontFamily = IconFont,
                FontSize = Math.Clamp(10 * scale, 9, 14),
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        btn.Template = CreateRoundedButtonTemplate(4 * scale);
        return btn;
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

    private static Button CreateCopyIconButton(WindowsPluginViewContext context, double scale)
    {
        var copyBtn = PluginButtons.Create(context, "Copiar");
        copyBtn.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(14 * scale, 12, 22)
        };
        copyBtn.MinWidth = 28 * scale;
        copyBtn.MinHeight = 28 * scale;
        copyBtn.ToolTip = "Copiar resumo de horários mundiais";
        AutomationProperties.SetName(copyBtn, "Copiar resumo de horários mundiais");
        copyBtn.VerticalAlignment = VerticalAlignment.Center;
        copyBtn.Margin = new Thickness(6 * scale, 0, 0, 0);
        copyBtn.Click += (_, e) =>
        {
            e.Handled = true;
            var text = context.Module.GetClipboardText(context.State);
            if (!string.IsNullOrWhiteSpace(text))
            {
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, text));
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Horários copiados para a área de transferência!"));
            }
        };
        return copyBtn;
    }

    #endregion
}
