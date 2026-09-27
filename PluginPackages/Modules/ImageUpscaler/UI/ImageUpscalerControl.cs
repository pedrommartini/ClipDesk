using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDesk.Plugin.ImageUpscaler.Engines;
using ClipDesk.Plugin.ImageUpscaler.Models;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using Microsoft.Win32;

namespace ClipDesk.Plugin.ImageUpscaler.UI;

/// <summary>
/// Controle principal de interface do plugin Image Upscaler v2.
/// Implementa layout horizontal estruturado (Entrada | Opções | Saída & Ação) com suporte nativo
/// a arrastar arquivos, botão com progresso integrado e comparativo Antes/Depois interativo.
/// </summary>
public sealed class ImageUpscalerControl : Grid
{
    private readonly WindowsPluginViewContext _context;
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationToken _token;
    private bool _disposed;
    private bool _restored;
    private readonly ImageUpscaleOptions _options;

    // Estado ativo
    private UpscaleItem? _currentItem;
    private bool _isProcessing;

    // Elementos principais do layout
    private readonly Grid _mainContentGrid;
    private readonly Border _overlayContainer;

    // Layout adaptativo e colunas
    private readonly Grid _columnsGrid;
    private readonly Grid _col0Container;
    private readonly Border _col1Card;
    private readonly Border _col2Card;
    private bool _isCompactLayout;
    private readonly double _scale;

    // Coluna 1 (Entrada)
    private readonly Border _dropZoneCard;
    private readonly Border _imagePreviewCard;
    private readonly Image _previewImage;
    private readonly TextBlock _fileNameText;
    private readonly TextBlock _fileDimensionsText;
    private readonly TextBlock _fileSizeText;

    // Coluna 2 (Opções)
    private readonly TextBlock _projectionBadgeText;
    private readonly PluginToggle _scaleToggle;
    private readonly PluginToggle _modelToggle;
    private readonly PluginSlider _sharpnessSlider;
    private readonly TextBlock _sharpnessValueText;
    private readonly PluginToggle _denoiseToggle;
    private readonly PluginToggle _engineToggle;

    // Coluna 3 (Saída & Ação)
    private readonly PluginProgressButton _upscaleButton;
    private readonly Border _resultCard;
    private readonly TextBlock _resultDimensionsText;
    private readonly TextBlock _resultSpeedText;
    private readonly Button _compareButton;
    private readonly Button _addToBoardButton;

