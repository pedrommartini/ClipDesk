using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipDesk.Core;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.PluginSdk;
using ClipDesk.Services;

namespace ClipDesk.Views;

public sealed partial class BoardObjectView
{
    public readonly record struct PluginChoice(string Code, string Label);

    private static readonly PluginChoice[] LanguageChoices =
    [
        new("auto", "Detectar idioma"), new("en", "Inglês"), new("pt-BR", "Português (Brasil)"),
        new("pt-PT", "Português (Portugal)"), new("es", "Espanhol"), new("fr", "Francês"),
        new("de", "Alemão"), new("it", "Italiano"), new("nl", "Holandês"), new("pl", "Polonês"),
        new("ru", "Russo"), new("uk", "Ucraniano"), new("tr", "Turco"), new("ar", "Árabe"),
        new("he", "Hebraico"), new("hi", "Hindi"), new("bn", "Bengali"), new("zh-CN", "Chinês simplificado"),
        new("zh-TW", "Chinês tradicional"), new("ja", "Japonês"), new("ko", "Coreano"),
        new("id", "Indonésio"), new("ms", "Malaio"), new("th", "Tailandês"), new("vi", "Vietnamita"),
        new("sv", "Sueco"), new("no", "Norueguês"), new("da", "Dinamarquês"), new("fi", "Finlandês"),
        new("cs", "Tcheco"), new("sk", "Eslovaco"), new("ro", "Romeno"), new("hu", "Húngaro"),
        new("el", "Grego"), new("bg", "Búlgaro"), new("hr", "Croata"), new("sr", "Sérvio"),
        new("ca", "Catalão"), new("eu", "Basco"), new("gl", "Galego")
    ];

    private static readonly PluginChoice[] FallbackCurrencyChoices =
    [
        new("AUD", "Dólar australiano"), new("BGN", "Lev búlgaro"), new("BRL", "Real brasileiro"),
        new("CAD", "Dólar canadense"), new("CHF", "Franco suíço"), new("CNY", "Yuan chinês"),
        new("CZK", "Coroa tcheca"), new("DKK", "Coroa dinamarquesa"), new("EUR", "Euro"),
        new("GBP", "Libra esterlina"), new("HKD", "Dólar de Hong Kong"), new("HUF", "Forint húngaro"),
        new("IDR", "Rupia indonésia"), new("ILS", "Novo shekel israelense"), new("INR", "Rupia indiana"),
        new("ISK", "Coroa islandesa"), new("JPY", "Iene japonês"), new("KRW", "Won sul-coreano"),
        new("MXN", "Peso mexicano"), new("MYR", "Ringgit malaio"), new("NOK", "Coroa norueguesa"),
        new("NZD", "Dólar neozelandês"), new("PHP", "Peso filipino"), new("PLN", "Zlóti polonês"),
        new("RON", "Leu romeno"), new("SEK", "Coroa sueca"), new("SGD", "Dólar de Singapura"),
        new("THB", "Baht tailandês"), new("TRY", "Lira turca"), new("USD", "Dólar americano"),
        new("ZAR", "Rand sul-africano")
    ];

    private Border? _pluginSurface;
    private string? _pluginSignature;
    private bool _pluginEditing;
    private bool _buildingPlugin;
    private IReadOnlyList<PluginChoice> _currencyChoices = FallbackCurrencyChoices;
    private double _pluginHeaderHeight = 48;
    private RowDefinition? _pluginHeaderRow;
    private Button? _pluginEditButton;
    private Size _pluginLayoutSize = Size.Empty;
    private DispatcherTimer? _utilityTimer;
    private TextBlock? _pluginResultText;
    private TextBlock? _pluginStatusText;
    private bool _utilityLoading;
    private static readonly WindowsPluginLoader ExternalPlugins = new();
    public static void InvalidateExternalPlugin(string pluginId) => ExternalPlugins.Invalidate(pluginId);

