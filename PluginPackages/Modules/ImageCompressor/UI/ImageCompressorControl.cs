using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDesk.Plugin.ImageCompressor.Engines;
using ClipDesk.Plugin.ImageCompressor.Models;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using Microsoft.Win32;

namespace ClipDesk.Plugin.ImageCompressor.UI;

/// <summary>
/// Controle principal de interface do plugin Image Compressor v2.
/// Implementa o redesign horizontal focado em 3 colunas (Entrada | Opções | Saída),
/// modo compacto individual por padrão com alternância para lote,
/// botão de ação com barra de progresso embutida, e comparativo antes/depois
/// interativo com divisor deslizante (split-slider) em tela cheia do plugin.
/// </summary>
public sealed class ImageCompressorControl : Grid
{
    private readonly WindowsPluginViewContext _context;
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationToken _token;
    private bool _disposed;
    private bool _restored;
    private CompressorSharedItem? _sharedSingle;
    private List<CompressorSharedItem> _sharedBatch;
    private readonly ObservableCollection<BatchQueueItem> _queue = new();
    private readonly ImageCompressionOptions _options;
    private readonly double _scale;
    private bool _isCompactLayout = false;

    // Estado da UI
    private bool _isBatchMode = false;
    private BatchQueueItem? _singleItem = null;
    private CompressionResult? _singleResult = null;
    private bool _isCompressing = false;
    private CancellationTokenSource? _projectionCts;

    // Layout principal e colunas
    private readonly Grid _mainContentGrid;
    private readonly TextBlock _statsBlock;
    private readonly Button _toggleModeBtn;

    // Coluna 0: Entrada
    private readonly Border _col0Card;
    private readonly StackPanel _col0Content;
    private readonly Border _dropZoneBorder;
    private readonly StackPanel _dropZoneEmptyStack;
    private readonly StackPanel _dropZoneLoadedStack;
    private readonly Image _singleThumbImage;
    private readonly TextBlock _singleFileNameText;
    private readonly TextBlock _singleFileInfoText;
    private readonly ScrollViewer _batchQueueScrollViewer;
    private readonly StackPanel _batchQueueItemsPanel;
    private readonly TextBlock _batchCountText;
    private readonly Button _switchModeInColBtn;

    // Coluna 1: Opções
    private readonly Border _col1Card;
    private readonly StackPanel _col1Content;
    private readonly PluginSlider _qualitySlider;
    private readonly TextBlock _qualityValueText;
    private readonly TextBlock _projectionText;
    private readonly Button _presetEconomyBtn;
    private readonly Button _presetBalancedBtn;
    private readonly Button _presetFidelityBtn;
    private readonly Button _dimOriginalBtn;
    private readonly Button _dimFullHdBtn;
    private readonly Button _dimHdBtn;
    private readonly Button _fmtOriginalBtn;
    private readonly Button _fmtJpegBtn;
    private readonly Button _fmtPngBtn;

    // Coluna 2: Saída & Ação
    private readonly Border _col2Card;
    private readonly StackPanel _col2Content;
    private readonly PluginProgressButton _compressProgressBtn;
    private readonly Border _resultCard;
    private readonly StackPanel _resultStack;
    private readonly Image _resultThumbImage;
    private readonly TextBlock _resultBadgeText;
    private readonly TextBlock _resultDetailsText;
    private readonly Button _compareBeforeAfterBtn;
    private readonly Button _addToBoardBtn;
    private readonly Button _openFolderBtn;
    private readonly TextBlock _col2PlaceholderText;

    // Camada Full-Plugin de Comparativo Antes/Depois
    private BeforeAfterSplitOverlay? _activeSplitOverlay;

