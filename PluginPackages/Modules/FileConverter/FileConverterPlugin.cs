using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDesk.Plugin.FileConverter.Engines;
using ClipDesk.Plugin.FileConverter.Models;
using ClipDesk.Plugin.FileConverter.Services;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using Microsoft.Win32;

namespace ClipDesk.Plugin.FileConverter;

/// <summary>
/// Renderizador oficial WPF v2 para o Conversor de Arquivos ClipDesk.
/// Implementa layout horizontal em 3 zonas (Entrada à esquerda, Opções no centro, Saída/Ação à direita),
/// botão de ação com barra de progresso integrada, e suporte nativo a drag-and-drop.
/// </summary>
public sealed class FileConverterPlugin : IWindowsPluginRenderer
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var root = new Grid { Tag = "plugin-interactive" };
        var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        bool started = false;
        root.Unloaded += (_, _) => { lifetime.Cancel(); };
        context.FilesDropped += async files =>
        {
            if (!token.IsCancellationRequested && files.Count > 0)
                await LoadSourceFileAsync(context, files[0].Path, token);
        };
        root.Children.Add(new TextBlock { Text = "Carregando arquivos compartilhados…", TextWrapping = TextWrapping.Wrap });
        root.Loaded += async (_, _) =>
        {
            if (started || token.IsCancellationRequested) return;
            started = true;
            try
            {
                var input = await WindowsSharedAssetFiles.MaterializeAsync(context, context.State.GetString("sourceAssetId"), token);
                var output = await WindowsSharedAssetFiles.MaterializeAsync(context, context.State.GetString("outputAssetId"), token);
                token.ThrowIfCancellationRequested();
                await root.Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    root.Children.Clear();
                    root.Children.Add(BuildBody(context, input ?? "", output ?? ""));
                });
            }
            catch (OperationCanceledException) { }
            catch
            {
                await root.Dispatcher.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    root.Children.Clear();
                    root.Children.Add(new TextBlock { Text = "Não foi possível abrir os arquivos compartilhados. Verifique a conexão da mesa.", TextWrapping = TextWrapping.Wrap });
                });
            }
        };
        return root;
    }

    private FrameworkElement BuildBody(WindowsPluginViewContext context, string sourcePath, string lastConvertedFile)
    {
        bool dark = context.IsDarkMode;
        double scale = Math.Max(1.0, context.Scale);

        var foreground = GetBrush(dark ? "#F8FAFC" : "#0F172A");
        var muted = GetBrush(dark ? "#94A3B8" : "#64748B");
        var borderBrush = GetBrush(dark ? "#334155" : "#E2E8F0");
        var surface = GetBrush(dark ? "#162032" : "#FFFFFF");
        var cardSurface = GetBrush(dark ? "#1E293B" : "#F8FAFC");
        var hoverSurface = GetBrush(dark ? "#26354D" : "#EDF2F7");
        var accent = GetBrush(context.AccentColor, "#6366F1");
        var accentForeground = GetContrastBrush(context.AccentColor);
        var successGreen = GetBrush("#10B981");
        var errorRed = GetBrush("#EF4444");

        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        var state = context.State;
        string sourceFileName = state.GetString("sourceFileName") ?? "";
        string sourceExt = (state.GetString("sourceExtension") ?? "").TrimStart('.').ToLowerInvariant();
        string targetFormat = (state.GetString("targetFormat") ?? "").TrimStart('.').ToLowerInvariant();
        string status = state.GetString("status") ?? "idle";
        string lastMessage = state.GetString("lastMessage") ?? "";
        int quality = state.GetInt32("quality", 85);
        int icoSize = state.GetInt32("icoSize", 32);
        bool prettyPrint = !string.Equals(state.GetString("prettyPrint"), "false", StringComparison.OrdinalIgnoreCase);

        bool hasSource = Guid.TryParse(state.GetString("sourceAssetId"), out _);
        var srcCategory = hasSource ? FormatRegistry.DetectCategory(sourceExt) : FileCategory.Unknown;
        var compatibleFormats = hasSource ? FormatRegistry.GetCompatibleTargetFormats(sourceExt) : Array.Empty<FormatDefinition>();

        // Se tem source e não tem target válido, pega o primeiro
        if (hasSource && compatibleFormats.Count > 0 && !compatibleFormats.Any(f => string.Equals(f.Extension, targetFormat, StringComparison.OrdinalIgnoreCase)))
        {
            targetFormat = compatibleFormats[0].Extension;
        }

        var rootGrid = new Grid
        {
            Margin = new Thickness(8 * scale),
            Background = Brushes.Transparent,
            Tag = "plugin-interactive"
        };

        rootGrid.Unloaded += (_, _) =>
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        };

        // =========================================================================
        // ESTRUTURA HORIZONTAL: 3 COLUNAS
        // Coluna 0: Entrada (Dropzone / Info do Arquivo)
        // Coluna 1: Opções (Formato de Destino / Sliders / Configurações)
        // Coluna 2: Saída & Ação (Botão de Progresso / Resultado / Adicionar à Mesa)
        // =========================================================================
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header sutil
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 Colunas

        // Header
        var header = new Grid { Margin = new Thickness(4 * scale, 0, 4 * scale, 6 * scale) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (hasSource)
        {
            var badgeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var badgeBorder = new Border
            {
                Background = dark ? GetBrush("#1E293B") : GetBrush("#F1F5F9"),
                CornerRadius = new CornerRadius(4 * scale),
                Padding = new Thickness(6 * scale, 2 * scale, 6 * scale, 2 * scale),
                Child = new TextBlock
                {
                    Text = $".{sourceExt.ToUpperInvariant()} → .{targetFormat.ToUpperInvariant()}",
                    FontFamily = TextFont,
                    FontSize = 11 * scale,
                    FontWeight = FontWeights.Bold,
                    Foreground = accent
                }
            };
            badgeStack.Children.Add(badgeBorder);
            Grid.SetColumn(badgeStack, 0);
            header.Children.Add(badgeStack);

            var resetBtn = new Button
            {
                Content = "✕ Limpar",
                FontFamily = TextFont,
                FontSize = 11 * scale,
                Foreground = muted,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6 * scale, 2 * scale, 6 * scale, 2 * scale),
                Tag = "plugin-interactive"
            };
            resetBtn.Click += async (_, _) =>
            {
                await context.ExecuteAsync(new PluginCommand("reset"));
            };
            Grid.SetColumn(resetBtn, 1);
            header.Children.Add(resetBtn);
        }
        else
        {
            header.Visibility = Visibility.Collapsed;
        }

        Grid.SetRow(header, 0);
        rootGrid.Children.Add(header);

        // Container com ScrollViewer vertical
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Tag = "plugin-interactive"
        };

        var columnsGrid = new Grid();

        // =========================================================================
        // COLUNA 0: ENTRADA (Drop zone ou Informações do Arquivo Carregado)
        // =========================================================================
        var col0Card = CreateSectionCard("1. Entrada", "\uE8B7", dark, cardSurface, borderBrush, foreground, muted, accent, scale);
        var col0Content = (Panel)col0Card.Child;

        var dropBorder = new Border
        {
            Background = dark ? GetBrush("#141D2E") : GetBrush("#FFFFFF"),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(10 * scale),
            AllowDrop = true,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Stretch,
            Tag = "plugin-interactive"
        };

        dropBorder.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                dropBorder.BorderBrush = accent;
                dropBorder.Background = hoverSurface;
            }
            e.Handled = true;
        };
        dropBorder.DragLeave += (_, e) =>
        {
            dropBorder.BorderBrush = borderBrush;
            dropBorder.Background = dark ? GetBrush("#141D2E") : GetBrush("#FFFFFF");
            e.Handled = true;
        };
        dropBorder.Drop += async (_, e) =>
        {
            dropBorder.BorderBrush = borderBrush;
            dropBorder.Background = dark ? GetBrush("#141D2E") : GetBrush("#FFFFFF");
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    await LoadSourceFileAsync(context, files[0]);
                }
            }
            e.Handled = true;
        };

        if (!hasSource)
        {
            var emptyStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var bigIcon = new TextBlock
            {
                Text = "\uE8B7", // Convert
                FontFamily = IconFont,
                FontSize = 28 * scale,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8 * scale)
            };
            emptyStack.Children.Add(bigIcon);

            var dropLabel = new TextBlock
            {
                Text = "Arraste um arquivo aqui",
                FontFamily = TextFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12 * scale,
                Foreground = foreground,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4 * scale)
            };
            emptyStack.Children.Add(dropLabel);

            var pickBtn = PluginButtons.Create(context, "+ Selecionar Arquivo", primary: false);
            pickBtn.Margin = new Thickness(0, 8 * scale, 0, 0);
            pickBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await ShowOpenFileDialogAsync(context);
            };
            emptyStack.Children.Add(pickBtn);

            dropBorder.Child = emptyStack;
            dropBorder.MouseLeftButtonDown += async (_, e) =>
            {
                e.Handled = true;
                await ShowOpenFileDialogAsync(context);
            };
        }
        else
        {
            var loadedStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var catIcon = new TextBlock
            {
                Text = GetCategoryGlyph(srcCategory),
                FontFamily = IconFont,
                FontSize = 26 * scale,
                Foreground = accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6 * scale)
            };
            loadedStack.Children.Add(catIcon);

            var nameText = new TextBlock
            {
                Text = sourceFileName,
                FontFamily = TextFont,
                FontWeight = FontWeights.Bold,
                FontSize = 12 * scale,
                Foreground = foreground,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4 * scale)
            };
            loadedStack.Children.Add(nameText);

            long.TryParse(state.GetString("sourceSize") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out long sz);
            var infoText = new TextBlock
            {
                Text = $"{sourceExt.ToUpperInvariant()} • {FormatFileSize(sz)}",
                FontFamily = TextFont,
                FontSize = 11 * scale,
                Foreground = muted,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8 * scale)
            };
            loadedStack.Children.Add(infoText);

            var changeBtn = PluginButtons.Create(context, "Trocar Arquivo", primary: false);
            changeBtn.HorizontalAlignment = HorizontalAlignment.Center;
            changeBtn.Click += async (_, e) =>
            {
                e.Handled = true;
                await ShowOpenFileDialogAsync(context);
            };
            loadedStack.Children.Add(changeBtn);

            dropBorder.Child = loadedStack;
        }

        col0Content.Children.Add(dropBorder);
        Grid.SetColumn(col0Card, 0);
        columnsGrid.Children.Add(col0Card);

        // =========================================================================
        // COLUNA 1: OPÇÕES (Formatos Compatíveis e Configurações Dinâmicas)
        // =========================================================================
        var col1Card = CreateSectionCard("2. Opções", "\uE713", dark, cardSurface, borderBrush, foreground, muted, accent, scale);
        var col1Content = (Panel)col1Card.Child;

        if (!hasSource)
        {
            var emptyOptionsText = new TextBlock
            {
                Text = "Selecione ou solte um arquivo na coluna de entrada para visualizar os formatos compatíveis.",
                FontFamily = TextFont,
                FontSize = 11.5 * scale,
                Foreground = muted,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10 * scale)
            };
            col1Content.Children.Add(emptyOptionsText);
        }
        else
        {
            var optionsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Tag = "plugin-interactive"
            };
            var optionsStack = new StackPanel { Margin = new Thickness(2 * scale) };

            var targetLabel = new TextBlock
            {
                Text = "Formato de Destino:",
                FontFamily = TextFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5 * scale,
                Foreground = foreground,
                Margin = new Thickness(0, 0, 0, 6 * scale)
            };
            optionsStack.Children.Add(targetLabel);

            var formatWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10 * scale) };
            foreach (var fmt in compatibleFormats)
            {
                bool isSelected = string.Equals(fmt.Extension, targetFormat, StringComparison.OrdinalIgnoreCase);
                var btn = new Button
                {
                    Content = fmt.Extension.ToUpperInvariant(),
                    FontFamily = TextFont,
                    FontSize = 11 * scale,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isSelected ? accentForeground : foreground,
                    Background = isSelected ? accent : (dark ? GetBrush("#334155") : GetBrush("#E2E8F0")),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10 * scale, 4 * scale, 10 * scale, 4 * scale),
                    Margin = new Thickness(3 * scale),
                    Cursor = Cursors.Hand,
                    Tag = "plugin-interactive"
                };
                string extToSet = fmt.Extension;
                btn.Click += async (_, _) =>
                {
                    await context.ExecuteAsync(new PluginCommand("set-target", new Dictionary<string, string>
                    {
                        ["format"] = extToSet
                    }));
                };
                formatWrap.Children.Add(btn);
            }
            optionsStack.Children.Add(formatWrap);

            // Ajustes específicos dependendo do tipo
            if (srcCategory == FileCategory.Image || targetFormat is "jpg" or "jpeg" or "webp")
            {
                var qualityLabel = new TextBlock
                {
                    Text = $"Qualidade da Imagem: {quality}%",
                    FontFamily = TextFont,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11 * scale,
                    Foreground = foreground,
                    Margin = new Thickness(0, 4 * scale, 0, 4 * scale)
                };
                optionsStack.Children.Add(qualityLabel);

                var slider = PluginSliders.Create(context, 10, 100, quality, val =>
                {
                    int q = (int)Math.Round(val);
                    qualityLabel.Text = $"Qualidade da Imagem: {q}%";
                    var next = context.State.With("quality", q.ToString(CultureInfo.InvariantCulture));
                    context.Commit(next, rebuild: false);
                }, step: 5);
                optionsStack.Children.Add(slider);
            }

            if (targetFormat == "ico")
            {
                var icoLabel = new TextBlock
                {
                    Text = "Tamanho do Ícone:",
                    FontFamily = TextFont,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11 * scale,
                    Foreground = foreground,
                    Margin = new Thickness(0, 6 * scale, 0, 4 * scale)
                };
                optionsStack.Children.Add(icoLabel);

                var icoWrap = new WrapPanel();
                int[] sizes = [16, 32, 48, 64, 128, 256];
                foreach (int s in sizes)
                {
                    bool isSel = icoSize == s;
                    var b = new Button
                    {
                        Content = $"{s}px",
                        FontFamily = TextFont,
                        FontSize = 10 * scale,
                        Foreground = isSel ? accentForeground : foreground,
                        Background = isSel ? accent : (dark ? GetBrush("#334155") : GetBrush("#E2E8F0")),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(6 * scale, 3 * scale, 6 * scale, 3 * scale),
                        Margin = new Thickness(2 * scale),
                        Cursor = Cursors.Hand,
                        Tag = "plugin-interactive"
                    };
                    int sCopy = s;
                    b.Click += async (_, _) =>
                    {
                        await context.ExecuteAsync(new PluginCommand("set-option", new Dictionary<string, string>
                        {
                            ["key"] = "icoSize",
                            ["value"] = sCopy.ToString()
                        }));
                    };
                    icoWrap.Children.Add(b);
                }
                optionsStack.Children.Add(icoWrap);
            }

            if (srcCategory == FileCategory.Data)
            {
                var prettyBtn = new Button
                {
                    Content = prettyPrint ? "✓ Formatação Bonita (Pretty Print)" : "Formatação Compacta (Minified)",
                    FontFamily = TextFont,
                    FontSize = 10.5 * scale,
                    Foreground = prettyPrint ? accent : muted,
                    Background = Brushes.Transparent,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8 * scale, 4 * scale, 8 * scale, 4 * scale),
                    Margin = new Thickness(0, 8 * scale, 0, 0),
                    Cursor = Cursors.Hand,
                    Tag = "plugin-interactive"
                };
                prettyBtn.Click += async (_, _) =>
                {
                    await context.ExecuteAsync(new PluginCommand("set-option", new Dictionary<string, string>
                    {
                        ["key"] = "prettyPrint",
                        ["value"] = prettyPrint ? "false" : "true"
                    }));
                };
                optionsStack.Children.Add(prettyBtn);
            }

            optionsScroll.Content = optionsStack;
            col1Content.Children.Add(optionsScroll);
        }

        Grid.SetColumn(col1Card, 1);
        columnsGrid.Children.Add(col1Card);

        // =========================================================================
        // COLUNA 2: SAÍDA & AÇÃO (Botão com Barra de Progresso Embutida e Resultado)
        // =========================================================================
        var col2Card = CreateSectionCard("3. Saída", "\uE896", dark, cardSurface, borderBrush, foreground, muted, accent, scale);
        var col2Content = (Panel)col2Card.Child;

        var col2Stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // Botão de Ação com Barra de Progresso Embutida
        string buttonLabel = hasSource && !string.IsNullOrEmpty(targetFormat)
            ? $"Converter para .{targetFormat.ToUpperInvariant()}"
            : "Converter Arquivo";

        var progressButton = new PluginProgressButton(context, buttonLabel, primary: true)
        {
            IsEnabled = hasSource && !string.IsNullOrEmpty(targetFormat),
            Margin = new Thickness(0, 0, 0, 10 * scale)
        };

        progressButton.IsEnabled = hasSource && File.Exists(sourcePath) && context.SharedAssets is not null;
        col2Stack.Children.Add(progressButton);

        // Painel de Resultado
        bool hasConverted = status == "success" && !string.IsNullOrWhiteSpace(lastConvertedFile) && File.Exists(lastConvertedFile);
        if (hasConverted)
        {
            var resultCard = new Border
            {
                Background = dark ? GetBrush("#142A22") : GetBrush("#ECFDF5"),
                BorderBrush = successGreen,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8 * scale),
                Padding = new Thickness(10 * scale),
                Margin = new Thickness(0, 0, 0, 10 * scale)
            };

            var resStack = new StackPanel();
            var successHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4 * scale) };
            successHeader.Children.Add(new TextBlock
            {
                Text = "\uE73E", // Checkmark
                FontFamily = IconFont,
                FontSize = 14 * scale,
                Foreground = successGreen,
                Margin = new Thickness(0, 0, 6 * scale, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            successHeader.Children.Add(new TextBlock
            {
                Text = "Convertido com Sucesso!",
                FontFamily = TextFont,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5 * scale,
                Foreground = successGreen,
                VerticalAlignment = VerticalAlignment.Center
            });
            resStack.Children.Add(successHeader);

            var fi = new FileInfo(lastConvertedFile);
            var fileRow = new Grid { Margin = new Thickness(0, 0, 0, 2 * scale) };
            fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameText = new TextBlock
            {
                Text = fi.Name,
                FontFamily = TextFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11 * scale,
                Foreground = foreground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 0);
            fileRow.Children.Add(nameText);

            var copyIconBtn = PluginButtons.Create(context, "Copiar");
            copyIconBtn.Content = new TextBlock
            {
                Text = "\uE8C8",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = Math.Clamp(11 * scale, 10, 16)
            };
            copyIconBtn.MinWidth = 24 * scale;
            copyIconBtn.MinHeight = 24 * scale;
            copyIconBtn.Padding = new Thickness(2 * scale);
            copyIconBtn.ToolTip = "Copiar caminho do arquivo convertido";
            AutomationProperties.SetName(copyIconBtn, "Copiar caminho do arquivo convertido");
            copyIconBtn.Margin = new Thickness(4 * scale, 0, 0, 0);
            copyIconBtn.Click += (_, e) =>
            {
                e.Handled = true;
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, lastConvertedFile));
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Caminho do arquivo copiado!"));
            };
            Grid.SetColumn(copyIconBtn, 1);
            fileRow.Children.Add(copyIconBtn);
            resStack.Children.Add(fileRow);
            resStack.Children.Add(new TextBlock
            {
                Text = $"Tamanho: {FormatFileSize(fi.Length)}",
                FontFamily = TextFont,
                FontSize = 10.5 * scale,
                Foreground = muted,
                Margin = new Thickness(0, 0, 0, 8 * scale)
            });

            // Botão Adicionar à Mesa (DevKit board-files)
            var addToBoardBtn = PluginButtons.Create(context, "Adicionar à Mesa", primary: true);
            addToBoardBtn.Margin = new Thickness(0, 0, 0, 6 * scale);
            addToBoardBtn.Click += (_, e) =>
            {
                e.Handled = true;
                try
                {
                    var boardFile = WindowsPluginFiles.FromPath(lastConvertedFile, fi.Name);
                    context.RequestHostAction(PluginHostAction.AddFiles(new PluginBoardFileRequest([boardFile])));
                    context.RequestHostAction(new(PluginHostActionKind.ShowMessage, $"{fi.Name} adicionado à mesa!"));
                }
                catch (Exception ex)
                {
                    context.RequestHostAction(new(PluginHostActionKind.ShowMessage, $"Erro: {ex.Message}"));
                }
            };
            resStack.Children.Add(addToBoardBtn);

            // Botão Salvar Como
            var saveAsBtn = PluginButtons.Create(context, "Salvar Como...", primary: false);
            saveAsBtn.Click += (_, e) =>
            {
                e.Handled = true;
                var saveDialog = new SaveFileDialog
                {
                    Title = "Salvar arquivo convertido",
                    FileName = fi.Name,
                    Filter = $"{targetFormat.ToUpperInvariant()} Arquivo (*.{targetFormat})|*.{targetFormat}|Todos os arquivos (*.*)|*.*"
                };
                if (saveDialog.ShowDialog() == true)
                {
                    File.Copy(lastConvertedFile, saveDialog.FileName, overwrite: true);
                    context.RequestHostAction(new(PluginHostActionKind.ShowMessage, $"Salvo em: {saveDialog.FileName}"));
                }
            };
            resStack.Children.Add(saveAsBtn);

            resultCard.Child = resStack;
            col2Stack.Children.Add(resultCard);
        }
        else if (status == "error")
        {
            var errorCard = new Border
            {
                Background = dark ? GetBrush("#331B1B") : GetBrush("#FEF2F2"),
                BorderBrush = errorRed,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8 * scale),
                Padding = new Thickness(10 * scale)
            };
            var errStack = new StackPanel();
            errStack.Children.Add(new TextBlock
            {
                Text = "Falha na conversão",
                FontFamily = TextFont,
                FontWeight = FontWeights.Bold,
                FontSize = 11.5 * scale,
                Foreground = errorRed,
                Margin = new Thickness(0, 0, 0, 4 * scale)
            });
            errStack.Children.Add(new TextBlock
            {
                Text = lastMessage,
                FontFamily = TextFont,
                FontSize = 10.5 * scale,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap
            });
            errorCard.Child = errStack;
            col2Stack.Children.Add(errorCard);
        }
        else
        {
            var placeholderCard = new Border
            {
                Background = dark ? GetBrush("#141D2E") : GetBrush("#FFFFFF"),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8 * scale),
                Padding = new Thickness(10 * scale),
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var placeText = new TextBlock
            {
                Text = "O arquivo convertido e as opções de salvamento aparecerão aqui após clicar no botão de converter.",
                FontFamily = TextFont,
                FontSize = 11 * scale,
                Foreground = muted,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            placeholderCard.Child = placeText;
            col2Stack.Children.Add(placeholderCard);
        }

        // Lógica de clique do botão com progresso integrado
        progressButton.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!hasSource || !File.Exists(sourcePath) || string.IsNullOrEmpty(targetFormat) || context.SharedAssets is null) return;

            progressButton.SetProgress(0.05, "Convertendo...");

            try
            {
                string tempOut = await UniversalConversionCoordinator.ConvertFileAsync(
                    sourcePath, targetFormat, context.State.GetInt32("quality", 85), context.State.GetInt32("icoSize", 32),
                    !string.Equals(context.State.GetString("prettyPrint"), "false", StringComparison.OrdinalIgnoreCase), 192000, cancellationToken);
                var outputAsset = await context.SharedAssets.ImportAsync(WindowsPluginFile.FromPath(tempOut), cancellationToken);

                progressButton.SetSuccess("Concluído!");
                await Task.Delay(200, cancellationToken);

                await context.ExecuteAsync(new PluginCommand("set-status", new Dictionary<string, string>
                {
                    ["status"] = "success",
                    ["message"] = "Conversão concluída com êxito!",
                    ["assetId"] = outputAsset.Id,
                    ["fileName"] = outputAsset.Name
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                progressButton.SetError("Falha na conversão");
                await context.ExecuteAsync(new PluginCommand("set-status", new Dictionary<string, string>
                {
                    ["status"] = "error",
                    ["message"] = "Não foi possível converter ou compartilhar o arquivo.",
                    ["assetId"] = ""
                }));
            }
        };

        col2Content.Children.Add(col2Stack);
        Grid.SetColumn(col2Card, 2);
        columnsGrid.Children.Add(col2Card);

        bool isCompactLayout = false;

        void ApplyLayout(bool compact)
        {
            isCompactLayout = compact;

            columnsGrid.ColumnDefinitions.Clear();
            columnsGrid.RowDefinitions.Clear();

            if (compact)
            {
                // Modo empilhado vertical para janelas estreitas / quadradas (ex: 240x240)
                columnsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                columnsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                columnsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                Grid.SetRow(col0Card, 0);
                Grid.SetColumn(col0Card, 0);

                Grid.SetRow(col1Card, 1);
                Grid.SetColumn(col1Card, 0);

                Grid.SetRow(col2Card, 2);
                Grid.SetColumn(col2Card, 0);

                col0Card.Margin = new Thickness(0, 0, 0, 8 * scale);
                col1Card.Margin = new Thickness(0, 0, 0, 8 * scale);
                col2Card.Margin = new Thickness(0);

                col0Card.MinHeight = 110 * scale;
                col1Card.MinHeight = 0;
                col2Card.MinHeight = 0;
            }
            else
            {
                // Modo 3 colunas horizontais para janelas largas (>= 480 DIP)
                columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

                Grid.SetRow(col0Card, 0);
                Grid.SetColumn(col0Card, 0);

                Grid.SetRow(col1Card, 0);
                Grid.SetColumn(col1Card, 1);

                Grid.SetRow(col2Card, 0);
                Grid.SetColumn(col2Card, 2);

                col0Card.Margin = new Thickness(0, 0, 4 * scale, 0);
                col1Card.Margin = new Thickness(4 * scale, 0, 4 * scale, 0);
                col2Card.Margin = new Thickness(4 * scale, 0, 0, 0);

                col0Card.MinHeight = 0;
                col1Card.MinHeight = 0;
                col2Card.MinHeight = 0;
            }
        }

        void CheckLayout()
        {
            var w = rootGrid.ActualWidth;
            if (w <= 0) w = context.Width;
            bool compact = w < 480;
            if (compact != isCompactLayout || columnsGrid.ColumnDefinitions.Count == 0 && columnsGrid.RowDefinitions.Count == 0)
            {
                ApplyLayout(compact);
            }

            var isVerySmall = w < 260 || (rootGrid.ActualHeight > 0 && rootGrid.ActualHeight < 260);
            rootGrid.Margin = new Thickness(isVerySmall ? 4 * scale : 8 * scale);
        }

        ApplyLayout(false);
        rootGrid.SizeChanged += (_, _) => CheckLayout();
        context.LayoutChanged += CheckLayout;

        scrollViewer.Content = columnsGrid;
        Grid.SetRow(scrollViewer, 1);
        rootGrid.Children.Add(scrollViewer);

        return rootGrid;
    }

    private static Border CreateSectionCard(
        string title, string glyph, bool dark, Brush background, Brush border, Brush foreground, Brush muted, Brush accent, double scale)
    {
        var borderBox = new Border
        {
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10 * scale),
            Padding = new Thickness(10 * scale),
            Margin = new Thickness(4 * scale),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var stack = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8 * scale) };
        header.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = IconFont,
            FontSize = 13 * scale,
            Foreground = accent,
            Margin = new Thickness(0, 0, 6 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = TextFont,
            FontWeight = FontWeights.Bold,
            FontSize = 12 * scale,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        borderBox.Child = stack;
        return borderBox;
    }

    private static async Task ShowOpenFileDialogAsync(WindowsPluginViewContext context)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o arquivo para converter",
            Filter = "Todos os formatos suportados (*.*)|*.*|Imagens (*.png;*.jpg;*.webp;*.bmp;*.gif;*.ico;*.tiff)|*.png;*.jpg;*.webp;*.bmp;*.gif;*.ico;*.tiff|Áudio e Vídeo (*.mp3;*.wav;*.aac;*.mp4;*.wmv;*.avi)|*.mp3;*.wav;*.aac;*.mp4;*.wmv;*.avi|Dados e Documentos (*.json;*.csv;*.xml;*.yaml;*.md;*.txt)|*.json;*.csv;*.xml;*.yaml;*.md;*.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadSourceFileAsync(context, dialog.FileName);
        }
    }

    private static async Task LoadSourceFileAsync(WindowsPluginViewContext context, string filePath, CancellationToken token = default)
    {
        if (!File.Exists(filePath)) return;

        if (context.SharedAssets is null)
        {
            context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Atualize o ClipDesk para compartilhar arquivos deste plugin."));
            return;
        }
        try
        {
        var fi = new FileInfo(filePath);
        var asset = await context.SharedAssets.ImportAsync(WindowsPluginFile.FromPath(filePath), token);
        token.ThrowIfCancellationRequested();
        await context.ExecuteAsync(new PluginCommand("set-source", new Dictionary<string, string>
        {
            ["assetId"] = asset.Id,
            ["fileName"] = fi.Name,
            ["size"] = fi.Length.ToString(CultureInfo.InvariantCulture)
        }), cancellationToken: token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Não foi possível compartilhar o arquivo: " + ex.Message));
        }
    }

    private static string GetCategoryGlyph(FileCategory cat) => cat switch
    {
        FileCategory.Image => "\uEB9F",     // Picture
        FileCategory.Audio => "\uE8D6",     // Music
        FileCategory.Video => "\uE714",     // Video
        FileCategory.Document => "\uE8A5",  // Document
        FileCategory.Data => "\uE943",      // Code / Data
        FileCategory.Archive => "\uF012",   // Zip
        _ => "\uE8B7"
    };

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F1} GB";
    }

    private static SolidColorBrush GetBrush(string hex, string fallback = "#6366F1")
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var color = (Color)ColorConverter.ConvertFromString(fallback);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    private static SolidColorBrush GetContrastBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            var contrast = luminance > 0.55 ? Colors.Black : Colors.White;
            var brush = new SolidColorBrush(contrast);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.White;
        }
    }
}
