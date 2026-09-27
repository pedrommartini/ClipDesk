using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Stopwatch;

public sealed class StopwatchPlugin : IWindowsPluginRenderer
{
    private const string InteractiveTag = "plugin-interactive";

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var isDark = context.IsDarkMode;
        var scale = Math.Clamp(context.Scale, 0.75, 2.0);

        var bg = GetBrush(isDark ? "#0F172A" : "#FFFFFF");
        var surface = GetBrush(isDark ? "#1E293B" : "#F8FAFC");
        var surfaceCard = GetBrush(isDark ? "#162032" : "#F1F5F9");
        var border = GetBrush(isDark ? "#334155" : "#E2E8F0");
        var borderSubtle = GetBrush(isDark ? "#243247" : "#EDF2F7");
        var fg = GetBrush(isDark ? "#F8FAFC" : "#0F172A");
        var muted = GetBrush(isDark ? "#94A3B8" : "#64748B");
        var accent = GetBrush(context.AccentColor, "#3B82F6");
        var accentForeground = GetContrastBrush(context.AccentColor);

        var emerald = GetBrush("#10B981");
        var emeraldBg = GetBrush(isDark ? "#064E3B" : "#D1FAE5");
        var emeraldFg = GetBrush(isDark ? "#6EE7B7" : "#065F46");

        var amber = GetBrush("#F59E0B");
        var amberBg = GetBrush(isDark ? "#78350F" : "#FEF3C7");
        var amberFg = GetBrush(isDark ? "#FCD34D" : "#92400E");

        var status = context.State.GetString(StopwatchStateKeys.Status) ?? StopwatchStatus.Idle;

