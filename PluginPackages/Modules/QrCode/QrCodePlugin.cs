using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.QrCode;

public sealed class QrCodePlugin : IWindowsPluginRenderer
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI, sans-serif");

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var foreground = GetBrush(context.IsDarkMode ? "#F0F6FC" : "#1F2328");
        var muted = GetBrush(context.IsDarkMode ? "#8B949E" : "#656D76");
        var cardSurface = GetBrush(context.IsDarkMode ? "#161B22" : "#F6F8FA");
        var inputSurface = GetBrush(context.IsDarkMode ? "#0D1117" : "#FFFFFF");
        var borderBrush = GetBrush(context.IsDarkMode ? "#30363D" : "#D0D7DE");
        var accent = GetBrush(context.AccentColor, "#10B981");
        var accentForeground = GetContrastBrush(context.AccentColor);

        var root = new Grid
        {
            Margin = new Thickness(10 * context.Scale),
            Background = Brushes.Transparent
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Top Bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: QR Code Card
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Input Box / Text Editor
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Expandable Options (ECC, etc.)
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Action Buttons

        string currentText = context.State.GetString("text") ?? string.Empty;
        string currentEcc = context.State.GetString("ecc") ?? QrCodeModule.DefaultEcc;
        string selectedEcc = currentEcc;
        bool isUrl = IsWebUrl(currentText);

        // --- 0. Top Bar (Tipo de conteúdo + Botão Opções/Config) ---
        var topBar = new Grid { Margin = new Thickness(0, 0, 0, 8 * context.Scale) };
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            Background = isUrl ? GetAlphaBrush(context.AccentColor, 0.15) : cardSurface,
            BorderBrush = isUrl ? accent : borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8 * context.Scale, 3 * context.Scale, 10 * context.Scale, 3 * context.Scale)
        };

        var badgeContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var badgeIcon = new TextBlock
        {
            Text = isUrl ? "\ue8a7" : "\ue8c1",
            FontFamily = IconFont,
            FontSize = Math.Clamp(11 * context.Scale, 10, 16),
            Foreground = isUrl ? accent : muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5 * context.Scale, 0)
        };
        var badgeText = new TextBlock
        {
            Text = isUrl ? "Link Web" : "Texto",
            FontFamily = TextFont,
            FontSize = Math.Clamp(11 * context.Scale, 10, 16),
            Foreground = isUrl ? accent : muted,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        badgeContent.Children.Add(badgeIcon);
        badgeContent.Children.Add(badgeText);
        badge.Child = badgeContent;
        Grid.SetColumn(badge, 0);
        topBar.Children.Add(badge);

        var eccIndicator = new TextBlock
        {
            Text = $"ECC: {selectedEcc}",
            FontFamily = TextFont,
            FontSize = Math.Clamp(11 * context.Scale, 9, 15),
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8 * context.Scale, 0, 0, 0)
        };
        Grid.SetColumn(eccIndicator, 1);
        topBar.Children.Add(eccIndicator);

        var toggleOptionsBtn = CreateIconButton("\ue712", "Opções avançadas de ECC", cardSurface, foreground, context.Scale, borderBrush);
        Grid.SetColumn(toggleOptionsBtn, 2);
        topBar.Children.Add(toggleOptionsBtn);

        Grid.SetRow(topBar, 0);
        root.Children.Add(topBar);

        // --- 1. QR Code Plate (Centro responsivo com animação) ---
        var qrCard = new Border
        {
            Background = Brushes.White,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10 * context.Scale),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = context.IsDarkMode ? 0.40 : 0.09,
                Color = Colors.Black
            }
        };

        var qrImage = new Image
        {
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            MaxHeight = Math.Max(70, 180 * context.Scale),
            MaxWidth = Math.Max(70, 180 * context.Scale),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(qrImage, BitmapScalingMode.NearestNeighbor);
        qrCard.Child = qrImage;

        void RenderQr(string textToRender, string eccToUse)
        {
            if (string.IsNullOrWhiteSpace(textToRender))
            {
                qrImage.Source = null;
                return;
            }

            try
            {
                var qrData = QrCodeEncoder.EncodeText(textToRender, QrCodeModule.ParseEccLevel(eccToUse));
                qrImage.Source = CreateQrBitmap(qrData);

                // Animação de entrada suave ao atualizar
                var fadeAnim = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(200));
                qrCard.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                if (qrCard.RenderTransform is ScaleTransform st)
                {
                    var scaleAnim = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                    st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                }
            }
            catch
            {
                qrImage.Source = null;
            }
        }

        RenderQr(currentText, selectedEcc);

        Grid.SetRow(qrCard, 1);
        root.Children.Add(qrCard);

        // --- 2. Input Box Interativo (Permite alterar o link ou texto diretamente no card!) ---
        var inputCard = new Border
        {
            Background = inputSurface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6 * context.Scale, 3 * context.Scale, 6 * context.Scale, 3 * context.Scale),
            Margin = new Thickness(0, 8 * context.Scale, 0, 6 * context.Scale)
        };

        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var input = new TextBox
        {
            Text = currentText,
            Background = Brushes.Transparent,
            Foreground = foreground,
            BorderThickness = new Thickness(0),
            FontFamily = TextFont,
            FontSize = Math.Clamp(12 * context.Scale, 11, 20),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4 * context.Scale),
            Tag = "plugin-interactive"
        };
        Grid.SetColumn(input, 0);
        inputGrid.Children.Add(input);

        // Botão para aplicar/atualizar
        var applyBtn = PluginButtons.Create(context, "Atualizar", primary: true);
        applyBtn.Margin = new Thickness(4 * context.Scale, 0, 2 * context.Scale, 0);
        applyBtn.Visibility = Visibility.Collapsed;
        Grid.SetColumn(applyBtn, 1);
        inputGrid.Children.Add(applyBtn);

        // Ícone acessível de cópia
        var copyIconBtn = PluginButtons.Create(context, "Copiar");
        copyIconBtn.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(14 * context.Scale, 12, 20)
        };
        copyIconBtn.MinWidth = 28 * context.Scale;
        copyIconBtn.MinHeight = 28 * context.Scale;
        copyIconBtn.Padding = new Thickness(4 * context.Scale, 2 * context.Scale, 4 * context.Scale, 2 * context.Scale);
        copyIconBtn.ToolTip = "Copiar conteúdo";
        AutomationProperties.SetName(copyIconBtn, "Copiar conteúdo");
        copyIconBtn.IsEnabled = !string.IsNullOrWhiteSpace(currentText);
        copyIconBtn.Margin = new Thickness(2 * context.Scale, 0, 0, 0);
        copyIconBtn.Click += (_, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(input.Text))
            {
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, input.Text));
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Copiado para a área de transferência!"));
            }
        };
        Grid.SetColumn(copyIconBtn, 2);
        inputGrid.Children.Add(copyIconBtn);

        inputCard.Child = inputGrid;
        Grid.SetRow(inputCard, 2);
        root.Children.Add(inputCard);

        // --- 3. Painel de Opções Avançadas (ECC) ---
        var optionsPanel = new Border
        {
            Background = cardSurface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8 * context.Scale),
            Margin = new Thickness(0, 0, 0, 8 * context.Scale),
            Visibility = Visibility.Collapsed
        };

        var optionsStack = new StackPanel();
        var optionsTitle = new TextBlock
        {
            Text = "Nível de correção de erro (ECC):",
            FontFamily = TextFont,
            FontSize = Math.Clamp(11 * context.Scale, 10, 16),
            Foreground = muted,
            Margin = new Thickness(0, 0, 0, 6 * context.Scale)
        };
        optionsStack.Children.Add(optionsTitle);

        var eccToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("L", "L (7%)"),
            new PluginToggleOption("M", "M (15%)"),
            new PluginToggleOption("Q", "Q (25%)"),
            new PluginToggleOption("H", "H (30%)")
        }, selectedEcc, async newEcc =>
        {
            selectedEcc = newEcc;
            eccIndicator.Text = $"ECC: {selectedEcc}";
            RenderQr(input.Text, selectedEcc);

            await context.ExecuteAsync(new PluginCommand("set-ecc", new Dictionary<string, string>
            {
                ["value"] = selectedEcc
            }), rebuild: false);
        });

        optionsStack.Children.Add(eccToggle);
        optionsPanel.Child = optionsStack;
        Grid.SetRow(optionsPanel, 3);
        root.Children.Add(optionsPanel);

        toggleOptionsBtn.Click += (_, e) =>
        {
            e.Handled = true;
            optionsPanel.Visibility = optionsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        };

        // --- 4. Ações Principais (Abrir Link, Limpar) ---
        var actionsGrid = new Grid();
        if (isUrl)
        {
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8 * context.Scale) });
            actionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var openBtn = PluginButtons.Create(context, "Abrir Link", primary: true);
            openBtn.Click += (_, e) =>
            {
                e.Handled = true;
                context.RequestHostAction(new(PluginHostActionKind.OpenUri, input.Text));
            };
            Grid.SetColumn(openBtn, 0);
            actionsGrid.Children.Add(openBtn);

            var clearBtn = PluginButtons.Create(context, "Limpar", primary: false);
            clearBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                input.Text = string.Empty;
                RenderQr(string.Empty, selectedEcc);
                await context.ExecuteAsync(new PluginCommand("clear"), rebuild: true);
            };
            Grid.SetColumn(clearBtn, 2);
            actionsGrid.Children.Add(clearBtn);
        }
        else
        {
            var clearBtn = PluginButtons.Create(context, "Limpar", primary: false);
            clearBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                input.Text = string.Empty;
                RenderQr(string.Empty, selectedEcc);
                await context.ExecuteAsync(new PluginCommand("clear"), rebuild: true);
            };
            actionsGrid.Children.Add(clearBtn);
        }

        Grid.SetRow(actionsGrid, 4);
        root.Children.Add(actionsGrid);

        // Salvar alterações ao digitar / alterar o texto
        async Task ApplyTextChangeAsync()
        {
            string newText = input.Text.Trim();
            if (string.Equals(newText, currentText, StringComparison.Ordinal)) return;

            RenderQr(newText, selectedEcc);
            currentText = newText;
            isUrl = IsWebUrl(newText);
            badgeIcon.Text = isUrl ? "\ue8a7" : "\ue8c1";
            badgeText.Text = isUrl ? "Link Web" : "Texto";
            badgeText.Foreground = isUrl ? accent : muted;
            badgeIcon.Foreground = isUrl ? accent : muted;
            badge.Background = isUrl ? GetAlphaBrush(context.AccentColor, 0.15) : cardSurface;
            badge.BorderBrush = isUrl ? accent : borderBrush;

            applyBtn.Visibility = Visibility.Collapsed;

            var result = await context.ExecuteAsync(new PluginCommand("set-text", new Dictionary<string, string>
            {
                ["value"] = newText
            }), rebuild: true);

            if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, result.Message));
            }
        }

        applyBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await ApplyTextChangeAsync();
        };

        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ApplyTextChangeAsync();
            }
        };

        input.TextChanged += (_, _) =>
        {
            copyIconBtn.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
            if (!string.Equals(input.Text.Trim(), currentText, StringComparison.Ordinal))
            {
                applyBtn.Visibility = Visibility.Visible;
            }
            else
            {
                applyBtn.Visibility = Visibility.Collapsed;
            }
        };

        input.LostFocus += async (_, _) =>
        {
            await ApplyTextChangeAsync();
        };

        void UpdateSpacing()
        {
            var isCompact = (root.ActualWidth > 0 && root.ActualWidth < 260) || (root.ActualHeight > 0 && root.ActualHeight < 260);
            root.Margin = new Thickness(isCompact ? 6 * context.Scale : 10 * context.Scale);
            topBar.Margin = new Thickness(0, 0, 0, isCompact ? 4 * context.Scale : 8 * context.Scale);
            inputCard.Margin = new Thickness(0, isCompact ? 4 * context.Scale : 8 * context.Scale, 0, isCompact ? 4 * context.Scale : 6 * context.Scale);
        }

        root.SizeChanged += (_, _) => UpdateSpacing();
        context.LayoutChanged += UpdateSpacing;

        return root;
    }

    #region Helpers Gráficos, Tipografia e Animações



    private static Button CreateIconButton(
        string glyph,
        string tooltip,
        Brush background,
        Brush foreground,
        double scale,
        Brush border)
    {
        var btn = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = IconFont,
                FontSize = Math.Clamp(12 * scale, 11, 18),
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center
            },
            ToolTip = tooltip,
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Width = Math.Max(26, 28 * scale),
            Height = Math.Max(26, 28 * scale),
            Padding = new Thickness(0),
            Tag = "plugin-interactive",
            Cursor = Cursors.Hand
        };
        return btn;
    }



    private static BitmapSource CreateQrBitmap(QrCodeData qr)
    {
        int qrSize = qr.Size;
        int quietZone = 4;
        int totalSize = qrSize + (quietZone * 2);
        int stride = totalSize;
        byte[] pixels = new byte[totalSize * totalSize];

        Array.Fill(pixels, (byte)255);

        for (int y = 0; y < qrSize; y++)
        {
            for (int x = 0; x < qrSize; x++)
            {
                if (qr.GetModule(x, y))
                {
                    int index = ((y + quietZone) * totalSize) + (x + quietZone);
                    pixels[index] = 0;
                }
            }
        }

        var bitmap = BitmapSource.Create(
            totalSize, totalSize,
            96, 96,
            PixelFormats.Gray8,
            null,
            pixels,
            stride);

        bitmap.Freeze();
        return bitmap;
    }

    private static bool IsWebUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        return (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
               Uri.TryCreate(text, UriKind.Absolute, out _);
    }

    private static Brush GetBrush(string color, string fallback = "#000000")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

    private static Brush GetAlphaBrush(string color, double opacity)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color);
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 16, 185, 129));
        }
    }

    private static Brush GetContrastBrush(string color)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            var luminance = (0.299 * parsed.R) + (0.587 * parsed.G) + (0.114 * parsed.B);
            return GetBrush(luminance > 160 ? "#111827" : "#FFFFFF");
        }
        catch { return Brushes.White; }
    }

    #endregion
}