    private bool IsPluginKind => Object.Kind is BoardObjectKind.Checklist or BoardObjectKind.Calculator or BoardObjectKind.Translator
        or BoardObjectKind.CurrencyConverter || Object.Kind == BoardObjectKind.Plugin && !string.IsNullOrWhiteSpace(Object.PluginId);
    public bool IsEditingPluginInput => (Object.Kind is BoardObjectKind.Translator or BoardObjectKind.CurrencyConverter)
        && _pluginSurface?.IsKeyboardFocusWithin == true;
    private void ScheduleUtilityRefresh()
    {
        if (Object.Kind is not (BoardObjectKind.Translator or BoardObjectKind.CurrencyConverter)) return;
        var pluginId = Object.PluginId ?? BoardPluginIdentity.FromKind(Object.Kind);
        if (pluginId is not null && ExternalPlugins.Manifest(pluginId)?.Runtime == "wpf-v1") return;
        if (_utilityTimer is null)
        {
            _utilityTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
            _utilityTimer.Tick += (_, _) =>
            {
                _utilityTimer.Stop();
                WidgetActionRequested?.Invoke(this, Object.Kind == BoardObjectKind.Translator ? "translate" : "convert");
            };
        }
        _utilityTimer.Stop();
        _utilityTimer.Start();
    }
    public void StartAutomaticUtility() => ScheduleUtilityRefresh();
    public void SetUtilityLoading(bool loading) { _utilityLoading = loading; UpdatePluginResult(); }
    public void UpdatePluginResult()
    {
        if (_pluginResultText is not null)
            _pluginResultText.Text = Object.Kind == BoardObjectKind.Translator
                ? Object.Content.GetValueOrDefault("output", "A tradução aparece aqui")
                : Object.Content.GetValueOrDefault("result", "Escolha as moedas");
        if (_pluginStatusText is not null)
            _pluginStatusText.Text = _utilityLoading
                ? Object.Kind == BoardObjectKind.Translator ? "Traduzindo…" : "Convertendo…"
                : "Atualização automática";
    }
    private Rect PluginHeaderBounds => new(ContentBounds.Left, ContentBounds.Top, ContentBounds.Width, Math.Min(ContentBounds.Height, _pluginHeaderHeight));