    public ImageCompressorControl(WindowsPluginViewContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        var state = new ImageCompressorState(context.State);
        _options = ImageCompressionOptions.FromState(state);
        _token = _cts.Token;
        _sharedSingle = CompressorSharedState.ReadSingle(context.State);
        _sharedBatch = CompressorSharedState.ReadBatch(context.State);

        bool dark = context.IsDarkMode;
        _scale = Math.Max(1.0, context.Scale);
        double scale = _scale;

        Margin = new Thickness(8 * scale);
        Background = Brushes.Transparent;
        Tag = "plugin-interactive";

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Top Header
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: Main Horizontal 3-Column View

        Unloaded += (_, _) =>
        {
            if (_disposed) return;
            _disposed = true;
            _context.LayoutChanged -= CheckLayout;
            _cts.Cancel();
            _cts.Dispose();
            _projectionCts?.Cancel();
            _projectionCts?.Dispose();
        };

        // Suporte Nativo do DevKit para arquivos soltos do Windows ou da mesa ClipDesk
        Loaded += async (_, _) => await RestoreSharedFilesAsync();
        _context.FilesDropped += async files =>
        {
            if (files != null && files.Count > 0)
            {
                var validPaths = files
                    .Where(f => WicCompressionEngine.IsSupportedExtension(f.Path) && File.Exists(f.Path))
                    .Select(f => f.Path)
                    .ToList();

                if (validPaths.Count > 0)
                {
                    await HandleFilesAddedAsync(validPaths);
                }
            }
            await Task.CompletedTask;
        };

        // =========================================================================
        // ROW 0: HEADER COMPACTO COM ALTERNÂNCIA DE MODO
        // =========================================================================
        var headerGrid = new Grid { Margin = new Thickness(4 * scale, 0, 4 * scale, 6 * scale) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconText = new TextBlock
        {
            Text = "\uEB9F", // Photo icon
            FontFamily = UiStyles.IconFont,
            FontSize = 14 * scale,
            Foreground = UiStyles.GetBrush(_context.AccentColor),
            Margin = new Thickness(0, 0, 6 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(iconText);

        _statsBlock = new TextBlock
        {
            Text = GetFormattedLifetimeStats(state),
            FontFamily = UiStyles.TextFont,
            FontSize = 11 * scale,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(_statsBlock);
        Grid.SetColumn(titleStack, 0);
        headerGrid.Children.Add(titleStack);

        // Botão para alternar entre modo individual e modo em lote
        _toggleModeBtn = PluginButtons.Create(context, "👥 Modo em Lote", primary: false);
        _toggleModeBtn.FontSize = 11 * scale;
        _toggleModeBtn.Padding = new Thickness(8 * scale, 3 * scale, 8 * scale, 3 * scale);
        _toggleModeBtn.Click += (_, _) => ToggleBatchMode();
        Grid.SetColumn(_toggleModeBtn, 1);
        headerGrid.Children.Add(_toggleModeBtn);

        SetRow(headerGrid, 0);
        Children.Add(headerGrid);

        // =========================================================================
        // ROW 1: HORIZONTAL 3-COLUMN CONTAINER
        // =========================================================================
        _mainContentGrid = new Grid
        {
            Margin = new Thickness(0, 4 * scale, 0, 0)
        };

        var cardBg = dark ? UiStyles.GetBrush("#1E293B") : UiStyles.GetBrush("#F8FAFC");
        var borderBr = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0");
        var fg = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A");
        var muted = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B");
        var accent = UiStyles.GetBrush(_context.AccentColor);

        // -------------------------------------------------------------------------
        // COLUNA 0: ENTRADA (Drop zone, Preview individual ou Fila em lote)
        // -------------------------------------------------------------------------
        _col0Card = CreateSectionCard("1. Entrada", "\uEB9F", dark, cardBg, borderBr, fg, muted, accent, scale);
        _col0Content = (StackPanel)_col0Card.Child;

        _dropZoneBorder = new Border
        {
            Background = dark ? UiStyles.GetBrush("#141D2E") : UiStyles.GetBrush("#FFFFFF"),
            BorderBrush = borderBr,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(8 * scale),
            AllowDrop = true,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Stretch,
            Tag = "plugin-interactive"
        };

        SetupDropEvents(_dropZoneBorder, borderBr, accent, dark ? UiStyles.GetBrush("#26354D") : UiStyles.GetBrush("#EDF2F7"), dark ? UiStyles.GetBrush("#141D2E") : UiStyles.GetBrush("#FFFFFF"));

        // Estado Vazio do Drop Zone
        _dropZoneEmptyStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var dropIcon = new TextBlock
        {
            Text = "\uEB9F",
            FontFamily = UiStyles.IconFont,
            FontSize = 26 * scale,
            Foreground = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6 * scale)
        };
        _dropZoneEmptyStack.Children.Add(dropIcon);

        var dropText = new TextBlock
        {
            Text = "Arraste uma imagem aqui",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.5 * scale,
            Foreground = fg,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4 * scale)
        };
        _dropZoneEmptyStack.Children.Add(dropText);

        var pickFileBtn = PluginButtons.Create(context, "+ Selecionar...", primary: false);
        pickFileBtn.FontSize = 11 * scale;
        pickFileBtn.Margin = new Thickness(0, 6 * scale, 0, 0);
        pickFileBtn.Click += async (_, e) => { e.Handled = true; await OpenImageFileDialogAsync(); };
        _dropZoneEmptyStack.Children.Add(pickFileBtn);

        // Estado com Imagem Única Carregada
        _dropZoneLoadedStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

        var thumbBorder = new Border
        {
            Width = 90 * scale,
            Height = 90 * scale,
            CornerRadius = new CornerRadius(6 * scale),
            ClipToBounds = true,
            Background = dark ? UiStyles.GetBrush("#090D16") : UiStyles.GetBrush("#E2E8F0"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6 * scale)
        };
        _singleThumbImage = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(_singleThumbImage, BitmapScalingMode.HighQuality);
        thumbBorder.Child = _singleThumbImage;
        _dropZoneLoadedStack.Children.Add(thumbBorder);

        _singleFileNameText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5 * scale,
            Foreground = fg,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 2 * scale)
        };
        _dropZoneLoadedStack.Children.Add(_singleFileNameText);

        _singleFileInfoText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Foreground = muted,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6 * scale)
        };
        _dropZoneLoadedStack.Children.Add(_singleFileInfoText);

        var loadedActionsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var changeBtn = PluginButtons.Create(context, "Trocar", primary: false);
        changeBtn.FontSize = 10.5 * scale;
        changeBtn.Padding = new Thickness(8 * scale, 2 * scale, 8 * scale, 2 * scale);
        changeBtn.Margin = new Thickness(0, 0, 4 * scale, 0);
        changeBtn.Click += async (_, e) => { e.Handled = true; await OpenImageFileDialogAsync(); };
        loadedActionsRow.Children.Add(changeBtn);

        var removeBtn = PluginButtons.Create(context, "✕", primary: false);
        removeBtn.FontSize = 10.5 * scale;
        removeBtn.Padding = new Thickness(6 * scale, 2 * scale, 6 * scale, 2 * scale);
        removeBtn.Click += (_, e) => { e.Handled = true; ClearSingleItem(); };
        loadedActionsRow.Children.Add(removeBtn);

        _dropZoneLoadedStack.Children.Add(loadedActionsRow);

        var dropContainer = new Grid();
        dropContainer.Children.Add(_dropZoneEmptyStack);
        dropContainer.Children.Add(_dropZoneLoadedStack);
        _dropZoneBorder.Child = dropContainer;
        _dropZoneBorder.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            if (_singleItem == null && !_isBatchMode) await OpenImageFileDialogAsync();
        };

        // Fila em Lote (oculta no modo individual)
        _batchCountText = new TextBlock
        {
            Text = "0 imagens na fila",
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Foreground = muted,
            Margin = new Thickness(0, 0, 0, 4 * scale),
            Visibility = Visibility.Collapsed
        };

        _batchQueueScrollViewer = new ScrollViewer
        {
            MaxHeight = 160 * scale,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 6 * scale),
            AllowDrop = true,
            Tag = "plugin-interactive"
        };
        _batchQueueItemsPanel = new StackPanel();
        _batchQueueScrollViewer.Content = _batchQueueItemsPanel;

        _switchModeInColBtn = new Button
        {
            Content = "Alternar para Modo em Lote 👥",
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Foreground = accent,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6 * scale, 0, 0),
            Tag = "plugin-interactive"
        };
        _switchModeInColBtn.Click += (_, _) => ToggleBatchMode();

        _col0Content.Children.Add(_dropZoneBorder);
        _col0Content.Children.Add(_batchCountText);
        _col0Content.Children.Add(_batchQueueScrollViewer);
        _col0Content.Children.Add(_switchModeInColBtn);

        Grid.SetColumn(_col0Card, 0);
        _mainContentGrid.Children.Add(_col0Card);

        // -------------------------------------------------------------------------
        // COLUNA 1: OPÇÕES (Presets, Qualidade, Formato, Resolução, Projeção)
        // -------------------------------------------------------------------------
        _col1Card = CreateSectionCard("2. Opções", "\uE713", dark, cardBg, borderBr, fg, muted, accent, scale);
        _col1Content = (StackPanel)_col1Card.Child;

        var optStack = new StackPanel { Margin = new Thickness(2 * scale) };

        // Presets rápidos
        var presetHeader = new TextBlock
        {
            Text = "Preset de Otimização:",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11 * scale,
            Foreground = fg,
            Margin = new Thickness(0, 0, 0, 4 * scale)
        };
        optStack.Children.Add(presetHeader);

        var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8 * scale) };
        _presetEconomyBtn = CreatePillButton("Economia (55%)", scale, () => SelectPreset(QualityPreset.Economy, 55));
        _presetBalancedBtn = CreatePillButton("Equilibrado (75%)", scale, () => SelectPreset(QualityPreset.Balanced, 75));
        _presetFidelityBtn = CreatePillButton("Fidelidade (90%)", scale, () => SelectPreset(QualityPreset.Fidelity, 90));
        presetRow.Children.Add(_presetEconomyBtn);
        presetRow.Children.Add(_presetBalancedBtn);
        presetRow.Children.Add(_presetFidelityBtn);
        optStack.Children.Add(presetRow);

        // Slider de Qualidade
        var qRow = new Grid { Margin = new Thickness(0, 0, 0, 4 * scale) };
        qRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        qRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var qLabel = new TextBlock
        {
            Text = "Qualidade:",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11 * scale,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(qLabel, 0);
        qRow.Children.Add(qLabel);

        _qualityValueText = new TextBlock
        {
            Text = $"{_options.Quality}%",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5 * scale,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_qualityValueText, 1);
        qRow.Children.Add(_qualityValueText);
        optStack.Children.Add(qRow);

        _qualitySlider = PluginSliders.Create(context, 10, 100, _options.Quality, val =>
        {
            int q = (int)Math.Round(val);
            _options.Quality = q;
            _qualityValueText.Text = $"{q}%";
            _options.Preset = q switch
            {
                55 => QualityPreset.Economy,
                75 => QualityPreset.Balanced,
                90 => QualityPreset.Fidelity,
                _ => QualityPreset.Custom
            };
            UpdatePresetHighlights();
            ScheduleProjectionUpdate();
            SyncSettingsToHost(rebuild: false);
        }, step: 1);
        optStack.Children.Add(_qualitySlider);

        // Formato de Saída (Original, JPEG, PNG)
        var fmtLabel = new TextBlock
        {
            Text = "Formato de Saída:",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11 * scale,
            Foreground = fg,
            Margin = new Thickness(0, 8 * scale, 0, 4 * scale)
        };
        optStack.Children.Add(fmtLabel);

        var fmtRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6 * scale) };
        _fmtOriginalBtn = CreatePillButton("Original", scale, () => SelectFormat(OutputFormat.Original));
        _fmtJpegBtn = CreatePillButton("JPEG", scale, () => SelectFormat(OutputFormat.Jpeg));
        _fmtPngBtn = CreatePillButton("PNG", scale, () => SelectFormat(OutputFormat.Png));
        fmtRow.Children.Add(_fmtOriginalBtn);
        fmtRow.Children.Add(_fmtJpegBtn);
        fmtRow.Children.Add(_fmtPngBtn);
        optStack.Children.Add(fmtRow);

        // Redimensionamento Máximo
        var dimLabel = new TextBlock
        {
            Text = "Dimensão Máxima:",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11 * scale,
            Foreground = fg,
            Margin = new Thickness(0, 4 * scale, 0, 4 * scale)
        };
        optStack.Children.Add(dimLabel);

        var dimRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8 * scale) };
        _dimOriginalBtn = CreatePillButton("Original", scale, () => SelectDimension(MaxDimension.Original));
        _dimFullHdBtn = CreatePillButton("1920px", scale, () => SelectDimension(MaxDimension.FullHd));
        _dimHdBtn = CreatePillButton("1280px", scale, () => SelectDimension(MaxDimension.Hd));
        dimRow.Children.Add(_dimOriginalBtn);
        dimRow.Children.Add(_dimFullHdBtn);
        dimRow.Children.Add(_dimHdBtn);
        optStack.Children.Add(dimRow);

        // Projeção em tempo real
        var projCard = new Border
        {
            Background = dark ? UiStyles.GetBrush("#141D2E") : UiStyles.GetBrush("#FFFFFF"),
            BorderBrush = borderBr,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6 * scale),
            Padding = new Thickness(8 * scale, 5 * scale, 8 * scale, 5 * scale),
            Margin = new Thickness(0, 4 * scale, 0, 0)
        };
        _projectionText = new TextBlock
        {
            Text = "Adicione uma imagem para calcular estimativa",
            FontFamily = UiStyles.TextFont,
            FontSize = 10 * scale,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap
        };
        projCard.Child = _projectionText;
        optStack.Children.Add(projCard);

        _col1Content.Children.Add(optStack);

        Grid.SetColumn(_col1Card, 1);
        _mainContentGrid.Children.Add(_col1Card);

        // -------------------------------------------------------------------------
        // COLUNA 2: SAÍDA & AÇÃO (Botão de Progresso, Resultado, Comparador e Mesa)
        // -------------------------------------------------------------------------
        _col2Card = CreateSectionCard("3. Saída & Ação", "\uE896", dark, cardBg, borderBr, fg, muted, accent, scale);
        _col2Content = (StackPanel)_col2Card.Child;

        _compressProgressBtn = new PluginProgressButton(context, "Comprimir Imagem", primary: true)
        {
            IsEnabled = false,
            Margin = new Thickness(0, 0, 0, 10 * scale)
        };
        _compressProgressBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await StartCompressionAsync();
        };
        _col2Content.Children.Add(_compressProgressBtn);

        // Card de Resultado (Modo Individual)
        _resultCard = new Border
        {
            Background = dark ? UiStyles.GetBrush("#142A22") : UiStyles.GetBrush("#ECFDF5"),
            BorderBrush = UiStyles.GetBrush("#10B981"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(8 * scale),
            Visibility = Visibility.Collapsed
        };

        _resultStack = new StackPanel();

        var resTopHeader = new Grid { Margin = new Thickness(0, 0, 0, 4 * scale) };
        resTopHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resTopHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badgeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badgeStack.Children.Add(new TextBlock
        {
            Text = "\uE73E", // Checkmark
            FontFamily = UiStyles.IconFont,
            FontSize = 14 * scale,
            Foreground = UiStyles.GetBrush("#10B981"),
            Margin = new Thickness(0, 0, 6 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        _resultBadgeText = new TextBlock
        {
            Text = "Economia de 0%",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5 * scale,
            Foreground = UiStyles.GetBrush("#10B981"),
            VerticalAlignment = VerticalAlignment.Center
        };
        badgeStack.Children.Add(_resultBadgeText);
        Grid.SetColumn(badgeStack, 0);
        resTopHeader.Children.Add(badgeStack);

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
        copyIconBtn.ToolTip = "Copiar caminho da imagem comprimida";
        AutomationProperties.SetName(copyIconBtn, "Copiar caminho da imagem comprimida");
        copyIconBtn.Margin = new Thickness(4 * scale, 0, 0, 0);
        copyIconBtn.Click += (_, e) =>
        {
            e.Handled = true;
            string? textToCopy = _singleResult?.OutputPath;
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
        Grid.SetColumn(copyIconBtn, 1);
        resTopHeader.Children.Add(copyIconBtn);
        _resultStack.Children.Add(resTopHeader);

        // Thumbnail clicável do resultado com hint visual
        var resThumbBorder = new Border
        {
            Width = 90 * scale,
            Height = 80 * scale,
            CornerRadius = new CornerRadius(6 * scale),
            ClipToBounds = true,
            Background = dark ? UiStyles.GetBrush("#090D16") : UiStyles.GetBrush("#E2E8F0"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 4 * scale, 0, 4 * scale),
            ToolTip = "Clique para abrir o Comparativo Antes/Depois em tela cheia",
            Tag = "plugin-interactive"
        };
        _resultThumbImage = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(_resultThumbImage, BitmapScalingMode.HighQuality);
        resThumbBorder.Child = _resultThumbImage;
        resThumbBorder.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            OpenSplitOverlay();
        };
        _resultStack.Children.Add(resThumbBorder);

        _resultDetailsText = new TextBlock
        {
            Text = "Original -> Comprimido",
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Foreground = fg,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6 * scale)
        };
        _resultStack.Children.Add(_resultDetailsText);

        // Botão de Comparativo Antes/Depois com Split Slider
        _compareBeforeAfterBtn = PluginButtons.Create(context, "🔍 Comparar Antes / Depois", primary: false);
        _compareBeforeAfterBtn.FontSize = 11 * scale;
        _compareBeforeAfterBtn.Margin = new Thickness(0, 0, 0, 6 * scale);
        _compareBeforeAfterBtn.Click += (_, e) =>
        {
            e.Handled = true;
            OpenSplitOverlay();
        };
        _resultStack.Children.Add(_compareBeforeAfterBtn);

        // Botão Adicionar à Mesa (DevKit board-files)
        _addToBoardBtn = PluginButtons.Create(context, "Adicionar à Mesa", primary: true);
        _addToBoardBtn.FontSize = 11 * scale;
        _addToBoardBtn.Margin = new Thickness(0, 0, 0, 6 * scale);
        _addToBoardBtn.Click += async (_, e) =>
        {
            e.Handled = true;
            await AddCompletedToBoardAsync();
        };
        _resultStack.Children.Add(_addToBoardBtn);

        // Botão Abrir Pasta
        _openFolderBtn = PluginButtons.Create(context, "Abrir Pasta", primary: false);
        _openFolderBtn.FontSize = 10.5 * scale;
        _openFolderBtn.Click += (_, e) =>
        {
            e.Handled = true;
            OpenOutputFolder();
        };
        _resultStack.Children.Add(_openFolderBtn);

        _resultCard.Child = _resultStack;
        _col2Content.Children.Add(_resultCard);

        _col2PlaceholderText = new TextBlock
        {
            Text = "O resultado, estatísticas de economia e o comparador antes/depois aparecerão aqui após a compressão.",
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8 * scale)
        };
        _col2Content.Children.Add(_col2PlaceholderText);

        Grid.SetColumn(_col2Card, 2);
        _mainContentGrid.Children.Add(_col2Card);

        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Tag = "plugin-interactive",
            Content = _mainContentGrid
        };
        SetRow(scrollViewer, 1);
        Children.Add(scrollViewer);

        ApplyLayout(false);
        SizeChanged += (_, _) => CheckLayout();
        _context.LayoutChanged += CheckLayout;

        // Destaque inicial dos presets
        UpdatePresetHighlights();
        UpdateFormatHighlights();
        UpdateDimensionHighlights();
    }

    private void ApplyLayout(bool compact)
    {
        _mainContentGrid.ColumnDefinitions.Clear();
        _mainContentGrid.RowDefinitions.Clear();

        if (compact)
        {
            _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(_col0Card, 0);
            Grid.SetColumn(_col0Card, 0);

            Grid.SetRow(_col1Card, 1);
            Grid.SetColumn(_col1Card, 0);

            Grid.SetRow(_col2Card, 2);
            Grid.SetColumn(_col2Card, 0);

            _col0Card.Margin = new Thickness(0, 0, 0, 8 * _scale);
            _col1Card.Margin = new Thickness(0, 0, 0, 8 * _scale);
            _col2Card.Margin = new Thickness(0);

            _col0Card.MinHeight = 110 * _scale;
            _col1Card.MinHeight = 0;
            _col2Card.MinHeight = 0;
        }
        else
        {
            _mainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            _mainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            _mainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

            Grid.SetRow(_col0Card, 0);
            Grid.SetColumn(_col0Card, 0);

            Grid.SetRow(_col1Card, 0);
            Grid.SetColumn(_col1Card, 1);

            Grid.SetRow(_col2Card, 0);
            Grid.SetColumn(_col2Card, 2);

            _col0Card.Margin = new Thickness(0, 0, 4 * _scale, 0);
            _col1Card.Margin = new Thickness(4 * _scale, 0, 4 * _scale, 0);
            _col2Card.Margin = new Thickness(4 * _scale, 0, 0, 0);

            _col0Card.MinHeight = 0;
            _col1Card.MinHeight = 0;
            _col2Card.MinHeight = 0;
        }
    }

    private void CheckLayout()
    {
        var w = ActualWidth;
        if (w <= 0) w = _context.Width;
        bool compact = w < 480;
        if (compact != _isCompactLayout || (_mainContentGrid.ColumnDefinitions.Count == 0 && _mainContentGrid.RowDefinitions.Count == 0))
        {
            _isCompactLayout = compact;
            ApplyLayout(compact);
        }

        var isVerySmall = w < 260 || (ActualHeight > 0 && ActualHeight < 260);
        Margin = new Thickness(isVerySmall ? 4 * _scale : 8 * _scale);
    }

    private void ToggleBatchMode(bool persist = true)
    {
        if (_isCompressing || _disposed) return;
        _isBatchMode = !_isBatchMode;
        _toggleModeBtn.Content = _isBatchMode ? "📄 Modo Individual" : "👥 Modo em Lote";
        _switchModeInColBtn.Content = _isBatchMode ? "Voltar ao Modo Individual 📄" : "Alternar para Modo em Lote 👥";

        _batchCountText.Visibility = _isBatchMode ? Visibility.Visible : Visibility.Collapsed;
        _batchQueueScrollViewer.Visibility = _isBatchMode ? Visibility.Visible : Visibility.Collapsed;

        if (_isBatchMode)
        {
            _dropZoneLoadedStack.Visibility = Visibility.Collapsed;
            _dropZoneEmptyStack.Visibility = Visibility.Visible;
            _compressProgressBtn.DefaultLabel = "Comprimir Lote";
            _compressProgressBtn.IsEnabled = _queue.Count > 0 && !_isCompressing;
            RefreshBatchQueueList();
        }
        else
        {
            _compressProgressBtn.DefaultLabel = "Comprimir Imagem";
            _compressProgressBtn.IsEnabled = _singleItem != null && !_isCompressing;
            UpdateSingleItemView();
        }

        if (persist)
        {
            PersistSharedItems();
            ScheduleProjectionUpdate();
        }
    }

    private async Task HandleFilesAddedAsync(List<string> paths)
    {
        if (paths.Count == 0 || _isCompressing || _disposed) return;
        if (_context.SharedAssets is null)
        {
            _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Atualize o ClipDesk para compartilhar imagens deste plugin."));
            return;
        }
        bool batch = _isBatchMode;
        foreach (var path in batch ? paths : paths.Take(1))
        {
            if (_disposed) return;
            if (batch && _sharedBatch.Count >= CompressorSharedState.MaximumBatchItems)
            {
                _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "O lote compartilhado aceita até 100 imagens."));
                break;
            }
            try
            {
                var asset = await _context.SharedAssets.ImportAsync(WindowsPluginFile.FromPath(path), _token);
                var item = CreateQueueItem(path, asset.Id);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_disposed) return;
                    var shared = new CompressorSharedItem(asset.Id);
                    if (batch)
                    {
                        _queue.Add(item);
                        _sharedBatch.Add(shared);
                        RefreshBatchQueueList();
                    }
                    else
                    {
                        _singleItem = item;
                        _singleResult = null;
                        _sharedSingle = shared;
                        UpdateSingleItemView();
                        _resultCard.Visibility = Visibility.Collapsed;
                        _col2PlaceholderText.Visibility = Visibility.Visible;
                    }
                    PersistSharedItems();
                    _compressProgressBtn.IsEnabled = true;
                    ScheduleProjectionUpdate();
                });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => { if (!_disposed) _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Não foi possível compartilhar a imagem: " + ex.Message)); });
            }
        }
    }

    private void PersistSharedItems(bool recordUndo = true)
    {
        if (_disposed) return;
        if (recordUndo) _context.NotifyBeforeChange();
        _context.Commit(CompressorSharedState.WithItems(_context.State, _sharedSingle, _sharedBatch, _isBatchMode), rebuild: false);
    }

    private static BatchQueueItem CreateQueueItem(string path, string? assetId = null)
    {
        var fi = new FileInfo(path);
        var dims = WicCompressionEngine.ReadImageDimensions(path);
        return new BatchQueueItem(path, fi.Length, dims.Width, dims.Height, assetId)
        {
            Thumbnail = WicCompressionEngine.LoadThumbnail(path, 120)
        };
    }

    private void UpdateSingleItemView()
    {
        if (_singleItem == null)
        {
            _dropZoneEmptyStack.Visibility = Visibility.Visible;
            _dropZoneLoadedStack.Visibility = Visibility.Collapsed;
            _compressProgressBtn.IsEnabled = false;
        }
        else
        {
            _dropZoneEmptyStack.Visibility = Visibility.Collapsed;
            _dropZoneLoadedStack.Visibility = Visibility.Visible;
            _singleThumbImage.Source = _singleItem.Thumbnail ?? WicCompressionEngine.LoadThumbnail(_singleItem.SourcePath, 120);
            _singleFileNameText.Text = _singleItem.FileName;
            _singleFileInfoText.Text = $"{_singleItem.OriginalWidth}×{_singleItem.OriginalHeight} • {UiStyles.FormatBytes(_singleItem.OriginalSize)}";
            _compressProgressBtn.IsEnabled = !_isCompressing;
        }
    }

    private void ClearSingleItem()
    {
        if (_isCompressing || _disposed) return;
        _sharedSingle = null;
        PersistSharedItems();
        _singleItem = null;
        _singleResult = null;
        UpdateSingleItemView();
        _resultCard.Visibility = Visibility.Collapsed;
        _col2PlaceholderText.Visibility = Visibility.Visible;
        _projectionText.Text = "Adicione uma imagem para calcular estimativa";
    }

    private void RefreshBatchQueueList()
    {
        _batchQueueItemsPanel.Children.Clear();
        _batchCountText.Text = $"{_queue.Count} imagem(ns) na fila";

        double scale = Math.Max(1.0, _context.Scale);
        bool dark = _context.IsDarkMode;

        foreach (var item in _queue)
        {
            var itemBorder = new Border
            {
                Background = dark ? UiStyles.GetBrush("#141D2E") : UiStyles.GetBrush("#FFFFFF"),
                BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6 * scale),
                Padding = new Thickness(4 * scale),
                Margin = new Thickness(0, 0, 0, 3 * scale),
                Tag = "plugin-interactive"
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var thumb = new Image
            {
                Source = item.Thumbnail ?? WicCompressionEngine.LoadThumbnail(item.SourcePath, 64),
                Width = 28 * scale,
                Height = 28 * scale,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 0, 6 * scale, 0)
            };
            Grid.SetColumn(thumb, 0);
            row.Children.Add(thumb);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = item.FileName,
                FontFamily = UiStyles.TextFont,
                FontWeight = FontWeights.SemiBold,
                FontSize = 10 * scale,
                Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textStack.Children.Add(new TextBlock
            {
                Text = UiStyles.FormatBytes(item.OriginalSize),
                FontFamily = UiStyles.TextFont,
                FontSize = 9.5 * scale,
                Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B")
            });
            Grid.SetColumn(textStack, 1);
            row.Children.Add(textStack);

            if (item.Status == BatchItemStatus.Completed && !string.IsNullOrEmpty(item.OutputPath))
            {
                var viewBtn = new Button
                {
                    Content = "🔍",
                    FontFamily = UiStyles.TextFont,
                    FontSize = 10 * scale,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Tag = "plugin-interactive"
                };
                var capturedItem = item;
                viewBtn.Click += (_, _) => OpenSplitOverlayForItem(capturedItem);
                Grid.SetColumn(viewBtn, 2);
                row.Children.Add(viewBtn);
            }

            itemBorder.Child = row;
            _batchQueueItemsPanel.Children.Add(itemBorder);
        }
    }

    private async Task StartCompressionAsync()
    {
        if (_isCompressing || _disposed || _context.SharedAssets is null) return;
        var pending = (_isBatchMode ? _queue.ToList() : _singleItem is null ? [] : new List<BatchQueueItem> { _singleItem })
            .Where(i => i.Status != BatchItemStatus.Completed).ToList();
        if (pending.Count == 0) return;
        var options = ImageCompressionOptions.FromState(new ImageCompressorState(_context.State));
        bool batch = _isBatchMode;
        _isCompressing = true;
        _compressProgressBtn.SetProgress(.02, $"0/{pending.Count}");
        try
        {
            using var workers = new SemaphoreSlim(4);
            var results = await Task.WhenAll(pending.Select(async item =>
            {
                await workers.WaitAsync(_token);
                try { return await Task.Run(() => WicCompressionEngine.CompressImage(item.SourcePath, options, _token), _token); }
                finally { workers.Release(); }
            }));
            for (int index = 0; index < pending.Count; index++)
            {
                _token.ThrowIfCancellationRequested();
                var item = pending[index];
                var result = results[index];
                string? outputId = null;
                if (result.Success && result.OutputPath is not null)
                    outputId = (await _context.SharedAssets.ImportAsync(WindowsPluginFile.FromPath(result.OutputPath), _token)).Id;
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_disposed) return;
                    item.ApplyResult(result);
                    if (outputId is not null)
                    {
                        var entry = new CompressorSharedItem(item.Id, outputId, result.OutputWidth, result.OutputHeight);
                        if (batch)
                        {
                            int position = _sharedBatch.FindIndex(e => e.InputAssetId == item.Id);
                            if (position < 0) return;
                            _sharedBatch[position] = entry;
                        }
                        else
                        {
                            if (_sharedSingle?.InputAssetId != item.Id) return;
                            _sharedSingle = entry;
                            _singleResult = result;
                            ShowSingleResult(result);
                        }
                        PersistSharedItems(recordUndo: false);
                        var statistics = await _context.ExecuteAsync(new PluginCommand(ImageCompressorCommands.RecordCompression,
                            new Dictionary<string, string> { ["filesCount"] = "1", ["bytesSaved"] = result.SavedBytes.ToString() }),
                            rebuild: false, cancellationToken: _token, recordUndo: false);
                        _statsBlock.Text = GetFormattedLifetimeStats(new ImageCompressorState(statistics.State));
                    }
                    RefreshBatchQueueList();
                    _compressProgressBtn.SetProgress((index + 1d) / pending.Count, $"{index + 1}/{pending.Count}");
                }).Task.Unwrap();
            }
            await Dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                if (batch) ShowBatchResults();
                if (results.All(r => r.Success)) _compressProgressBtn.SetSuccess("Concluído!");
                else _compressProgressBtn.SetError("Algumas imagens não foram comprimidas.");
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                _compressProgressBtn.SetError("Não foi possível concluir ou compartilhar a compressão.");
                _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, ex.Message));
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => { _isCompressing = false; if (!_disposed) RefreshBatchQueueList(); });
        }
    }

    private void ShowSingleResult(CompressionResult result)
    {
        if (!result.Success || result.OutputPath is null) return;
        _resultBadgeText.Text = $"Economia de {result.ReductionPercentage:F1}%";
        _resultThumbImage.Source = WicCompressionEngine.LoadThumbnail(result.OutputPath, 160);
        _resultDetailsText.Text = $"{UiStyles.FormatBytes(result.OriginalBytes)} ➔ {UiStyles.FormatBytes(result.CompressedBytes)}\n({result.OutputWidth}×{result.OutputHeight}px)";
        _resultCard.Visibility = Visibility.Visible;
        _col2PlaceholderText.Visibility = Visibility.Collapsed;
        _compareBeforeAfterBtn.Visibility = Visibility.Visible;
    }

    private void ShowBatchResults()
    {
        var completed = _queue.Where(i => i.Status == BatchItemStatus.Completed).ToList();
        if (completed.Count == 0) return;
        _resultCard.Visibility = Visibility.Visible;
        _col2PlaceholderText.Visibility = Visibility.Collapsed;
        _resultBadgeText.Text = $"{completed.Count} imagens comprimidas!";
        _resultDetailsText.Text = $"Total economizado: {UiStyles.FormatBytes(completed.Sum(i => i.SavedBytes))}";
        _compareBeforeAfterBtn.Visibility = Visibility.Collapsed;
    }

    private async Task RestoreSharedFilesAsync()
    {
        if (_restored || _disposed) return;
        _restored = true;
        try
        {
            // Restore references only; do not run compression/estimation on the receiving computer.
            var entries = _sharedBatch.Concat(_sharedSingle is null ? [] : new[] { _sharedSingle }).DistinctBy(e => e.InputAssetId);
            var restored = new Dictionary<string, BatchQueueItem>();
            foreach (var entry in entries)
            {
                var input = await WindowsSharedAssetFiles.MaterializeAsync(_context, entry.InputAssetId, _token);
                if (input is null) continue;
                var item = CreateQueueItem(input, entry.InputAssetId);
                var output = await WindowsSharedAssetFiles.MaterializeAsync(_context, entry.OutputAssetId, _token);
                if (output is not null)
                    item.ApplyResult(CompressionResult.Succeeded(input, output, item.OriginalSize, new FileInfo(output).Length,
                        item.OriginalWidth, item.OriginalHeight, entry.OutputWidth, entry.OutputHeight));
                restored[item.Id] = item;
            }
            await Dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                if (_context.State.GetString(CompressorSharedState.BatchMode) == "true") ToggleBatchMode(persist: false);
                foreach (var entry in _sharedBatch)
                    if (restored.TryGetValue(entry.InputAssetId, out var batchItem)) _queue.Add(batchItem);
                if (_sharedSingle is not null && restored.TryGetValue(_sharedSingle.InputAssetId, out var single))
                {
                    _singleItem = single;
                    if (single.Status == BatchItemStatus.Completed && single.OutputPath is not null)
                        _singleResult = CompressionResult.Succeeded(single.SourcePath, single.OutputPath, single.OriginalSize,
                            single.CompressedSize, single.OriginalWidth, single.OriginalHeight, single.OutputWidth, single.OutputHeight);
                }
                if (_isBatchMode) { RefreshBatchQueueList(); ShowBatchResults(); }
                else { UpdateSingleItemView(); if (_singleResult is not null) ShowSingleResult(_singleResult); }
                _compressProgressBtn.IsEnabled = _isBatchMode ? _queue.Count > 0 : _singleItem is not null;
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.InvokeAsync(() => { if (!_disposed) _compressProgressBtn.SetError("Não foi possível abrir as imagens compartilhadas."); });
        }
    }

    private void OpenSplitOverlay()
    {
        if (_singleItem == null || _singleResult == null || string.IsNullOrEmpty(_singleResult.OutputPath) || !File.Exists(_singleResult.OutputPath))
        {
            return;
        }

        try
        {
            var origBitmap = LoadBitmap(_singleItem.SourcePath);
            var compBitmap = LoadBitmap(_singleResult.OutputPath);

            string origInfo = $"{UiStyles.FormatBytes(_singleResult.OriginalBytes)} ({_singleResult.OriginalWidth}×{_singleResult.OriginalHeight})";
            string compInfo = $"{UiStyles.FormatBytes(_singleResult.CompressedBytes)} (-{_singleResult.ReductionPercentage:F0}%)";

            _activeSplitOverlay = new BeforeAfterSplitOverlay(_context, origBitmap, compBitmap, origInfo, compInfo, CloseSplitOverlay);

            SetRow(_activeSplitOverlay, 0);
            SetRowSpan(_activeSplitOverlay, 2);
            Children.Add(_activeSplitOverlay);
        }
        catch (Exception ex)
        {
            _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, $"Erro ao abrir comparativo: {ex.Message}"));
        }
    }

    private void OpenSplitOverlayForItem(BatchQueueItem item)
    {
        if (string.IsNullOrEmpty(item.OutputPath) || !File.Exists(item.OutputPath)) return;

        try
        {
            var origBitmap = LoadBitmap(item.SourcePath);
            var compBitmap = LoadBitmap(item.OutputPath);

            string origInfo = $"{UiStyles.FormatBytes(item.OriginalSize)} ({item.OriginalWidth}×{item.OriginalHeight})";
            string compInfo = $"{UiStyles.FormatBytes(item.CompressedSize)} (-{item.ReductionPercentage:F0}%)";

            _activeSplitOverlay = new BeforeAfterSplitOverlay(_context, origBitmap, compBitmap, origInfo, compInfo, CloseSplitOverlay);

            SetRow(_activeSplitOverlay, 0);
            SetRowSpan(_activeSplitOverlay, 2);
            Children.Add(_activeSplitOverlay);
        }
        catch (Exception ex)
        {
            _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, $"Erro ao abrir comparativo: {ex.Message}"));
        }
    }

    private void CloseSplitOverlay()
    {
        if (_activeSplitOverlay != null)
        {
            Children.Remove(_activeSplitOverlay);
            _activeSplitOverlay = null;
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private async Task AddCompletedToBoardAsync()
    {
        var pathsToAdd = new List<(string Path, string Name)>();

        if (!_isBatchMode && _singleResult != null && !string.IsNullOrEmpty(_singleResult.OutputPath) && File.Exists(_singleResult.OutputPath))
        {
            pathsToAdd.Add((_singleResult.OutputPath, Path.GetFileName(_singleResult.OutputPath)));
        }
        else if (_isBatchMode)
        {
            pathsToAdd.AddRange(_queue
                .Where(i => i.Status == BatchItemStatus.Completed && !string.IsNullOrEmpty(i.OutputPath) && File.Exists(i.OutputPath))
                .Select(i => (i.OutputPath!, i.FileName)));
        }

        if (pathsToAdd.Count == 0) return;

        // Limite estrito de 16 arquivos por lote no host
        int totalAdded = 0;
        foreach (var chunk in pathsToAdd.Chunk(PluginBoardFileRequest.MaximumFiles))
        {
            var boardFiles = chunk.Select(p => WindowsPluginFiles.FromPath(p.Path, p.Name)).ToList();
            var req = new PluginBoardFileRequest(boardFiles);
            _context.RequestHostAction(PluginHostAction.AddFiles(req));
            totalAdded += boardFiles.Count;
        }

        _context.RequestHostAction(new(PluginHostActionKind.ShowMessage, $"{totalAdded} imagem(ns) adicionada(s) à mesa de trabalho!"));
        await Task.CompletedTask;
    }

    private void OpenOutputFolder()
    {
        try
        {
            if (Directory.Exists(_options.OutputDirectory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _options.OutputDirectory,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Shell ignore
        }
    }

    private async Task OpenImageFileDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = _isBatchMode ? "Selecione imagens para a fila" : "Selecione uma imagem para comprimir",
            Multiselect = _isBatchMode,
            Filter = "Imagens Suportadas (*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tiff)|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tiff|Todos os Arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            await HandleFilesAddedAsync(dialog.FileNames.ToList());
        }
    }

    private void ScheduleProjectionUpdate()
    {
        _projectionCts?.Cancel();
        _projectionCts?.Dispose();
        _projectionCts = new CancellationTokenSource();
        var token = _projectionCts.Token;

        string? samplePath = _isBatchMode
            ? _queue.FirstOrDefault()?.SourcePath
            : _singleItem?.SourcePath;

        if (string.IsNullOrEmpty(samplePath) || !File.Exists(samplePath))
        {
            _projectionText.Text = "Adicione uma imagem para estimar economia";
            return;
        }

        _projectionText.Text = "Calculando projeção...";

        Task.Run(async () =>
        {
            try
            {
                var fi = new FileInfo(samplePath);
                long origSize = fi.Length;
                await Task.Delay(120, token);
                token.ThrowIfCancellationRequested();

                var testResult = WicCompressionEngine.CompressImage(samplePath, _options, token);
                if (testResult.Success && !token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _projectionText.Text = $"Estimativa: ~{UiStyles.FormatBytes(testResult.CompressedBytes)} (economia de {testResult.ReductionPercentage:F0}%)";
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                Dispatcher.Invoke(() => _projectionText.Text = "Estimativa pronta");
            }
        }, token);
    }

    private void SelectPreset(QualityPreset preset, int quality)
    {
        _options.Preset = preset;
        _options.Quality = quality;
        _qualitySlider.Value = quality;
        _qualityValueText.Text = $"{quality}%";
        UpdatePresetHighlights();
        ScheduleProjectionUpdate();
        SyncSettingsToHost(rebuild: false);
    }

    private void SelectFormat(OutputFormat format)
    {
        _options.Format = format;
        UpdateFormatHighlights();
        ScheduleProjectionUpdate();
        SyncSettingsToHost(rebuild: false);
    }

    private void SelectDimension(MaxDimension dimension)
    {
        _options.Dimension = dimension;
        UpdateDimensionHighlights();
        ScheduleProjectionUpdate();
        SyncSettingsToHost(rebuild: false);
    }

    private void UpdatePresetHighlights()
    {
        HighlightPill(_presetEconomyBtn, _options.Preset == QualityPreset.Economy);
        HighlightPill(_presetBalancedBtn, _options.Preset == QualityPreset.Balanced);
        HighlightPill(_presetFidelityBtn, _options.Preset == QualityPreset.Fidelity);
    }

    private void UpdateFormatHighlights()
    {
        HighlightPill(_fmtOriginalBtn, _options.Format == OutputFormat.Original);
        HighlightPill(_fmtJpegBtn, _options.Format == OutputFormat.Jpeg);
        HighlightPill(_fmtPngBtn, _options.Format == OutputFormat.Png);
    }

    private void UpdateDimensionHighlights()
    {
        HighlightPill(_dimOriginalBtn, _options.Dimension == MaxDimension.Original);
        HighlightPill(_dimFullHdBtn, _options.Dimension == MaxDimension.FullHd);
        HighlightPill(_dimHdBtn, _options.Dimension == MaxDimension.Hd);
    }

    private void HighlightPill(Button btn, bool active)
    {
        bool dark = _context.IsDarkMode;
        btn.Background = active ? UiStyles.GetBrush(_context.AccentColor) : (dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0"));
        btn.Foreground = active ? UiStyles.GetContrastBrush(_context.AccentColor) : (dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"));
        btn.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
    }

    private Button CreatePillButton(string label, double scale, Action onClick)
    {
        bool dark = _context.IsDarkMode;
        var btn = new Button
        {
            Content = label,
            FontFamily = UiStyles.TextFont,
            FontSize = 10.5 * scale,
            Padding = new Thickness(7 * scale, 3 * scale, 7 * scale, 3 * scale),
            Margin = new Thickness(0, 0, 4 * scale, 0),
            Background = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0"),
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = "plugin-interactive"
        };
        btn.Click += (_, e) => { e.Handled = true; onClick(); };
        return btn;
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
            Padding = new Thickness(8 * scale),
            Margin = new Thickness(4 * scale),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var stack = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6 * scale) };
        header.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = UiStyles.IconFont,
            FontSize = 13 * scale,
            Foreground = accent,
            Margin = new Thickness(0, 0, 6 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.Bold,
            FontSize = 11.5 * scale,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        });
        stack.Children.Add(header);

        borderBox.Child = stack;
        return borderBox;
    }

    private void SetupDropEvents(Border target, Brush borderDefault, Brush borderAccent, Brush bgHover, Brush bgDefault)
    {
        target.DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                target.BorderBrush = borderAccent;
                target.Background = bgHover;
            }
            e.Handled = true;
        };

        target.DragLeave += (_, e) =>
        {
            target.BorderBrush = borderDefault;
            target.Background = bgDefault;
            e.Handled = true;
        };

        target.Drop += async (_, e) =>
        {
            target.BorderBrush = borderDefault;
            target.Background = bgDefault;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var valid = files.Where(WicCompressionEngine.IsSupportedExtension).ToList();
                    if (valid.Count > 0)
                    {
                        await HandleFilesAddedAsync(valid);
                    }
                }
            }
            e.Handled = true;
        };
    }

    private void SyncSettingsToHost(bool rebuild)
    {
        var cmd = new PluginCommand(ImageCompressorCommands.SetSettings, new Dictionary<string, string>
        {
            ["quality"] = _options.Quality.ToString(),
            ["preset"] = _options.Preset.ToString().ToLowerInvariant(),
            ["outputFormat"] = _options.Format.ToString().ToLowerInvariant(),
            ["maxDimension"] = _options.Dimension.ToString().ToLowerInvariant(),
            ["stripMetadata"] = _options.StripMetadata ? "true" : "false"
        });
        _ = _context.ExecuteAsync(cmd, rebuild: rebuild, cancellationToken: _token, recordUndo: false);
    }

    private static string GetFormattedLifetimeStats(ImageCompressorState state)
    {
        if (state.FilesCompressedCount <= 0) return "";
        return $"• {state.FilesCompressedCount:N0} otimizadas ({UiStyles.FormatBytes(state.BytesSavedCount)} economizados)";
    }
}
