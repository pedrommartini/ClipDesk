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

namespace ClipDesk.Plugin.Timezone;

/// <summary>
/// Procedural WPF renderer for Timezone Converter and Meeting Planner plugin.
/// </summary>
public sealed class TimezonePlugin : IWindowsPluginRenderer
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    private sealed class CityRowUiBinding
    {
        public required TimezoneCity City { get; init; }
        public required TextBlock TimeText { get; init; }
        public required TextBlock SubtitleText { get; init; }
        public required Border DayDiffBadge { get; init; }
        public required TextBlock DayDiffText { get; init; }
        public required Border StatusBadge { get; init; }
        public required TextBlock StatusText { get; init; }
        public required Border CardBorder { get; init; }
    }

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var dark = context.IsDarkMode;
        var scale = Math.Clamp(context.Scale, 0.75, 2.5);

        // Palette
        var bg = GetBrush(dark ? "#0F172A" : "#F8FAFC");
        var cardBg = GetBrush(dark ? "#1E293B" : "#FFFFFF");
        var cardBorderBrush = GetBrush(dark ? "#334155" : "#E2E8F0");
        var textPrimary = GetBrush(dark ? "#F8FAFC" : "#0F172A");
        var textMuted = GetBrush(dark ? "#94A3B8" : "#64748B");
        var textSubtle = GetBrush(dark ? "#64748B" : "#94A3B8");
        var accent = GetBrush(context.AccentColor, "#8B5CF6");
        var accentContrast = GetContrastBrush(context.AccentColor);

        // Working hours badge colors
        var bizBg = GetBrush(dark ? "#064E3B" : "#ECFDF5");
        var bizFg = GetBrush(dark ? "#6EE7B7" : "#047857");
        var bizBorder = GetBrush(dark ? "#059669" : "#A7F3D0");

        var shoulderBg = GetBrush(dark ? "#78350F" : "#FFFBEB");
        var shoulderFg = GetBrush(dark ? "#FCD34D" : "#B45309");
        var shoulderBorder = GetBrush(dark ? "#D97706" : "#FDE68A");

        var nightBg = GetBrush(dark ? "#26222E" : "#FEF2F2");
        var nightFg = GetBrush(dark ? "#FCA5A5" : "#B91C1C");
        var nightBorder = GetBrush(dark ? "#EF4444" : "#FECACA");

        var plusDayBg = GetBrush(dark ? "#372459" : "#EDE9FE");
        var plusDayFg = GetBrush(dark ? "#C4B5FD" : "#6D28D9");

        var minusDayBg = GetBrush(dark ? "#452A18" : "#FFEDD5");
        var minusDayFg = GetBrush(dark ? "#FDBA74" : "#C2410C");

        // Parse State
        var refZone = context.State.GetString(TimezoneModule.KeyReferenceZone) ?? "America/Sao_Paulo";
        var refCity = context.State.GetString(TimezoneModule.KeyReferenceCity) ?? "São Paulo";
        var minuteOfDay = context.State.GetInt32(TimezoneModule.KeySelectedMinuteOfDay, 840);
        var isLive = string.Equals(context.State.GetString(TimezoneModule.KeyIsLive), "true", StringComparison.OrdinalIgnoreCase);
        var cities = TimezoneModule.ParseZones(context.State);

        // Root container
        var root = new Grid
        {
            Margin = new Thickness(10 * scale),
            Background = Brushes.Transparent
        };

        void UpdateSpacing()
        {
            root.Margin = (context.Width < 280 || root.ActualWidth is > 0 and < 280)
                ? new Thickness(6 * scale, 5 * scale, 6 * scale, 5 * scale)
                : new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale);
        }
        root.SizeChanged += (_, _) => UpdateSpacing();
        context.LayoutChanged += UpdateSpacing;
        UpdateSpacing();

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header & Overall Status
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Slider & Reference Time
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // City Rows List
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Quick Presets / Add

        // ---------------------------------------------------------
        // ROW 0: Header Actions (NO duplicate title) & Feasibility Status
        // ---------------------------------------------------------
        var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8 * scale) };

        var topBar = new Grid();
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Action Button: Agora (Sync with current time)
        var nowBtn = PluginButtons.Create(context, "Sincronizar Agora", primary: true);
        nowBtn.ToolTip = "Sincronizar com horário atual de referência";
        Grid.SetColumn(nowBtn, 1);
        topBar.Children.Add(nowBtn);
        headerPanel.Children.Add(topBar);

        // Overall Feasibility Banner with accessible Copy icon
        var statusBorder = new Border
        {
            CornerRadius = new CornerRadius(6 * scale),
            Padding = new Thickness(8 * scale, 5 * scale, 8 * scale, 5 * scale),
            Margin = new Thickness(0, 6 * scale, 0, 0),
            BorderThickness = new Thickness(1)
        };
        var statusGrid = new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var statusIcon = new TextBlock
        {
            FontFamily = IconFont,
            FontSize = 13 * scale,
            Margin = new Thickness(0, 0, 6 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            FontFamily = TextFont,
            FontSize = 12 * scale,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusStack.Children.Add(statusIcon);
        statusStack.Children.Add(statusText);
        Grid.SetColumn(statusStack, 0);
        statusGrid.Children.Add(statusStack);

        var copyIconBtn = PluginButtons.Create(context, "Copiar");
        copyIconBtn.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(14 * scale, 12, 20)
        };
        copyIconBtn.MinWidth = 28 * scale;
        copyIconBtn.MinHeight = 28 * scale;
        copyIconBtn.ToolTip = "Copiar proposta de reunião";
        AutomationProperties.SetName(copyIconBtn, "Copiar proposta de reunião");
        copyIconBtn.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(copyIconBtn, 1);
        statusGrid.Children.Add(copyIconBtn);

        statusBorder.Child = statusGrid;
        headerPanel.Children.Add(statusBorder);

        Grid.SetRow(headerPanel, 0);
        root.Children.Add(headerPanel);

        // ---------------------------------------------------------
        // ROW 1: 24h Slider Section & Reference Info
        // ---------------------------------------------------------
        var sliderSection = new Border
        {
            Background = cardBg,
            BorderBrush = cardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale),
            Margin = new Thickness(0, 0, 0, 10 * scale)
        };
        var sliderPanel = new StackPanel();

        var refInfoGrid = new Grid();
        refInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        refInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var refInfoText = new TextBlock
        {
            Text = $"Fuso Base: {refCity}",
            FontFamily = TextFont,
            FontSize = 12 * scale,
            Foreground = textMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(refInfoText, 0);
        refInfoGrid.Children.Add(refInfoText);

        var timeDisplay = new TextBlock
        {
            Text = FormatMinute(minuteOfDay),
            FontFamily = DisplayFont,
            FontSize = 18 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(timeDisplay, 1);
        refInfoGrid.Children.Add(timeDisplay);
        sliderPanel.Children.Add(refInfoGrid);

        // 24h Slider
        var slider = PluginSliders.Create(context, 0, 1439, minuteOfDay, step: 15);
        slider.Margin = new Thickness(0, 6 * scale, 0, 2 * scale);
        sliderPanel.Children.Add(slider);

        // Slider Timeline Markers
        var markersGrid = new Grid();
        for (int i = 0; i < 5; i++)
            markersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var markers = new[] { "00:00", "06:00", "12:00", "18:00", "23:59" };
        for (int i = 0; i < markers.Length; i++)
        {
            var marker = new TextBlock
            {
                Text = markers[i],
                FontFamily = TextFont,
                FontSize = 10 * scale,
                Foreground = textSubtle,
                HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : (i == markers.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center)
            };
            Grid.SetColumn(marker, i);
            markersGrid.Children.Add(marker);
        }
        sliderPanel.Children.Add(markersGrid);

        sliderSection.Child = sliderPanel;
        Grid.SetRow(sliderSection, 1);
        root.Children.Add(sliderSection);

        // ---------------------------------------------------------
        // ROW 2: City Cards List (ScrollViewer)
        // ---------------------------------------------------------
        var listScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 8 * scale),
            Tag = "plugin-interactive"
        };

        var cityCardsStack = new StackPanel();
        var uiBindings = new List<CityRowUiBinding>();

        void RefreshUi(int selectedMinute)
        {
            var evaluation = TimezoneEngine.Evaluate(context.Execution.UtcNow, refZone, selectedMinute, cities);

            timeDisplay.Text = FormatMinute(selectedMinute);
            refInfoText.Text = $"Fuso Base: {refCity}";

            // Update Status Banner
            switch (evaluation.Feasibility)
            {
                case MeetingFeasibility.Ideal:
                    statusBorder.Background = bizBg;
                    statusBorder.BorderBrush = bizBorder;
                    statusIcon.Text = "\uE73E"; // Checkmark icon
                    statusIcon.Foreground = bizFg;
                    statusText.Text = "Horário Ideal para Reunião (Comercial em todas as cidades)";
                    statusText.Foreground = bizFg;
                    break;
                case MeetingFeasibility.Acceptable:
                    statusBorder.Background = shoulderBg;
                    statusBorder.BorderBrush = shoulderBorder;
                    statusIcon.Text = "\uE7BA"; // Warning / Clock icon
                    statusIcon.Foreground = shoulderFg;
                    statusText.Text = "Horário Aceitável (Algumas cidades em horário estendido)";
                    statusText.Foreground = shoulderFg;
                    break;
                case MeetingFeasibility.Inconvenient:
                    statusBorder.Background = nightBg;
                    statusBorder.BorderBrush = nightBorder;
                    statusIcon.Text = "\uEA39"; // Moon / Sleep icon
                    statusIcon.Foreground = nightFg;
                    var names = string.Join(", ", evaluation.InconvenientCities);
                    statusText.Text = $"Inconveniente para Reunião (Madrugada em {names})";
                    statusText.Foreground = nightFg;
                    break;
            }

            // Update each city card
            foreach (var b in uiBindings)
            {
                var r = evaluation.Results.FirstOrDefault(res => res.City.Id == b.City.Id);
                if (r == null) continue;

                b.TimeText.Text = r.FormattedTime;
                b.SubtitleText.Text = $"{r.Abbreviation} ({r.OffsetDisplay})";

                // Day shift
                if (r.DayOffset > 0)
                {
                    b.DayDiffBadge.Visibility = Visibility.Visible;
                    b.DayDiffBadge.Background = plusDayBg;
                    b.DayDiffText.Foreground = plusDayFg;
                    b.DayDiffText.Text = $"+{r.DayOffset}d";
                }
                else if (r.DayOffset < 0)
                {
                    b.DayDiffBadge.Visibility = Visibility.Visible;
                    b.DayDiffBadge.Background = minusDayBg;
                    b.DayDiffText.Foreground = minusDayFg;
                    b.DayDiffText.Text = $"{r.DayOffset}d";
                }
                else
                {
                    b.DayDiffBadge.Visibility = Visibility.Collapsed;
                }

                // Category badge
                switch (r.SlotCategory)
                {
                    case TimeSlotCategory.Business:
                        b.StatusBadge.Background = bizBg;
                        b.StatusBadge.BorderBrush = bizBorder;
                        b.StatusText.Text = "Comercial";
                        b.StatusText.Foreground = bizFg;
                        break;
                    case TimeSlotCategory.Shoulder:
                        b.StatusBadge.Background = shoulderBg;
                        b.StatusBadge.BorderBrush = shoulderBorder;
                        b.StatusText.Text = "Estendido";
                        b.StatusText.Foreground = shoulderFg;
                        break;
                    case TimeSlotCategory.Night:
                        b.StatusBadge.Background = nightBg;
                        b.StatusBadge.BorderBrush = nightBorder;
                        b.StatusText.Text = "Madrugada";
                        b.StatusText.Foreground = nightFg;
                        break;
                }
            }
        }

        // Build City Cards
        foreach (var city in cities)
        {
            var isReference = string.Equals(city.Zone, refZone, StringComparison.OrdinalIgnoreCase);

            var cardBorder = new Border
            {
                Background = cardBg,
                BorderBrush = isReference ? accent : cardBorderBrush,
                BorderThickness = new Thickness(isReference ? 1.5 : 1),
                CornerRadius = new CornerRadius(6 * scale),
                Padding = new Thickness(10 * scale, 7 * scale, 8 * scale, 7 * scale),
                Margin = new Thickness(0, 0, 0, 5 * scale)
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) }); // City info
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) }); // Time & Day
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) }); // Status badge
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Actions

            // Col 0: City Name & Offset
            var cityInfoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var cityNameStack = new StackPanel { Orientation = Orientation.Horizontal };
            cityNameStack.Children.Add(new TextBlock
            {
                Text = city.City,
                FontFamily = DisplayFont,
                FontSize = 13 * scale,
                FontWeight = FontWeights.SemiBold,
                Foreground = textPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (isReference)
            {
                cityNameStack.Children.Add(new Border
                {
                    Background = accent,
                    CornerRadius = new CornerRadius(3 * scale),
                    Padding = new Thickness(4 * scale, 1 * scale, 4 * scale, 1 * scale),
                    Margin = new Thickness(5 * scale, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "Base",
                        FontSize = 9 * scale,
                        FontWeight = FontWeights.Bold,
                        Foreground = accentContrast
                    }
                });
            }
            cityInfoStack.Children.Add(cityNameStack);

            var subtitleText = new TextBlock
            {
                FontFamily = TextFont,
                FontSize = 11 * scale,
                Foreground = textMuted,
                Margin = new Thickness(0, 2 * scale, 0, 0)
            };
            cityInfoStack.Children.Add(subtitleText);
            Grid.SetColumn(cityInfoStack, 0);
            cardGrid.Children.Add(cityInfoStack);

            // Col 1: Time and Day difference
            var timeStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4 * scale, 0, 4 * scale, 0)
            };
            var rowTimeText = new TextBlock
            {
                FontFamily = DisplayFont,
                FontSize = 16 * scale,
                FontWeight = FontWeights.Bold,
                Foreground = textPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            timeStack.Children.Add(rowTimeText);

            var dayBadge = new Border
            {
                CornerRadius = new CornerRadius(4 * scale),
                Padding = new Thickness(4 * scale, 1 * scale, 4 * scale, 1 * scale),
                Margin = new Thickness(6 * scale, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            var dayText = new TextBlock
            {
                FontFamily = TextFont,
                FontSize = 10 * scale,
                FontWeight = FontWeights.Bold
            };
            dayBadge.Child = dayText;
            timeStack.Children.Add(dayBadge);
            Grid.SetColumn(timeStack, 1);
            cardGrid.Children.Add(timeStack);

            // Col 2: Feasibility Slot Badge
            var slotBadge = new Border
            {
                CornerRadius = new CornerRadius(4 * scale),
                Padding = new Thickness(6 * scale, 2 * scale, 6 * scale, 2 * scale),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var slotText = new TextBlock
            {
                FontFamily = TextFont,
                FontSize = 10.5 * scale,
                FontWeight = FontWeights.SemiBold
            };
            slotBadge.Child = slotText;
            Grid.SetColumn(slotBadge, 2);
            cardGrid.Children.Add(slotBadge);

            // Col 3: Action Buttons (Swap reference & Remove)
            var cardActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4 * scale, 0, 0, 0)
            };

            if (!isReference)
            {
                var makeRefBtn = CreateIconButton("\uE734", "Tornar cidade de referência", scale, dark ? "#334155" : "#E2E8F0", textMuted);
                var currentCityId = city.Id;
                makeRefBtn.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await context.ExecuteAsync(new PluginCommand("swap-reference", new Dictionary<string, string> { ["id"] = currentCityId }));
                };
                cardActions.Children.Add(makeRefBtn);
            }

            if (cities.Count > 1)
            {
                var removeBtn = CreateIconButton("\uE711", "Remover cidade", scale, dark ? "#334155" : "#E2E8F0", textSubtle);
                removeBtn.Margin = new Thickness(4 * scale, 0, 0, 0);
                var currentCityId = city.Id;
                removeBtn.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await context.ExecuteAsync(new PluginCommand("remove-zone", new Dictionary<string, string> { ["id"] = currentCityId }));
                };
                cardActions.Children.Add(removeBtn);
            }

            Grid.SetColumn(cardActions, 3);
            cardGrid.Children.Add(cardActions);

            cardBorder.Child = cardGrid;
            cityCardsStack.Children.Add(cardBorder);

            uiBindings.Add(new CityRowUiBinding
            {
                City = city,
                TimeText = rowTimeText,
                SubtitleText = subtitleText,
                DayDiffBadge = dayBadge,
                DayDiffText = dayText,
                StatusBadge = slotBadge,
                StatusText = slotText,
                CardBorder = cardBorder
            });
        }

        listScrollViewer.Content = cityCardsStack;
        Grid.SetRow(listScrollViewer, 2);
        root.Children.Add(listScrollViewer);

        // ---------------------------------------------------------
        // ROW 3: Quick Add Suggestions
        // ---------------------------------------------------------
        var presetsContainer = new StackPanel { Margin = new Thickness(0, 4 * scale, 0, 0) };
        var presetsScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Tag = "plugin-interactive"
        };
        var presetsPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var suggestions = new (string id, string city, string zone)[]
        {
            ("sp", "São Paulo", "America/Sao_Paulo"),
            ("nyc", "Nova York", "America/New_York"),
            ("lon", "Londres", "Europe/London"),
            ("par", "Paris", "Europe/Paris"),
            ("tyo", "Tóquio", "Asia/Tokyo"),
            ("syd", "Sydney", "Australia/Sydney"),
            ("lax", "Los Angeles", "America/Los_Angeles"),
            ("dxb", "Dubai", "Asia/Dubai"),
            ("del", "Nova Déli", "Asia/Kolkata"),
            ("ber", "Berlim", "Europe/Berlin")
        };

        var availableSuggestions = suggestions.Where(s => !cities.Any(c => string.Equals(c.Zone, s.zone, StringComparison.OrdinalIgnoreCase))).ToList();

        if (availableSuggestions.Count > 0)
        {
            var addLabel = new TextBlock
            {
                Text = "+ Adicionar:",
                FontFamily = TextFont,
                FontSize = 11 * scale,
                Foreground = textSubtle,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6 * scale, 0)
            };
            presetsPanel.Children.Add(addLabel);

            foreach (var sugg in availableSuggestions.Take(5))
            {
                var chip = CreateChip($"+ {sugg.city}", dark ? "#1E293B" : "#F1F5F9", textPrimary, scale);
                chip.Margin = new Thickness(0, 0, 5 * scale, 0);
                var targetSugg = sugg;
                chip.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await context.ExecuteAsync(new PluginCommand("add-zone", new Dictionary<string, string>
                    {
                        ["id"] = targetSugg.id,
                        ["city"] = targetSugg.city,
                        ["zone"] = targetSugg.zone
                    }));
                };
                presetsPanel.Children.Add(chip);
            }
        }

        var dropdownOptions = availableSuggestions.Select(s => new PluginDropdownOption($"{s.id}|{s.city}|{s.zone}", $"{s.city} ({s.zone})", s.city)).ToList();
        if (dropdownOptions.Count > 0)
        {
            var addZoneDropdown = PluginDropdowns.Create(context, dropdownOptions, "", async selected =>
            {
                var parts = selected.Split('|');
                if (parts.Length == 3)
                {
                    await context.ExecuteAsync(new PluginCommand("add-zone", new Dictionary<string, string>
                    {
                        ["id"] = parts[0],
                        ["city"] = parts[1],
                        ["zone"] = parts[2]
                    }));
                }
            }, "Buscar fuso para adicionar…");
            addZoneDropdown.Margin = new Thickness(0, 0, 0, 4 * scale);
            presetsContainer.Children.Add(addZoneDropdown);
        }

        presetsScroll.Content = presetsPanel;
        presetsContainer.Children.Add(presetsScroll);
        Grid.SetRow(presetsContainer, 3);
        root.Children.Add(presetsContainer);

        // Initial UI population
        RefreshUi(minuteOfDay);

        // ---------------------------------------------------------
        // Event Handlers & Slider Real-Time Dragging
        // ---------------------------------------------------------
        var isUpdatingInternally = false;
        slider.ValueChanged += (newVal) =>
        {
            if (isUpdatingInternally) return;

            var val = (int)Math.Round(newVal);
            val = Math.Clamp(val, 0, 1439);

            RefreshUi(val);

            // Execute command without rebuilding visual tree to avoid flicker
            _ = context.ExecuteAsync(new PluginCommand("set-reference-time", new Dictionary<string, string>
            {
                ["minuteOfDay"] = val.ToString(CultureInfo.InvariantCulture)
            }), rebuild: false, recordUndo: false);
        };

        // "Agora" button click: sync to live current time
        nowBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            var tz = TimezoneEngine.ResolveTimeZone(refZone);
            var local = TimeZoneInfo.ConvertTime(context.Execution.UtcNow, tz);
            var currentMinute = (local.Hour * 60) + local.Minute;

            isUpdatingInternally = true;
            slider.Value = currentMinute;
            isUpdatingInternally = false;

            RefreshUi(currentMinute);

            await context.ExecuteAsync(new PluginCommand("set-current-time"));
        };

        // Copy icon button click
        copyIconBtn.Click += (_, e) =>
        {
            e.Handled = true;
            var eval = TimezoneEngine.Evaluate(context.Execution.UtcNow, refZone, (int)slider.Value, cities);
            var text = TimezoneEngine.FormatProposalText(eval, refCity);
            if (!string.IsNullOrEmpty(text))
            {
                context.RequestHostAction(new PluginHostAction(PluginHostActionKind.CopyToClipboard, text));
                context.RequestHostAction(new PluginHostAction(PluginHostActionKind.ShowMessage, "Proposta de reunião copiada!"));
            }

            // Transient visual feedback
            if (copyIconBtn.Content is TextBlock tb) tb.Text = "\uE73E"; // Checkmark
            var timerFeedback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timerFeedback.Tick += (_, _) =>
            {
                if (copyIconBtn.Content is TextBlock textBlock) textBlock.Text = "\uE8C8";
                timerFeedback.Stop();
            };
            timerFeedback.Start();
        };

        // Live mode DispatcherTimer
        DispatcherTimer? liveTimer = null;
        if (isLive)
        {
            liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            liveTimer.Tick += (_, _) =>
            {
                var tz = TimezoneEngine.ResolveTimeZone(refZone);
                var local = TimeZoneInfo.ConvertTime(context.Execution.UtcNow, tz);
                var curMin = (local.Hour * 60) + local.Minute;
                if ((int)slider.Value != curMin)
                {
                    isUpdatingInternally = true;
                    slider.Value = curMin;
                    isUpdatingInternally = false;
                    RefreshUi(curMin);
                }
            };
            liveTimer.Start();
        }

        // Memory cleanup: Stop DispatcherTimer on Unloaded
        root.Unloaded += (_, _) =>
        {
            liveTimer?.Stop();
        };

        return root;
    }

    private static string FormatMinute(int minuteOfDay)
    {
        minuteOfDay = Math.Clamp(minuteOfDay, 0, 1439);
        var h = minuteOfDay / 60;
        var m = minuteOfDay % 60;
        return $"{h:D2}:{m:D2}";
    }

    private static Button CreateButton(string label, object bg, Brush fg, double scale) => new()
    {
        Content = label,
        Background = bg is string s ? GetBrush(s) : (Brush)bg,
        Foreground = fg,
        FontFamily = TextFont,
        FontSize = 12 * scale,
        FontWeight = FontWeights.SemiBold,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(10 * scale, 5 * scale, 10 * scale, 5 * scale),
        MinHeight = 28 * scale,
        Tag = "plugin-interactive",
        Cursor = Cursors.Hand
    };

    private static Button CreateChip(string label, string bgHex, Brush fg, double scale) => new()
    {
        Content = label,
        Background = GetBrush(bgHex),
        Foreground = fg,
        FontFamily = TextFont,
        FontSize = 10.5 * scale,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(8 * scale, 3 * scale, 8 * scale, 3 * scale),
        Tag = "plugin-interactive",
        Cursor = Cursors.Hand
    };

    private static Button CreateIconButton(string iconGlyph, string tooltip, double scale, string bgHex, Brush fg) => new()
    {
        Content = new TextBlock
        {
            Text = iconGlyph,
            FontFamily = IconFont,
            FontSize = 11 * scale,
            Foreground = fg,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        },
        Background = GetBrush(bgHex),
        BorderThickness = new Thickness(0),
        Width = 24 * scale,
        Height = 24 * scale,
        ToolTip = tooltip,
        Tag = "plugin-interactive",
        Cursor = Cursors.Hand
    };

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
}