    public void SetCurrencyChoices(IEnumerable<PluginChoice> choices)
    {
        var normalized = choices.Where(choice => !string.IsNullOrWhiteSpace(choice.Code))
            .Select(choice => FallbackCurrencyChoices.FirstOrDefault(local => local.Code == choice.Code) is { Label: not null } translated ? translated : choice)
            .DistinctBy(choice => choice.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(choice => choice.Code, StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0) return;
        _currencyChoices = normalized;
        _pluginSignature = null;
        RefreshPluginSurface();
    }

    private void RefreshPluginSurface()
    {
        if (!IsPluginKind)
        {
            if (_pluginSurface is not null) Children.Remove(_pluginSurface);
            _pluginSurface = null;
            _pluginSignature = null;
            _pluginEditButton = null;
            return;
        }

        var pluginId = Object.PluginId ?? BoardPluginIdentity.FromKind(Object.Kind);
        var activeVersion = pluginId is null ? null : ExternalPlugins.ActiveVersion(pluginId);
        var signature = $"{Object.Kind}|{activeVersion}|{IsDarkMode}|{_pluginEditing}|{_currencyChoices.Count}|" +
                        string.Join('|', Object.Content.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}"));
        if (_pluginSurface is null || !string.Equals(signature, _pluginSignature, StringComparison.Ordinal))
        {
            Children.Clear();
            _pluginSurface = BuildPluginSurface();
            Children.Add(_pluginSurface);
            _pluginSignature = signature;
            _pluginLayoutSize = Size.Empty;
        }

        if (_pluginSurface.RenderTransform is not RotateTransform rotation)
            _pluginSurface.RenderTransform = rotation = new RotateTransform();
        rotation.Angle = Object.Rotation;
        var size = new Size(Math.Max(8, Object.Width), Math.Max(8, Object.Height));
        if (_pluginLayoutSize == size) return;
        _pluginLayoutSize = size;

        var scale = PluginScale();
        _pluginHeaderHeight = Math.Clamp(48 * scale, 38, Math.Max(38, Object.Height * .27));
        if (_pluginHeaderRow is not null) _pluginHeaderRow.Height = new GridLength(_pluginHeaderHeight);
        if (_pluginEditButton is not null)
        {
            _pluginEditButton.Width = Math.Clamp(34 * scale, 32, 260);
            _pluginEditButton.Height = Math.Clamp(30 * scale, 30, 220);
            _pluginEditButton.Margin = new Thickness(Math.Clamp(4 * scale, 3, 24), Math.Clamp(3 * scale, 2, 18), Math.Clamp(9 * scale, 6, 54), Math.Clamp(3 * scale, 2, 18));
        }
        _pluginSurface.Width = Math.Max(8, Object.Width);
        _pluginSurface.Height = Math.Max(8, Object.Height);
        SetLeft(_pluginSurface, SelectionPadding);
        SetTop(_pluginSurface, RotationSpace);
        _pluginSurface.RenderTransformOrigin = new Point(.5, .5);
        ApplyResponsiveMetrics(_pluginSurface, scale);
    }

    private Border BuildPluginSurface()
    {
        _buildingPlugin = true;
        _pluginEditButton = null;
        try
        {
            var scale = PluginScale();
            var shell = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1.1),
                BorderBrush = ColorBrush(IsDarkMode ? "#536A88" : "#CBD5E1"),
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(IsDarkMode ? "#FC1C2637" : "#FFFFFFFF"),
                    (Color)ColorConverter.ConvertFromString(IsDarkMode ? "#FA121B29" : "#F4F7FB"), 105),
                ClipToBounds = true
            };
            var root = new Grid { Background = Brushes.Transparent };
            var headerRow = _pluginHeaderRow = new RowDefinition { Height = new GridLength(Math.Clamp(48 * scale, 38, 76)) };
            root.RowDefinitions.Add(headerRow);
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(BuildPluginHeader());
            var pluginId = Object.PluginId ?? BoardPluginIdentity.FromKind(Object.Kind);
            var externalBody = pluginId is null ? null : ExternalPlugins.CreateBody(pluginId,
                new PluginState(Object.Content), IsDarkMode, _pluginEditing, Object.Width, Object.Height, scale, PluginAccentColor(),
                () => WidgetActionRequested?.Invoke(this, "plugin:before-change"),
                (state, rebuild) =>
                {
                    if (_buildingPlugin) return;
                    Object.Content = state.ToDictionary();
                    MarkPluginChanged();
                    if (rebuild) { _pluginSignature = null; RefreshPluginSurface(); }
                }, HandlePluginHostAction);
            externalBody ??= pluginId is null ? null : ExternalPlugins.CreateBody(pluginId,
                new PluginViewContext(Object.Content, IsDarkMode, _pluginEditing, Object.Width, Object.Height, scale,
                    rebuild =>
                    {
                        if (_buildingPlugin) return;
                        MarkPluginChanged();
                        if (rebuild) { _pluginSignature = null; RefreshPluginSurface(); }
                    }, action => WidgetActionRequested?.Invoke(this, action)));
            var body = externalBody ?? (Object.Kind switch
            {
                BoardObjectKind.Checklist => BuildChecklistBody(),
                BoardObjectKind.Calculator => BuildCalculatorBody(),
                BoardObjectKind.Translator => BuildTranslatorBody(),
                BoardObjectKind.CurrencyConverter => BuildCurrencyBody(),
                _ => BuildUnavailableBody()
            });
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            shell.Child = root;
            return shell;
        }
        finally { _buildingPlugin = false; }
    }

    private void HandlePluginHostAction(PluginHostAction action)
    {
        switch (action.Kind)
        {
            case PluginHostActionKind.CopyToClipboard when !string.IsNullOrEmpty(action.Value):
                Clipboard.SetText(action.Value);
                break;
            case PluginHostActionKind.OpenUri when Uri.TryCreate(action.Value, UriKind.Absolute, out var uri)
                                                   && uri.Scheme is "https" or "http":
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                break;
            case PluginHostActionKind.ShowMessage:
                WidgetActionRequested?.Invoke(this, $"plugin:message:{action.Value}");
                break;
        }
    }

    private Grid BuildPluginHeader()
    {
        var (glyph, title) = Object.Kind switch
        {
            BoardObjectKind.Checklist => ("✓", Object.Content.GetValueOrDefault("title", "Checklist")),
            BoardObjectKind.Calculator => ("∑", "Calculadora"),
            BoardObjectKind.Translator => ("A", "Tradutor"),
            BoardObjectKind.CurrencyConverter => ("$", "Conversor de moeda"),
            _ => ("◈", ExternalPlugins.Manifest(Object.PluginId ?? "")?.Name ?? "Plugin")
        };
        var accent = PluginAccentColor();
        var header = new Grid { Background = ColorBrush("#12FFFFFF"), Tag = "plugin-header" };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = ResponsiveText(glyph, 21, accent, FontWeights.SemiBold);
        icon.Margin = new Thickness(15, 0, 9, 0); icon.VerticalAlignment = VerticalAlignment.Center;
        var label = ResponsiveText(title, 15, IsDarkMode ? "#F5F7FF" : "#223047", FontWeights.SemiBold);
        label.VerticalAlignment = VerticalAlignment.Center; label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(label, 1);
        header.Children.Add(icon); header.Children.Add(label);
        if (Object.Kind == BoardObjectKind.Checklist)
        {
            var edit = _pluginEditButton = FlatButton(_pluginEditing ? "✓" : "✎", false);
            edit.Tag = "plugin-interactive";
            edit.ToolTip = _pluginEditing ? "Concluir edição" : "Editar conteúdo";
            edit.Width = 34; edit.Height = 30; edit.Margin = new Thickness(4, 3, 9, 3);
            edit.Opacity = _pluginEditing ? 1 : .92;
            edit.Background = ColorBrush(IsDarkMode ? "#354661" : "#E2E8F0");
            if(edit.Content is TextBlock editGlyph) editGlyph.Tag = "plugin-font:19";
            edit.Click += (_, args) => { args.Handled = true; _pluginEditing = !_pluginEditing; _pluginSignature = null; RefreshPluginSurface(); WidgetActionRequested?.Invoke(this, "persist"); };
            Grid.SetColumn(edit, 2); header.Children.Add(edit);
        }
        return header;
    }

    private FrameworkElement BuildChecklistBody()
    {
        var body = new Grid { Margin = new Thickness(13, 9, 13, 12), Background = Brushes.Transparent };
        body.RowDefinitions.Add(new RowDefinition { Height = _pluginEditing ? GridLength.Auto : new GridLength(0) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = _pluginEditing ? GridLength.Auto : new GridLength(0) });
        if (_pluginEditing)
        {
            var title = PluginTextBox(Object.Content.GetValueOrDefault("title", "Checklist"), false, 14);
            title.Margin = new Thickness(0, 0, 0, 7);
            title.TextChanged += (_, _) => { if (_buildingPlugin) return; Object.Content["title"] = string.IsNullOrWhiteSpace(title.Text) ? "Checklist" : title.Text; MarkPluginChanged(); };
            body.Children.Add(title);
        }
        var items = ChecklistItems();
        var checkedItems = CheckedChecklistItems();
        var list = new StackPanel();
        for (var index = 0; index < items.Count; index++)
        {
            var itemIndex = index;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 3), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = _pluginEditing ? GridLength.Auto : new GridLength(0) });
            var toggle = FlatButton(checkedItems.Contains(index) ? "✓" : "", checkedItems.Contains(index));
            toggle.Tag = "plugin-interactive"; toggle.Width = 27; toggle.Height = 27; toggle.Margin = new Thickness(0, 0, 9, 0);
            toggle.Click += (_, args) => { args.Handled = true; WidgetActionRequested?.Invoke(this, $"checklist:{itemIndex}"); };
            row.Children.Add(toggle);
            FrameworkElement text;
            if (_pluginEditing)
            {
                var input = PluginTextBox(items[index], false, 14); input.BorderThickness = new Thickness(0); input.Padding = new Thickness(3, 2, 3, 2);
                input.TextChanged += (_, _) => { if (_buildingPlugin) return; UpdateChecklistItem(itemIndex, input.Text); };
                text = input;
            }
            else
            {
                var label = ResponsiveText(items[index], 14, checkedItems.Contains(index) ? "#7E8DA3" : (IsDarkMode ? "#E8EDF7" : "#2A374B"));
                label.TextWrapping = TextWrapping.Wrap; label.TextDecorations = checkedItems.Contains(index) ? TextDecorations.Strikethrough : null;
                label.VerticalAlignment = VerticalAlignment.Center; text = label;
            }
            Grid.SetColumn(text, 1); row.Children.Add(text);
            if (_pluginEditing)
            {
                var remove = FlatButton("×", false); remove.Tag = "plugin-interactive"; remove.Width = 28; remove.Height = 27; remove.Margin = new Thickness(7, 0, 0, 0); remove.Opacity = .72;
                remove.Click += (_, args) => { args.Handled = true; RemoveChecklistItem(itemIndex); };
                Grid.SetColumn(remove, 2); row.Children.Add(remove);
            }
            list.Children.Add(row);
        }
        if (items.Count == 0)
        {
            var empty = ResponsiveText(_pluginEditing ? "Adicione o primeiro item" : "Checklist vazio", 13, "#8797AD");
            empty.Margin = new Thickness(3, 8, 0, 0); list.Children.Add(empty);
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        if (_pluginEditing)
        {
            var add = FlatButton("＋  Adicionar item", true); add.Tag = "plugin-interactive"; add.Margin = new Thickness(0, 7, 0, 0); add.MinHeight = 30;
            add.Click += (_, args) => { args.Handled = true; AddChecklistItem(); };
            Grid.SetRow(add, 2); body.Children.Add(add);
        }
        return body;
    }

    private FrameworkElement BuildCalculatorBody()
    {
        var body = new Grid { Margin = new Thickness(12, 10, 12, 12), Background = Brushes.Transparent };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.22, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.78, GridUnitType.Star) });
        var display = ResponsiveText(Object.Content.GetValueOrDefault("display", "0"), 27, IsDarkMode ? "#FAFBFF" : "#172033", FontWeights.SemiBold);
        display.HorizontalAlignment = HorizontalAlignment.Stretch; display.VerticalAlignment = VerticalAlignment.Center; display.TextAlignment = TextAlignment.Right; display.TextTrimming = TextTrimming.CharacterEllipsis;
        var displayHost = new Border { Background = ColorBrush(IsDarkMode ? "#A70B1320" : "#EEF2F7"), BorderBrush = ColorBrush(IsDarkMode ? "#344962" : "#D5DEE9"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(11, 2, 11, 2), Margin = new Thickness(0, 0, 0, 9), Child = display };
        body.Children.Add(displayHost);
        var keys = new UniformGrid { Rows = 5, Columns = 4 };
        foreach (var row in CalculatorKeys)
        foreach (var key in row)
        {
            var accent = key is "÷" or "×" or "-" or "+" or "=";
            var button = FlatButton(key == "back" ? "⌫" : key, accent);
            button.Tag = "plugin-interactive"; button.Margin = new Thickness(3.5); button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Click += (_, args) => { args.Handled = true; WidgetActionRequested?.Invoke(this, $"calculator:{key}"); };
            if (button.Content is TextBlock content) { content.TextAlignment = TextAlignment.Center; content.HorizontalAlignment = HorizontalAlignment.Center; content.VerticalAlignment = VerticalAlignment.Center; }
            keys.Children.Add(button);
        }
        Grid.SetRow(keys, 1); body.Children.Add(keys);
        return body;
    }

    private FrameworkElement BuildTranslatorBody()
    {
        var body = new Grid { Margin = new Thickness(13, 9, 13, 11), Background = Brushes.Transparent };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var source = Object.Content.GetValueOrDefault("sourceLanguage", "auto");
        var target = Object.Content.GetValueOrDefault("targetLanguage", "en");
        var row = ChoiceRow(LanguageLabel(source), LanguageLabel(target), out var sourceButton, out var targetButton, out var swapButton);
        sourceButton.Click += (_, args) => { args.Handled = true; ShowChoicePopup(sourceButton, LanguageChoices, source, choice => SetPluginChoice("sourceLanguage", choice.Code)); };
        targetButton.Click += (_, args) => { args.Handled = true; ShowChoicePopup(targetButton, LanguageChoices.Where(choice => choice.Code != "auto"), target, choice => SetPluginChoice("targetLanguage", choice.Code)); };
        swapButton.Click += (_, args) =>
        {
            args.Handled = true;
            Object.Content["sourceLanguage"] = target;
            Object.Content["targetLanguage"] = source == "auto" ? "en" : source;
            var input = Object.Content.GetValueOrDefault("input", ""); var output = Object.Content.GetValueOrDefault("output", "");
            if (!string.IsNullOrWhiteSpace(output)) { Object.Content["input"] = output; Object.Content["output"] = input; }
            RebuildPluginAndPersist();
            ScheduleUtilityRefresh();
        };
        body.Children.Add(row);
        var input = PluginTextBox(Object.Content.GetValueOrDefault("input", ""), true, 14);
        input.ToolTip = "Digite para traduzir automaticamente"; input.Margin = new Thickness(0, 7, 0, 5);
        input.TextChanged += (_, _) => { if (_buildingPlugin) return; Object.Content["input"] = input.Text; MarkPluginChanged(); ScheduleUtilityRefresh(); };
        input.LostKeyboardFocus += (_, _) => WidgetActionRequested?.Invoke(this, "settle");
        Grid.SetRow(input, 1); body.Children.Add(input);
        var output = _pluginResultText = ResponsiveText(Object.Content.GetValueOrDefault("output", "A tradução aparece aqui"), 14, "#BCA9FF");
        output.TextWrapping = TextWrapping.Wrap; output.Margin = new Thickness(4, 6, 4, 3);
        var outputScroll = new ScrollViewer { Content = output, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(outputScroll, 2); body.Children.Add(outputScroll);
        var status = _pluginStatusText = ResponsiveText(_utilityLoading ? "Traduzindo…" : "Atualização automática", 11, IsDarkMode ? "#91A0B7" : "#64748B");
        status.Margin = new Thickness(2, 3, 0, 0);
        Grid.SetRow(status, 3); body.Children.Add(status);
        return body;
    }

    private FrameworkElement BuildCurrencyBody()
    {
        var body = new Grid { Margin = new Thickness(13, 9, 13, 11), Background = Brushes.Transparent };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.55, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.45, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var source = Object.Content.GetValueOrDefault("sourceCurrency", "BRL");
        var target = Object.Content.GetValueOrDefault("targetCurrency", "USD");
        var row = ChoiceRow(source, target, out var sourceButton, out var targetButton, out var swapButton);
        sourceButton.Click += (_, args) => { args.Handled = true; ShowChoicePopup(sourceButton, _currencyChoices, source, choice => SetPluginChoice("sourceCurrency", choice.Code)); };
        targetButton.Click += (_, args) => { args.Handled = true; ShowChoicePopup(targetButton, _currencyChoices, target, choice => SetPluginChoice("targetCurrency", choice.Code)); };
        swapButton.Click += (_, args) => { args.Handled = true; Object.Content["sourceCurrency"] = target; Object.Content["targetCurrency"] = source; RebuildPluginAndPersist(); ScheduleUtilityRefresh(); };
        body.Children.Add(row);
        var amount = PluginTextBox(Object.Content.GetValueOrDefault("amount", "1"), false, 21);
        amount.ToolTip = "Digite para converter automaticamente"; amount.Margin = new Thickness(0, 8, 0, 4);
        amount.TextChanged += (_, _) => { if (_buildingPlugin) return; Object.Content["amount"] = amount.Text; MarkPluginChanged(); ScheduleUtilityRefresh(); };
        amount.LostKeyboardFocus += (_, _) => WidgetActionRequested?.Invoke(this, "settle");
        Grid.SetRow(amount, 1); body.Children.Add(amount);
        var result = _pluginResultText = ResponsiveText(Object.Content.GetValueOrDefault("result", "Escolha as moedas e converta"), 17, "#7EE2BD", FontWeights.SemiBold);
        result.TextWrapping = TextWrapping.Wrap; result.VerticalAlignment = VerticalAlignment.Center; result.Margin = new Thickness(3, 2, 3, 2);
        Grid.SetRow(result, 2); body.Children.Add(result);
        var status = _pluginStatusText = ResponsiveText(_utilityLoading ? "Convertendo…" : "Atualização automática", 11, IsDarkMode ? "#91A0B7" : "#64748B");
        status.Margin = new Thickness(2, 3, 0, 0);
        Grid.SetRow(status, 3); body.Children.Add(status);
        return body;
    }

    private FrameworkElement BuildUnavailableBody() => new TextBlock
    {
        Text = Object.Kind == BoardObjectKind.Plugin
            ? "Este plugin ainda não está instalado neste dispositivo. Abra Mais plug-ins para instalá-lo e usar este objeto da mesa."
            : "Este plugin não está disponível nesta versão do ClipDesk.",
        Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap,
        Foreground = ColorBrush(IsDarkMode ? "#A9B3C7" : "#626C82")
    };

    private Grid ChoiceRow(string leftText, string rightText, out Button left, out Button right, out Button swap)
    {
        var row = new Grid { Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left = FlatButton(leftText + "  ▾", false); right = FlatButton(rightText + "  ▾", false); swap = FlatButton("↔", false);
        foreach (var button in new[] { left, right, swap }) button.Tag = "plugin-interactive";
        left.MinHeight = right.MinHeight = 31; swap.Width = 39; swap.Margin = new Thickness(6, 0, 6, 0);
        Grid.SetColumn(swap, 1); Grid.SetColumn(right, 2); row.Children.Add(left); row.Children.Add(swap); row.Children.Add(right);
        return row;
    }

    private void ShowChoicePopup(Button target, IEnumerable<PluginChoice> source, string current, Action<PluginChoice> selected)
    {
        var all = source.ToArray();
        var popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.Fade };
        var stack = new StackPanel();
        var search = PluginTextBox("", false, 13); search.Tag = "plugin-interactive"; search.Margin = new Thickness(8); search.ToolTip = "Pesquisar";
        var list = new StackPanel();
        void Fill(string query)
        {
            list.Children.Clear();
            var filtered = all.Where(choice => string.IsNullOrWhiteSpace(query) || choice.Code.Contains(query, StringComparison.CurrentCultureIgnoreCase) || choice.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            foreach (var choice in filtered)
            {
                var button = FlatButton($"{choice.Code}   {choice.Label}", choice.Code.Equals(current, StringComparison.OrdinalIgnoreCase));
                button.Tag = "plugin-interactive"; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(6, 2, 6, 2); button.MinHeight = 32;
                button.Click += (_, args) => { args.Handled = true; popup.IsOpen = false; selected(choice); };
                list.Children.Add(button);
            }
            if (filtered.Length == 0) { var none = ResponsiveText("Nenhum resultado", 12, "#8A99AF"); none.Margin = new Thickness(12); list.Children.Add(none); }
        }
        search.TextChanged += (_, _) => Fill(search.Text); Fill("");
        stack.Children.Add(search);
        stack.Children.Add(new ScrollViewer { Content = list, MaxHeight = Math.Clamp(Object.Height * 1.15, 180, 360), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        popup.Child = new Border { Width = Math.Clamp(Math.Max(target.ActualWidth, Object.Width * .72), 230, 390), Background = ColorBrush(IsDarkMode ? "#FF172235" : "#FFFFFFFF"), BorderBrush = ColorBrush("#776AA9E8"), BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(13), Padding = new Thickness(2), Child = stack };
        popup.Opened += (_, _) => { search.Focus(); Keyboard.Focus(search); };
        popup.IsOpen = true;
    }

    private Button FlatButton(string text, bool accent)
    {
        var label = ResponsiveText(text, 13.5, accent ? "#FFFFFF" : (IsDarkMode ? "#E8EDF7" : "#263449"), FontWeights.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
        return new Button
        {
            Content = label, Cursor = Cursors.Hand, FocusVisualStyle = null,
            Background = ColorBrush(accent ? "#5E4BC5" : (IsDarkMode ? "#293750" : "#E6EDF6")),
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(7, 3, 7, 3),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private TextBox PluginTextBox(string text, bool acceptsReturn, double baseFontSize)
    {
        var input = new TextBox
        {
            Text = text, AcceptsReturn = acceptsReturn, TextWrapping = acceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = acceptsReturn ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Segoe UI Variable Text"), FontSize = Responsive(baseFontSize), Tag = $"plugin-font:{baseFontSize.ToString(CultureInfo.InvariantCulture)}",
            Foreground = ColorBrush(IsDarkMode ? "#F0F4FC" : "#263449"), CaretBrush = ColorBrush("#B998FF"),
            Background = ColorBrush(IsDarkMode ? "#7F111A28" : "#F1F5F9"), BorderBrush = ColorBrush(IsDarkMode ? "#31445E" : "#D7E0EA"),
            BorderThickness = new Thickness(1), Padding = new Thickness(8, 5, 8, 5), VerticalContentAlignment = VerticalAlignment.Center
        };
        return input;
    }

    private TextBlock ResponsiveText(string text, double baseFontSize, string color, FontWeight? weight = null) => new()
    {
        Text = text, FontFamily = new FontFamily("Segoe UI Variable Text"), FontSize = Responsive(baseFontSize),
        Tag = $"plugin-font:{baseFontSize.ToString(CultureInfo.InvariantCulture)}", Foreground = ColorBrush(color),
        FontWeight = weight ?? FontWeights.Normal
    };

    private void ApplyResponsiveMetrics(DependencyObject root, double scale)
    {
        if (root is FrameworkElement element && element.Tag is string tag && tag.StartsWith("plugin-font:", StringComparison.Ordinal) &&
            double.TryParse(tag[12..], NumberStyles.Float, CultureInfo.InvariantCulture, out var baseSize))
        {
            var size = Math.Clamp(baseSize * scale, 9, 220);
            if (element is TextBlock textBlock) textBlock.FontSize = size;
            else if (element is Control control) control.FontSize = size;
        }
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++) ApplyResponsiveMetrics(VisualTreeHelper.GetChild(root, index), scale);
    }

    private double PluginScale()
    {
        var (baseWidth, baseHeight) = Object.Kind switch
        {
            BoardObjectKind.Checklist => (320d, 260d), BoardObjectKind.Calculator => (300d, 390d),
            BoardObjectKind.Translator => (370d, 270d), BoardObjectKind.CurrencyConverter => (350d, 245d),
            _ => (ExternalPlugins.Manifest(Object.PluginId ?? "")?.DefaultSize.Width ?? 320d,
                ExternalPlugins.Manifest(Object.PluginId ?? "")?.DefaultSize.Height ?? 240d)
        };
        var widthScale = Math.Max(.1, Object.Width / baseWidth); var heightScale = Math.Max(.1, Object.Height / baseHeight);
        var organic = Math.Sqrt(widthScale * heightScale);
        // A large board object is often viewed with the workspace zoomed out. Let its
        // internal typography grow with the object so it remains readable on screen.
        return Math.Clamp(organic * .72 + Math.Min(widthScale, heightScale) * .28, .70, 10);
    }

    private double Responsive(double value) => Math.Clamp(value * PluginScale(), 9, 220);
    private static SolidColorBrush ColorBrush(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private string PluginAccentColor(string? fallback = null)
    {
        fallback ??= ExternalPlugins.Manifest(Object.PluginId ?? BoardPluginIdentity.FromKind(Object.Kind) ?? "")?.AccentColor
            ?? "#A78BFA";
        var candidate = Object.Style.GetValueOrDefault(PluginStyleKeys.AccentColor, fallback);
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(candidate);
            return color.A == byte.MaxValue ? candidate : fallback;
        }
        catch (FormatException) { return fallback; }
        catch (NotSupportedException) { return fallback; }
    }
    private static string LanguageLabel(string code) => LanguageChoices.FirstOrDefault(choice => choice.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).Label is { Length: > 0 } label ? label : code;

    private bool IsPluginInteractiveSource(DependencyObject? source)
    {
        while (source is not null && source != this)
        {
            if (source is FrameworkElement { Tag: "plugin-interactive" } or TextBox or Button or ScrollViewer) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void SetPluginChoice(string key, string value) { Object.Content[key] = value; RebuildPluginAndPersist(); ScheduleUtilityRefresh(); }
    private void MarkPluginChanged() { Object.UpdatedAt = DateTimeOffset.UtcNow; WidgetActionRequested?.Invoke(this, "persist"); }
    private void RebuildPluginAndPersist() { MarkPluginChanged(); _pluginSignature = null; RefreshPluginSurface(); }

    private void UpdateChecklistItem(int index, string value)
    {
        var items = ChecklistItems(); if (index < 0 || index >= items.Count) return;
        items[index] = value; Object.Content["items"] = string.Join('\n', items); MarkPluginChanged();
    }

    private void AddChecklistItem()
    {
        var items = ChecklistItems(); items.Add("Novo item"); Object.Content["items"] = string.Join('\n', items); RebuildPluginAndPersist();
    }

    private void RemoveChecklistItem(int index)
    {
        var items = ChecklistItems(); if (index < 0 || index >= items.Count) return;
        items.RemoveAt(index); Object.Content["items"] = string.Join('\n', items); Object.Content["checked"] = ""; RebuildPluginAndPersist();
    }
}