        var root = new Border
        {
            Background = bg,
            Padding = new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale)
        };

        void UpdateSpacing()
        {
            root.Padding = (context.Width < 280 || root.ActualWidth is > 0 and < 280)
                ? new Thickness(6 * scale, 5 * scale, 6 * scale, 5 * scale)
                : new Thickness(12 * scale, 9 * scale, 12 * scale, 10 * scale);
        }
        root.SizeChanged += (_, _) => UpdateSpacing();
        context.LayoutChanged += UpdateSpacing;
        UpdateSpacing();

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Status badge
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Digital Display
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Action Controls
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3: Laps Table

        // -------------------------------------------------------------
        // Row 0: Status Badge (NO duplicate title)
        // -------------------------------------------------------------
        var statusBadge = new Border
        {
            Margin = new Thickness(0, 0, 0, 6 * scale),
            Padding = new Thickness(8 * scale, 2 * scale, 8 * scale, 2 * scale),
            CornerRadius = new CornerRadius(10 * scale),
            Background = status switch
            {
                StopwatchStatus.Running => emeraldBg,
                StopwatchStatus.Paused => amberBg,
                _ => surfaceCard
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = status switch
            {
                StopwatchStatus.Running => "Em execução",
                StopwatchStatus.Paused => "Pausado",
                _ => "Pronto"
            },
            Foreground = status switch
            {
                StopwatchStatus.Running => emeraldFg,
                StopwatchStatus.Paused => amberFg,
                _ => muted
            },
            FontSize = Math.Clamp(10.5 * scale, 9, 14),
            FontWeight = FontWeights.SemiBold
        };
        statusBadge.Child = statusText;

        Grid.SetRow(statusBadge, 0);
        grid.Children.Add(statusBadge);

        // -------------------------------------------------------------
        // Row 1: Fluid Digital Display with accessible Copy Icon
        // -------------------------------------------------------------
        var initialElapsedMs = StopwatchModule.GetElapsedMilliseconds(context.State, DateTimeOffset.UtcNow);
        var initialTotalSecs = initialElapsedMs / 1000;
        var initialHours = initialTotalSecs / 3600;
        var initialMins = (initialTotalSecs % 3600) / 60;
        var initialSecs = initialTotalSecs % 60;
        var initialCentis = (initialElapsedMs % 1000) / 10;

        var displayCard = new Border
        {
            Background = surfaceCard,
            BorderBrush = borderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(10 * scale, 8 * scale, 10 * scale, 8 * scale),
            Margin = new Thickness(0, 0, 0, 8 * scale)
        };

        var displayStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var displayFont = new FontFamily("Consolas, Segoe UI Variable Display, Segoe UI");

        var timeMain = new TextBlock
        {
            Text = $"{initialHours:D2}:{initialMins:D2}:{initialSecs:D2}",
            FontFamily = displayFont,
            FontSize = Math.Clamp(34 * scale, 24, 46),
            FontWeight = FontWeights.Bold,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center
        };

        var timeCentis = new TextBlock
        {
            Text = $".{initialCentis:D2}",
            FontFamily = displayFont,
            FontSize = Math.Clamp(22 * scale, 16, 32),
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2 * scale, 0, 0, 4 * scale)
        };

        var copyBtn = PluginButtons.Create(context, "Copiar");
        copyBtn.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(14 * scale, 12, 22)
        };
        copyBtn.MinWidth = 28 * scale;
        copyBtn.MinHeight = 28 * scale;
        copyBtn.ToolTip = "Copiar tempo atual";
        AutomationProperties.SetName(copyBtn, "Copiar tempo atual");
        copyBtn.IsEnabled = initialElapsedMs > 0;
        copyBtn.VerticalAlignment = VerticalAlignment.Center;
        copyBtn.Margin = new Thickness(8 * scale, 0, 0, 0);
        copyBtn.Click += (_, e) =>
        {
            e.Handled = true;
            var summary = context.Module.GetClipboardText(context.State);
            if (!string.IsNullOrEmpty(summary))
            {
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, summary));
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Tempo copiado!"));
            }
        };

        displayStack.Children.Add(timeMain);
        displayStack.Children.Add(timeCentis);
        displayStack.Children.Add(copyBtn);
        displayCard.Child = displayStack;

        Grid.SetRow(displayCard, 1);
        grid.Children.Add(displayCard);

        // -------------------------------------------------------------
        // Row 2: Action Controls
        // -------------------------------------------------------------
        var actionsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8 * scale)
        };

        var lapsList = StopwatchModule.ParseLaps(context.State.GetString(StopwatchStateKeys.Laps));

        if (status == StopwatchStatus.Idle)
        {
            var startBtn = PluginButtons.Create(context, "▶  Iniciar", primary: true);
            startBtn.MinWidth = 130 * scale;
            startBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand(StopwatchCommands.Start));
            };
            actionsPanel.Children.Add(startBtn);
        }
        else if (status == StopwatchStatus.Running)
        {
            var lapBtn = PluginButtons.Create(context, "⚐  Volta", primary: false);
            lapBtn.MinWidth = 95 * scale;
            lapBtn.Margin = new Thickness(0, 0, 8 * scale, 0);
            lapBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand(StopwatchCommands.RecordLap));
            };

            var pauseBtn = PluginButtons.Create(context, "⏸  Pausar", primary: true);
            pauseBtn.MinWidth = 95 * scale;
            pauseBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand(StopwatchCommands.Pause));
            };

            actionsPanel.Children.Add(lapBtn);
            actionsPanel.Children.Add(pauseBtn);
        }
        else // Paused
        {
            var resetBtn = PluginButtons.Create(context, "↺  Zerar", primary: false);
            resetBtn.MinWidth = 85 * scale;
            resetBtn.Margin = new Thickness(0, 0, 6 * scale, 0);
            resetBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand(StopwatchCommands.Reset));
            };

            var resumeBtn = PluginButtons.Create(context, "▶  Continuar", primary: true);
            resumeBtn.MinWidth = 100 * scale;
            resumeBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await context.ExecuteAsync(new PluginCommand(StopwatchCommands.Resume));
            };

            actionsPanel.Children.Add(resetBtn);
            actionsPanel.Children.Add(resumeBtn);

            if (lapsList.Count > 0)
            {
                var clearLapsBtn = PluginButtons.Create(context, "Limpar voltas", primary: false);
                clearLapsBtn.MinWidth = 85 * scale;
                clearLapsBtn.Margin = new Thickness(6 * scale, 0, 0, 0);
                clearLapsBtn.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await context.ExecuteAsync(new PluginCommand(StopwatchCommands.ClearLaps));
                };
                actionsPanel.Children.Add(clearLapsBtn);
            }
        }

        Grid.SetRow(actionsPanel, 2);
        grid.Children.Add(actionsPanel);

        // -------------------------------------------------------------
        // Row 3: Laps Table with ScrollViewer
        // -------------------------------------------------------------
        var lapsContainer = new Border
        {
            Background = surface,
            BorderBrush = borderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8 * scale)
        };

        var lapsGrid = new Grid();
        lapsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header row
        lapsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Scroll content

        // Table Header
        var tableHeader = new Border
        {
            Background = surfaceCard,
            BorderBrush = borderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10 * scale, 6 * scale, 10 * scale, 6 * scale),
            CornerRadius = new CornerRadius(7 * scale, 7 * scale, 0, 0)
        };

        var headerCols = new Grid();
        headerCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38 * scale, GridUnitType.Pixel) });
        headerCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 * scale, GridUnitType.Pixel) });
        headerCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 * scale, GridUnitType.Pixel) });
        headerCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        headerCols.Children.Add(CreateHeaderLabel("#", muted, scale, 0));
        headerCols.Children.Add(CreateHeaderLabel("Volta (Split)", muted, scale, 1));
        headerCols.Children.Add(CreateHeaderLabel("Total", muted, scale, 2));
        headerCols.Children.Add(CreateHeaderLabel("Destaque", muted, scale, 3));

        tableHeader.Child = headerCols;
        Grid.SetRow(tableHeader, 0);
        lapsGrid.Children.Add(tableHeader);

        // Table Scrollable Items
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Tag = InteractiveTag
        };

        var itemsStack = new StackPanel();

        if (lapsList.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "Nenhuma volta gravada.\nClique em 'Volta' enquanto o cronômetro estiver em execução.",
                Foreground = muted,
                FontSize = Math.Clamp(11.5 * scale, 10, 15),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(12 * scale, 24 * scale, 12 * scale, 24 * scale)
            };
            itemsStack.Children.Add(emptyText);
        }
        else
        {
            var (bestNum, worstNum) = StopwatchModule.GetBestAndWorstLapNumbers(lapsList);

            for (int i = 0; i < lapsList.Count; i++)
            {
                var lap = lapsList[i];
                var isEven = i % 2 == 1;

                var rowBorder = new Border
                {
                    Background = isEven ? surfaceCard : Brushes.Transparent,
                    BorderBrush = borderSubtle,
                    BorderThickness = new Thickness(0, 0, 0, i < lapsList.Count - 1 ? 1 : 0),
                    Padding = new Thickness(10 * scale, 6 * scale, 10 * scale, 6 * scale)
                };

                var rowCols = new Grid();
                rowCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38 * scale, GridUnitType.Pixel) });
                rowCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 * scale, GridUnitType.Pixel) });
                rowCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 * scale, GridUnitType.Pixel) });
                rowCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Lap Number
                var lapNumText = new TextBlock
                {
                    Text = $"#{lap.Number}",
                    Foreground = muted,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = Math.Clamp(11.5 * scale, 10, 15),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lapNumText, 0);
                rowCols.Children.Add(lapNumText);

                // Split time
                var splitText = new TextBlock
                {
                    Text = StopwatchModule.FormatTime(lap.SplitMs),
                    FontFamily = displayFont,
                    Foreground = fg,
                    FontWeight = FontWeights.Medium,
                    FontSize = Math.Clamp(12 * scale, 10.5, 16),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(splitText, 1);
                rowCols.Children.Add(splitText);

                // Total time
                var totalText = new TextBlock
                {
                    Text = StopwatchModule.FormatTime(lap.TotalMs),
                    FontFamily = displayFont,
                    Foreground = muted,
                    FontSize = Math.Clamp(11.5 * scale, 10, 15),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(totalText, 2);
                rowCols.Children.Add(totalText);

                // Badge
                if (lap.Number == bestNum)
                {
                    var bestBadge = CreateBadge("★ Melhor", emeraldBg, emeraldFg, scale);
                    Grid.SetColumn(bestBadge, 3);
                    rowCols.Children.Add(bestBadge);
                }
                else if (lap.Number == worstNum)
                {
                    var worstBadge = CreateBadge("▲ Pior", amberBg, amberFg, scale);
                    Grid.SetColumn(worstBadge, 3);
                    rowCols.Children.Add(worstBadge);
                }

                rowBorder.Child = rowCols;
                itemsStack.Children.Add(rowBorder);
            }

            // Auto-scroll to latest lap
            scrollViewer.Loaded += (_, _) => scrollViewer.ScrollToEnd();
        }

        scrollViewer.Content = itemsStack;
        Grid.SetRow(scrollViewer, 1);
        lapsGrid.Children.Add(scrollViewer);

        lapsContainer.Child = lapsGrid;
        Grid.SetRow(lapsContainer, 3);
        grid.Children.Add(lapsContainer);

        root.Child = grid;

        // -------------------------------------------------------------
        // DispatcherTimer & Lifecycle Cleanup
        // -------------------------------------------------------------
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };

        if (status == StopwatchStatus.Running)
        {
            timer.Tick += (_, _) =>
            {
                var elapsedMs = StopwatchModule.GetElapsedMilliseconds(context.State, DateTimeOffset.UtcNow);
                var totalSecs = elapsedMs / 1000;
                var h = totalSecs / 3600;
                var m = (totalSecs % 3600) / 60;
                var s = totalSecs % 60;
                var cs = (elapsedMs % 1000) / 10;
                timeMain.Text = $"{h:D2}:{m:D2}:{s:D2}";
                timeCentis.Text = $".{cs:D2}";
                copyBtn.IsEnabled = elapsedMs > 0;
            };
            timer.Start();
        }

        root.Unloaded += (_, _) =>
        {
            timer.Stop();
        };

        return root;
    }

    private static TextBlock CreateHeaderLabel(string text, Brush foreground, double scale, int column)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = Math.Clamp(10.5 * scale, 9, 14),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, column);
        return tb;
    }

    private static Border CreateBadge(string text, Brush background, Brush foreground, double scale)
    {
        var badge = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(4 * scale),
            Padding = new Thickness(6 * scale, 2 * scale, 6 * scale, 2 * scale),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = Math.Clamp(9.5 * scale, 8.5, 13),
            FontWeight = FontWeights.Bold
        };
        return badge;
    }

    private static Button CreateButton(
        string label,
        Brush background,
        Brush foreground,
        Brush? borderBrush = null,
        double cornerRadius = 6)
    {
        var btn = new Button
        {
            Content = label,
            Background = background,
            Foreground = foreground,
            BorderBrush = borderBrush ?? Brushes.Transparent,
            BorderThickness = new Thickness(borderBrush is not null ? 1 : 0),
            Tag = InteractiveTag,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = CreateButtonTemplate(cornerRadius)
        };
        return btn;
    }

    private static ControlTemplate CreateButtonTemplate(double cornerRadius)
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "bd";
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
        borderFactory.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(Button.Background))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
        borderFactory.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding(nameof(Button.BorderBrush))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
        borderFactory.SetBinding(Border.BorderThicknessProperty,
            new System.Windows.Data.Binding(nameof(Button.BorderThickness))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cp.SetBinding(ContentPresenter.MarginProperty,
            new System.Windows.Data.Binding(nameof(Button.Padding))
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });

        borderFactory.AppendChild(cp);
        template.VisualTree = borderFactory;
        return template;
    }

    private static Brush GetBrush(string? color, string fallback = "#000000")
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return ParseBrush(fallback, Brushes.Black);
        }
        return ParseBrush(color, ParseBrush(fallback, Brushes.Black));
    }

    private static Brush ParseBrush(string hex, Brush fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
        }
        catch
        {
            // fallback
        }
        return fallback;
    }

    private static Brush GetContrastBrush(string? color)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(color) && ColorConverter.ConvertFromString(color) is Color parsed)
            {
                var luminance = (0.299 * parsed.R) + (0.587 * parsed.G) + (0.114 * parsed.B);
                return GetBrush(luminance > 160 ? "#152033" : "#FFFFFF");
            }
        }
        catch
        {
            // fallback
        }
        return Brushes.White;
    }
}