    public ImageUpscalerControl(WindowsPluginViewContext context)
    {
        _context = context;
        _token = _cts.Token;
        var state = new ImageUpscalerState(context.State);
        _options = ImageUpscaleOptions.FromState(state);

        bool dark = context.IsDarkMode;
        _scale = Math.Max(1.0, context.Scale);
        double scale = _scale;

        Margin = new Thickness(8 * scale);
        Background = Brushes.Transparent;

        // Container raiz para suportar overlays (Antes/Depois full-plugin)
        _mainContentGrid = new Grid();
        _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 Colunas Horizontais

        _overlayContainer = new Border
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Panel.SetZIndex(_overlayContainer, 999);

        // =========================================================================
        // ROW 0: HEADER
        // =========================================================================
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8 * scale) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconText = new TextBlock
        {
            Text = "\uE790", // Magic Wand / Super Resolution
            FontFamily = UiStyles.IconFont,
            FontSize = 16 * scale,
            Foreground = UiStyles.GetBrush(_context.AccentColor),
            Margin = new Thickness(0, 0, 6 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(iconText);

        var count = ImageUpscalerState.GetTotalImagesUpscaled(context.State);
        var statsBlock = new TextBlock
        {
            Text = count > 0 ? $"★ {count} ampliadas" : "Alta Resolução",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5 * scale,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(statsBlock);
        Grid.SetColumn(titleStack, 0);
        headerGrid.Children.Add(titleStack);

        var pickFileBtn = new Button
        {
            Content = "+ Selecionar Imagem...",
            FontFamily = UiStyles.TextFont,
            FontSize = 12 * scale,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10 * scale, 4 * scale, 10 * scale, 4 * scale),
            Background = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0"),
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = "plugin-interactive"
        };
        pickFileBtn.Click += async (_, _) => await OpenFileDialogAsync();
        Grid.SetColumn(pickFileBtn, 1);
        headerGrid.Children.Add(pickFileBtn);

        Grid.SetRow(headerGrid, 0);
        _mainContentGrid.Children.Add(headerGrid);

        // =========================================================================
        // ROW 1: 3-ZONE HORIZONTAL LAYOUT (Entrada | Opções | Saída & Ação)
        // =========================================================================
        _columnsGrid = new Grid();

        // -------------------------------------------------------------------------
        // COLUNA 0: ENTRADA (Drop Zone & Imagem Carregada)
        // -------------------------------------------------------------------------
        var col0Container = new Grid();

        // Card DropZone Vazia
        _dropZoneCard = new Border
        {
            Background = dark ? UiStyles.GetBrush("#151E2E") : UiStyles.GetBrush("#F8FAFC"),
            BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#CBD5E1"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10 * scale),
            Padding = new Thickness(12 * scale),
            AllowDrop = true,
            Cursor = Cursors.Hand,
            Tag = "plugin-interactive"
        };
        _dropZoneCard.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                _dropZoneCard.BorderBrush = UiStyles.GetBrush(_context.AccentColor);
            }
            e.Handled = true;
        };
        _dropZoneCard.DragLeave += (_, e) =>
        {
            _dropZoneCard.BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#CBD5E1");
            e.Handled = true;
        };
        _dropZoneCard.Drop += async (_, e) =>
        {
            _dropZoneCard.BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#CBD5E1");
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) await LoadImageFileAsync(files[0]);
            }
            e.Handled = true;
        };
        _dropZoneCard.MouseLeftButtonDown += async (_, _) => await OpenFileDialogAsync();

        var dropStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var dropIcon = new TextBlock
        {
            Text = "\uEB9F", // Image icon
            FontFamily = UiStyles.IconFont,
            FontSize = 32 * scale,
            Foreground = UiStyles.GetBrush(_context.AccentColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8 * scale)
        };
        dropStack.Children.Add(dropIcon);

        var dropTitle = new TextBlock
        {
            Text = "Arraste uma imagem",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13 * scale,
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        dropStack.Children.Add(dropTitle);

        var dropSub = new TextBlock
        {
            Text = "ou clique para navegar\n(PNG, JPG, WebP, BMP)",
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4 * scale, 0, 0)
        };
        dropStack.Children.Add(dropSub);
        _dropZoneCard.Child = dropStack;
        col0Container.Children.Add(_dropZoneCard);

        // Card com Imagem Carregada
        _imagePreviewCard = new Border
        {
            Background = dark ? UiStyles.GetBrush("#1E293B") : UiStyles.GetBrush("#F8FAFC"),
            BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10 * scale),
            Padding = new Thickness(8 * scale),
            Visibility = Visibility.Collapsed
        };
        var previewLayout = new Grid();
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Imagem
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Metadados
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Botão Trocar

        _previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            MaxHeight = 130 * scale,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6 * scale)
        };
        RenderOptions.SetBitmapScalingMode(_previewImage, BitmapScalingMode.HighQuality);
        Grid.SetRow(_previewImage, 0);
        previewLayout.Children.Add(_previewImage);

        var metaStack = new StackPanel { Margin = new Thickness(0, 0, 0, 6 * scale) };
        _fileNameText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5 * scale,
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        metaStack.Children.Add(_fileNameText);

        var subMetaStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _fileDimensionsText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontSize = 10 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = UiStyles.GetBrush(_context.AccentColor),
            Margin = new Thickness(0, 2 * scale, 6 * scale, 0)
        };
        subMetaStack.Children.Add(_fileDimensionsText);

        _fileSizeText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontSize = 10 * scale,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            Margin = new Thickness(0, 2 * scale, 0, 0)
        };
        subMetaStack.Children.Add(_fileSizeText);
        metaStack.Children.Add(subMetaStack);

        Grid.SetRow(metaStack, 1);
        previewLayout.Children.Add(metaStack);

        var replaceBtn = new Button
        {
            Content = "Trocar Imagem",
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Padding = new Thickness(8 * scale, 3 * scale, 8 * scale, 3 * scale),
            Background = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0"),
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = "plugin-interactive"
        };
        replaceBtn.Click += async (_, _) => await OpenFileDialogAsync();
        Grid.SetRow(replaceBtn, 2);
        previewLayout.Children.Add(replaceBtn);

        _imagePreviewCard.Child = previewLayout;
        col0Container.Children.Add(_imagePreviewCard);

        Grid.SetColumn(col0Container, 0);
        _columnsGrid.Children.Add(col0Container);

        // -------------------------------------------------------------------------
        // COLUNA 1: OPÇÕES & PARÂMETROS (Centro)
        // -------------------------------------------------------------------------
        var col1Card = new Border
        {
            Background = dark ? UiStyles.GetBrush("#1E293B") : UiStyles.GetBrush("#F8FAFC"),
            BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10 * scale),
            Padding = new Thickness(10 * scale)
        };

        var optionsStack = new StackPanel();

        // 1. Projeção em tempo real
        var projBorder = new Border
        {
            Background = dark ? UiStyles.GetBrush("#0F172A") : UiStyles.GetBrush("#EEF2F6"),
            CornerRadius = new CornerRadius(6 * scale),
            Padding = new Thickness(8 * scale, 4 * scale, 8 * scale, 4 * scale),
            Margin = new Thickness(0, 0, 0, 8 * scale)
        };
        _projectionBadgeText = new TextBlock
        {
            Text = "⚡ Resolução Projetada: selecione uma imagem",
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            FontWeight = FontWeights.SemiBold,
            Foreground = UiStyles.GetBrush(_context.AccentColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        projBorder.Child = _projectionBadgeText;
        optionsStack.Children.Add(projBorder);

        // 2. Fator de Escala (2x, 4x, 8x)
        var scaleLabel = new TextBlock
        {
            Text = "FATOR DE ESCALA",
            FontFamily = UiStyles.TextFont,
            FontSize = 9.5 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            Margin = new Thickness(0, 0, 0, 3 * scale)
        };
        optionsStack.Children.Add(scaleLabel);

        _scaleToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("2x", "2x"),
            new PluginToggleOption("4x", "4x (Padrão)"),
            new PluginToggleOption("8x", "8x Ultra")
        }, _options.Factor.ToWireString(), val =>
        {
            _options.Factor = EnumExtensions.ParseUpscaleFactor(val);
            UpdateProjection();
            UpdateActionButtonLabel();
            SyncSettingsToHost();
        });
        optionsStack.Children.Add(_scaleToggle);

        // 3. Modelo de Upscale
        var modelLabel = new TextBlock
        {
            Text = "PERFIL DE OTIMIZAÇÃO",
            FontFamily = UiStyles.TextFont,
            FontSize = 9.5 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            Margin = new Thickness(0, 8 * scale, 0, 3 * scale)
        };
        optionsStack.Children.Add(modelLabel);

        _modelToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("balanced", "Equilibrado"),
            new PluginToggleOption("photo", "Foto"),
            new PluginToggleOption("illustration", "Arte"),
            new PluginToggleOption("crisp-text", "Texto")
        }, _options.Model.ToWireString(), val =>
        {
            _options.Model = EnumExtensions.ParseUpscaleModel(val);
            SyncSettingsToHost();
        });
        optionsStack.Children.Add(_modelToggle);

        // 4. Slider de Nitidez
        var sharpHeader = new Grid { Margin = new Thickness(0, 8 * scale, 0, 2 * scale) };
        sharpHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sharpHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sharpLabel = new TextBlock
        {
            Text = "NITIDEZ & DETALHES",
            FontFamily = UiStyles.TextFont,
            FontSize = 9.5 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B")
        };
        Grid.SetColumn(sharpLabel, 0);
        sharpHeader.Children.Add(sharpLabel);

        _sharpnessValueText = new TextBlock
        {
            Text = $"{_options.Sharpness}%",
            FontFamily = UiStyles.TextFont,
            FontSize = 10 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A")
        };
        Grid.SetColumn(_sharpnessValueText, 1);
        sharpHeader.Children.Add(_sharpnessValueText);
        optionsStack.Children.Add(sharpHeader);

        _sharpnessSlider = PluginSliders.Create(context, 0, 100, _options.Sharpness, val =>
        {
            int intVal = (int)Math.Round(val);
            _options.Sharpness = intVal;
            _sharpnessValueText.Text = $"{intVal}%";
        }, step: 1);
        _sharpnessSlider.InteractionCompleted += () => SyncSettingsToHost();
        optionsStack.Children.Add(_sharpnessSlider);

        // 5. Redução de Ruído & Motor
        var footerOptionsGrid = new Grid { Margin = new Thickness(0, 6 * scale, 0, 0) };
        footerOptionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerOptionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var denoiseStack = new StackPanel { Margin = new Thickness(0, 0, 4 * scale, 0) };
        var denoiseLabel = new TextBlock
        {
            Text = "DENOISE",
            FontFamily = UiStyles.TextFont,
            FontSize = 9 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            Margin = new Thickness(0, 0, 0, 2 * scale)
        };
        denoiseStack.Children.Add(denoiseLabel);

        _denoiseToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("off", "Off"),
            new PluginToggleOption("low", "Baixo"),
            new PluginToggleOption("high", "Alto")
        }, _options.Denoise.ToWireString(), val =>
        {
            _options.Denoise = EnumExtensions.ParseDenoiseLevel(val);
            SyncSettingsToHost();
        });
        denoiseStack.Children.Add(_denoiseToggle);
        Grid.SetColumn(denoiseStack, 0);
        footerOptionsGrid.Children.Add(denoiseStack);

        var engineStack = new StackPanel { Margin = new Thickness(4 * scale, 0, 0, 0) };
        var engineLabel = new TextBlock
        {
            Text = "MOTOR",
            FontFamily = UiStyles.TextFont,
            FontSize = 9 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            Margin = new Thickness(0, 0, 0, 2 * scale)
        };
        engineStack.Children.Add(engineLabel);

        _engineToggle = PluginToggles.Create(context, new[]
        {
            new PluginToggleOption("local", "Nativo"),
            new PluginToggleOption("cloud", "Nuvem")
        }, _options.Engine.ToWireString(), val =>
        {
            _options.Engine = EnumExtensions.ParseUpscaleEngine(val);
            SyncSettingsToHost();
        });
        engineStack.Children.Add(_engineToggle);
        Grid.SetColumn(engineStack, 1);
        footerOptionsGrid.Children.Add(engineStack);

        optionsStack.Children.Add(footerOptionsGrid);

        col1Card.Child = optionsStack;
        Grid.SetColumn(col1Card, 1);
        _columnsGrid.Children.Add(col1Card);

        // -------------------------------------------------------------------------
        // COLUNA 2: SAÍDA & AÇÃO (Direita)
        // -------------------------------------------------------------------------
        var col2Card = new Border
        {
            Background = dark ? UiStyles.GetBrush("#1E293B") : UiStyles.GetBrush("#F8FAFC"),
            BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#CBD5E1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10 * scale),
            Padding = new Thickness(10 * scale)
        };

        var actionStack = new StackPanel();

        // 1. Botão Principal com Barra de Progresso Embutida
        _upscaleButton = new PluginProgressButton(context, $"Ampliar Imagem ({_options.Factor.ToWireString()})", primary: true)
        {
            Margin = new Thickness(0, 0, 0, 8 * scale)
        };
        _upscaleButton.Click += async (_, _) => await RunUpscaleAsync();
        actionStack.Children.Add(_upscaleButton);

        // 2. Card de Resultado (Exibido após upscale concluído)
        _resultCard = new Border
        {
            Background = dark ? UiStyles.GetBrush("#151E2E") : UiStyles.GetBrush("#EEF2F6"),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(8 * scale),
            Margin = new Thickness(0, 0, 0, 8 * scale),
            Visibility = Visibility.Collapsed
        };
        var resultLayout = new StackPanel();

        var resHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 4 * scale) };
        resHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var resSuccessText = new TextBlock
        {
            Text = "✓ Ampliação Concluída",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5 * scale,
            Foreground = UiStyles.GetBrush("#10B981"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(resSuccessText, 0);
        resHeaderGrid.Children.Add(resSuccessText);

        var copyResultBtn = PluginButtons.Create(context, "Copiar");
        copyResultBtn.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(11 * scale, 10, 16)
        };
        copyResultBtn.MinWidth = 24 * scale;
        copyResultBtn.MinHeight = 24 * scale;
        copyResultBtn.Padding = new Thickness(2 * scale);
        copyResultBtn.ToolTip = "Copiar caminho da imagem ampliada";
        AutomationProperties.SetName(copyResultBtn, "Copiar caminho da imagem ampliada");
        copyResultBtn.Margin = new Thickness(4 * scale, 0, 0, 0);
        copyResultBtn.Click += (_, e) =>
        {
            e.Handled = true;
            string? textToCopy = _currentItem?.Result?.OutputPath;
            if (string.IsNullOrWhiteSpace(textToCopy))
            {
                textToCopy = context.Module.GetClipboardText(context.State);
            }
            if (!string.IsNullOrWhiteSpace(textToCopy))
            {
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, textToCopy));
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Copiado para a área de transferência!"));
            }
        };
        Grid.SetColumn(copyResultBtn, 1);
        resHeaderGrid.Children.Add(copyResultBtn);
        resultLayout.Children.Add(resHeaderGrid);

        _resultDimensionsText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontSize = 11 * scale,
            FontWeight = FontWeights.SemiBold,
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3 * scale, 0, 0)
        };
        resultLayout.Children.Add(_resultDimensionsText);

        _resultSpeedText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontSize = 10 * scale,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2 * scale, 0, 0)
        };
        resultLayout.Children.Add(_resultSpeedText);

        _resultCard.Child = resultLayout;
        actionStack.Children.Add(_resultCard);

        // 3. Botão Comparador Antes / Depois
        _compareButton = PluginButtons.Create(context, "◂ ▸ Comparar Antes / Depois");
        _compareButton.Margin = new Thickness(0, 0, 0, 6 * scale);
        _compareButton.Visibility = Visibility.Collapsed;
        _compareButton.Click += (_, _) => OpenBeforeAfterOverlay();
        actionStack.Children.Add(_compareButton);

        // 4. Botão Adicionar à Mesa
        _addToBoardButton = PluginButtons.Create(context, "+ Adicionar à Mesa");
        _addToBoardButton.Visibility = Visibility.Collapsed;
        _addToBoardButton.Click += (_, _) => AddResultToBoard();
        actionStack.Children.Add(_addToBoardButton);

        col2Card.Child = actionStack;
        Grid.SetColumn(col2Card, 2);
        _columnsGrid.Children.Add(col2Card);

        _col0Container = col0Container;
        _col1Card = col1Card;
        _col2Card = col2Card;

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Tag = "plugin-interactive",
            Content = _columnsGrid
        };
        Grid.SetRow(scrollViewer, 1);
        _mainContentGrid.Children.Add(scrollViewer);

        ApplyLayout(false);
        SizeChanged += (_, _) => CheckLayout();
        _context.LayoutChanged += CheckLayout;

        Children.Add(_mainContentGrid);
        Children.Add(_overlayContainer);

        // Eventos e Ciclo de Vida
        context.FilesDropped += OnFilesDroppedAsync;
        Loaded += async (_, _) => await RestoreSharedFilesAsync();
        Unloaded += (_, _) =>
        {
            if (_disposed) return;
            _disposed = true;
            context.FilesDropped -= OnFilesDroppedAsync;
            context.LayoutChanged -= CheckLayout;
            _cts.Cancel();
            _cts.Dispose();
        };

        UpdateProjection();
    }

    private void ApplyLayout(bool compact)
    {
        _columnsGrid.ColumnDefinitions.Clear();
        _columnsGrid.RowDefinitions.Clear();

        if (compact)
        {
            _columnsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _columnsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _columnsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(_col0Container, 0);
            Grid.SetColumn(_col0Container, 0);

            Grid.SetRow(_col1Card, 1);
            Grid.SetColumn(_col1Card, 0);

            Grid.SetRow(_col2Card, 2);
            Grid.SetColumn(_col2Card, 0);

            _col0Container.Margin = new Thickness(0, 0, 0, 8 * _scale);
            _col1Card.Margin = new Thickness(0, 0, 0, 8 * _scale);
            _col2Card.Margin = new Thickness(0);

            _dropZoneCard.MinHeight = 100 * _scale;
        }
        else
        {
            _columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            _columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            _columnsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

            Grid.SetRow(_col0Container, 0);
            Grid.SetColumn(_col0Container, 0);

            Grid.SetRow(_col1Card, 0);
            Grid.SetColumn(_col1Card, 1);

            Grid.SetRow(_col2Card, 0);
            Grid.SetColumn(_col2Card, 2);

            _col0Container.Margin = new Thickness(0, 0, 4 * _scale, 0);
            _col1Card.Margin = new Thickness(4 * _scale, 0, 4 * _scale, 0);
            _col2Card.Margin = new Thickness(4 * _scale, 0, 0, 0);

            _dropZoneCard.MinHeight = 0;
        }
    }

    private void CheckLayout()
    {
        var w = ActualWidth;
        if (w <= 0) w = _context.Width;
        bool compact = w < 480;
        if (compact != _isCompactLayout || (_columnsGrid.ColumnDefinitions.Count == 0 && _columnsGrid.RowDefinitions.Count == 0))
        {
            _isCompactLayout = compact;
            ApplyLayout(compact);
        }

        var isVerySmall = w < 260 || (ActualHeight > 0 && ActualHeight < 260);
        Margin = new Thickness(isVerySmall ? 4 * _scale : 8 * _scale);
    }

    private async Task OnFilesDroppedAsync(IReadOnlyList<PluginDroppedFile> droppedFiles)
    {
        if (droppedFiles != null && droppedFiles.Count > 0)
        {
            var first = droppedFiles[0];
            await LoadImageFileAsync(first.Path);
        }
    }

    private async Task OpenFileDialogAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecione uma imagem para ampliar",
            Filter = "Imagens Suportadas (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Todos os Arquivos (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            await LoadImageFileAsync(dlg.FileName);
        }
    }

    private async Task LoadImageFileAsync(string path)
    {
        if (!WicUpscaleEngine.IsSupported(path))
        {
            _context.RequestHostAction(new PluginHostAction(PluginHostActionKind.ShowMessage, "Formato não suportado. Escolha uma imagem PNG, JPG, WebP ou BMP."));
            return;
        }

        var item = UpscaleItem.FromPath(path);
        if (item == null)
        {
            _context.RequestHostAction(new PluginHostAction(PluginHostActionKind.ShowMessage, "Não foi possível carregar a imagem."));
            return;
        }

        if (_context.SharedAssets is null || _isProcessing || _disposed) return;
        try
        {
            var asset = await _context.SharedAssets.ImportAsync(WindowsPluginFile.FromPath(path), _token);
            _token.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(async () =>
            {
                if (_disposed) return;
                await _context.ExecuteAsync(new PluginCommand(ImageUpscalerCommands.SetInput,
                    new Dictionary<string, string> { [UpscaleSharedState.InputAsset] = asset.Id }), rebuild: false, cancellationToken: _token);
                ApplyInput(item);
            }).Task.Unwrap();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (!_disposed) _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Não foi possível compartilhar a imagem: " + ex.Message));
            });
        }
    }

    private void ApplyInput(UpscaleItem item)
    {
        _currentItem = item;
        _previewImage.Source = item.Thumbnail;
        _fileNameText.Text = item.FileName;
        _fileDimensionsText.Text = $"{item.Width} × {item.Height} px";
        _fileSizeText.Text = FormatBytes(item.FileSizeBytes);

        _dropZoneCard.Visibility = Visibility.Collapsed;
        _imagePreviewCard.Visibility = Visibility.Visible;

        // Resetar área de resultados de conversão anterior
        _resultCard.Visibility = Visibility.Collapsed;
        _compareButton.Visibility = Visibility.Collapsed;
        _addToBoardButton.Visibility = Visibility.Collapsed;
        _upscaleButton.Reset($"Ampliar Imagem ({_options.Factor.ToWireString()})");
        UpdateActionButtonLabel();

        UpdateProjection();
    }

    private void UpdateProjection()
    {
        if (_currentItem == null)
        {
            _projectionBadgeText.Text = "⚡ Resolução: selecione uma imagem";
            return;
        }

        int mult = (int)_options.Factor;
        int targetW = _currentItem.Width * mult;
        int targetH = _currentItem.Height * mult;
        double origMp = (double)_currentItem.Width * _currentItem.Height / 1_000_000.0;
        double targetMp = (double)targetW * targetH / 1_000_000.0;

        _projectionBadgeText.Text = $"⚡ {_currentItem.Width}×{_currentItem.Height} ➔ {targetW}×{targetH} ({mult}x · {targetMp:0.1} MP)";
    }

    private void UpdateActionButtonLabel()
    {
        if (!_isProcessing)
        {
            _upscaleButton.DefaultLabel = $"Ampliar Imagem ({_options.Factor.ToWireString()})";
        }
    }

    private async Task RunUpscaleAsync()
    {
        if (_isProcessing) return;

        if (_currentItem == null)
        {
            await OpenFileDialogAsync();
            return;
        }

        if (_context.SharedAssets is null || _disposed) return;
        var inputId = _context.State.GetString(UpscaleSharedState.InputAsset);
        var currentItem = _currentItem;
        _isProcessing = true;
        _upscaleButton.SetExecuting(true, "Processando...");

        var progressReporter = new Progress<double>(p =>
        {
            int pct = (int)Math.Round(p * 100);
            Dispatcher.BeginInvoke(() => { if (!_disposed) _upscaleButton.SetProgress(p, $"Ampliando ({pct}%)..."); });
        });

        try
        {
            UpscaleResult result;
            if (_options.Engine == UpscaleEngine.CloudAi)
            {
                result = await CloudAiUpscaleEngine.UpscaleAsync(currentItem.SourcePath, _options, progressReporter, _token);
            }
            else
            {
                result = await WicUpscaleEngine.UpscaleAsync(currentItem.SourcePath, _options, progressReporter, _token);
            }

            if (result.Succeeded && File.Exists(result.OutputPath))
            {
                var asset = await _context.SharedAssets.ImportAsync(WindowsPluginFile.FromPath(result.OutputPath), _token);
                _token.ThrowIfCancellationRequested();
                // Registrar no Core
                long pixelsGen = (long)result.UpscaledWidth * result.UpscaledHeight;
                var cmd = new PluginCommand(ImageUpscalerCommands.RecordUpscale, new Dictionary<string, string>
                {
                    ["pixels"] = pixelsGen.ToString(CultureInfo.InvariantCulture),
                    [UpscaleSharedState.InputAsset] = inputId ?? "",
                    [UpscaleSharedState.OutputAsset] = asset.Id,
                    [UpscaleSharedState.OutputInfo] = System.Text.Json.JsonSerializer.Serialize(new UpscaleOutputInfo(
                        result.UpscaledWidth, result.UpscaledHeight, result.Elapsed.TotalMilliseconds,
                        result.Factor.ToWireString(), result.Model.ToWireString(), result.EngineUsed)),
                    [ImageUpscalerState.LastMessage] = $"Ampliado para {result.UpscaledWidth}×{result.UpscaledHeight} em {result.Elapsed.TotalMilliseconds:0}ms"
                });
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_disposed || _currentItem != currentItem) return;
                    var saved = await _context.ExecuteAsync(cmd, rebuild: false, cancellationToken: _token, recordUndo: false);
                    if (saved.Succeeded) ApplyResult(result);
                }).Task.Unwrap();
            }
            else
            {
                await Dispatcher.InvokeAsync(() => { if (!_disposed) _upscaleButton.SetError(result.ErrorMessage ?? "Falha ao ampliar imagem."); });
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.InvokeAsync(() => { if (!_disposed) _upscaleButton.Reset($"Ampliar Imagem ({_options.Factor.ToWireString()})"); });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => { if (!_disposed) _upscaleButton.SetError(ex.Message); });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => _isProcessing = false);
        }
    }

    private void ApplyResult(UpscaleResult result)
    {
        if (_currentItem is null) return;
        _currentItem.Result = result;
        _upscaleButton.SetSuccess("✓ Ampliado com Sucesso!");
        _resultDimensionsText.Text = $"{result.UpscaledWidth} × {result.UpscaledHeight} px ({result.Factor.ToWireString()})";
        _resultSpeedText.Text = $"⚡ {result.Elapsed.TotalMilliseconds:0} ms · {FormatBytes(result.UpscaledSizeBytes)}";
        _resultCard.Visibility = Visibility.Visible;
        _compareButton.Visibility = Visibility.Visible;
        _addToBoardButton.Visibility = Visibility.Visible;
    }

    private async Task RestoreSharedFilesAsync()
    {
        if (_restored || _disposed) return;
        _restored = true;
        try
        {
            var input = await WindowsSharedAssetFiles.MaterializeAsync(_context, _context.State.GetString(UpscaleSharedState.InputAsset), _token);
            if (input is null) return;
            var item = UpscaleItem.FromPath(input);
            if (item is null) return;
            var output = await WindowsSharedAssetFiles.MaterializeAsync(_context, _context.State.GetString(UpscaleSharedState.OutputAsset), _token);
            var info = UpscaleSharedState.ReadOutput(_context.State);
            await Dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                ApplyInput(item);
                if (output is not null && info is not null)
                    ApplyResult(new UpscaleResult(input, output, item.Width, item.Height, info.Width, info.Height,
                        item.FileSizeBytes, new FileInfo(output).Length, TimeSpan.FromMilliseconds(info.ElapsedMs),
                        EnumExtensions.ParseUpscaleFactor(info.Factor), EnumExtensions.ParseUpscaleModel(info.Model), info.Engine, true));
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.InvokeAsync(() => { if (!_disposed) _upscaleButton.SetError("Não foi possível abrir a imagem compartilhada."); });
        }
    }

    private void OpenBeforeAfterOverlay()
    {
        if (_currentItem?.Result == null || !File.Exists(_currentItem.Result.OutputPath)) return;

        try
        {
            // Carregar bitmaps
            var origUri = new Uri(_currentItem.SourcePath, UriKind.Absolute);
            var upscaledUri = new Uri(_currentItem.Result.OutputPath, UriKind.Absolute);

            var origBitmap = new BitmapImage();
            origBitmap.BeginInit();
            origBitmap.CacheOption = BitmapCacheOption.OnLoad;
            origBitmap.UriSource = origUri;
            origBitmap.EndInit();
            origBitmap.Freeze();

            var upscaledBitmap = new BitmapImage();
            upscaledBitmap.BeginInit();
            upscaledBitmap.CacheOption = BitmapCacheOption.OnLoad;
            upscaledBitmap.UriSource = upscaledUri;
            upscaledBitmap.EndInit();
            upscaledBitmap.Freeze();

            string origDim = $"{_currentItem.Result.OriginalWidth}×{_currentItem.Result.OriginalHeight}";
            string upscaledDim = $"{_currentItem.Result.UpscaledWidth}×{_currentItem.Result.UpscaledHeight}";

            var splitOverlay = new BeforeAfterSplitOverlay(
                _context,
                origBitmap,
                upscaledBitmap,
                origDim,
                upscaledDim,
                onClose: () =>
                {
                    _overlayContainer.Child = null;
                    _overlayContainer.Visibility = Visibility.Collapsed;
                    _mainContentGrid.Visibility = Visibility.Visible;
                });

            _mainContentGrid.Visibility = Visibility.Collapsed;
            _overlayContainer.Child = splitOverlay;
            _overlayContainer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _context.RequestHostAction(new PluginHostAction(PluginHostActionKind.ShowMessage, $"Não foi possível abrir o comparador: {ex.Message}"));
        }
    }

    private void AddResultToBoard()
    {
        if (_currentItem?.Result == null || !File.Exists(_currentItem.Result.OutputPath)) return;

        var boardFile = WindowsPluginFiles.FromPath(_currentItem.Result.OutputPath);
        _context.RequestHostAction(PluginHostAction.AddFiles(new PluginBoardFileRequest(new[] { boardFile })));
        _context.RequestHostAction(new PluginHostAction(PluginHostActionKind.ShowMessage, "Imagem ampliada adicionada à mesa!"));
    }

    private void SyncSettingsToHost()
    {
        var cmd = new PluginCommand(ImageUpscalerCommands.SetSettings, new Dictionary<string, string>
        {
            [ImageUpscalerState.ScaleFactor] = _options.Factor.ToWireString(),
            [ImageUpscalerState.Model] = _options.Model.ToWireString(),
            [ImageUpscalerState.Engine] = _options.Engine.ToWireString(),
            [ImageUpscalerState.Sharpness] = _options.Sharpness.ToString(CultureInfo.InvariantCulture),
            [ImageUpscalerState.Denoise] = _options.Denoise.ToWireString(),
            [ImageUpscalerState.OutputFormat] = _options.Format.ToWireString()
        });

        _ = _context.ExecuteAsync(cmd, rebuild: false, cancellationToken: _token, recordUndo: false);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):0.0} KB";
        return $"{(bytes / (1024.0 * 1024.0)):0.0} MB";
    }
}
