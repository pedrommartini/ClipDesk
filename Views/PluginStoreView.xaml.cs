using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipDesk.Services;

namespace ClipDesk.Views;

public partial class PluginStoreView : UserControl
{
    private IReadOnlyList<PluginCatalogEntry> _entries = [];
    private readonly List<Button> _catalogCards = [];
    private readonly List<Button> _installedCards = [];
    private bool _showingInstalled;

    public PluginStoreView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateCardWidths();
    }

    public event EventHandler? CloseRequested;
    public event Action<PluginCatalogEntry>? PluginActionRequested;

    public void Bind(IReadOnlyList<PluginCatalogEntry> entries)
    {
        _entries = entries.OrderBy(entry => entry.Manifest.SortOrder).ToList();
        var installedCount = _entries.Count(entry => entry.IsInstalled);
        InstalledSummaryTitle.Text = installedCount == 1 ? "1 plugin instalado" : $"{installedCount} plugins instalados";
        InstalledCountText.Text = installedCount == 1 ? "1 plugin disponível neste dispositivo." : $"{installedCount} plugins disponíveis neste dispositivo.";
        CatalogCountText.Text = _entries.Count == 1 ? "1 plugin" : $"{_entries.Count} plugins";
        RebuildCards();
        Dispatcher.BeginInvoke(() => UpdateCardWidths(ActualWidth), DispatcherPriority.Loaded);
    }

    public void ShowCatalog()
    {
        _showingInstalled = false;
        CatalogView.Visibility = Visibility.Visible;
        InstalledView.Visibility = Visibility.Collapsed;
        SearchBox.Text = "";
        ApplyFilter();
        StoreScroller.ScrollToTop();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void RebuildCards()
    {
        PluginCardsPanel.Children.Clear();
        InstalledCardsPanel.Children.Clear();
        _catalogCards.Clear();
        _installedCards.Clear();

        foreach (var entry in _entries)
        {
            var catalogCard = CreatePluginCard(entry);
            _catalogCards.Add(catalogCard);
            PluginCardsPanel.Children.Add(catalogCard);
            if (!entry.IsInstalled) continue;
            var installedCard = CreatePluginCard(entry);
            _installedCards.Add(installedCard);
            InstalledCardsPanel.Children.Add(installedCard);
        }
        UpdateCardWidths();
        ApplyFilter();
    }

    private Button CreatePluginCard(PluginCatalogEntry entry)
    {
        var button = new Button
        {
            Style = (Style)FindResource("PluginCardButtonStyle"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(15, 14, 15, 13),
            Margin = new Thickness(0, 0, 12, 12),
            Height = 150,
            ToolTip = entry.IsInstalled ? $"Adicionar {entry.Manifest.Name} à mesa" : $"Instalar {entry.Manifest.Name}"
        };
        button.Click += (_, _) => PluginActionRequested?.Invoke(entry);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 39,
            Height = 39,
            CornerRadius = new CornerRadius(10)
        };
        icon.SetResourceReference(Border.BackgroundProperty, "StoreAccentWashBrush");
        var iconGlyph = new TextBlock
        {
            Text = entry.Manifest.IconGlyph,
            FontFamily = (FontFamily)FindResource("PluginMaterialSymbols"),
            FontSize = 21,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconGlyph.SetResourceReference(TextBlock.ForegroundProperty, "StoreAccentBrush");
        icon.Child = iconGlyph;
        header.Children.Add(icon);

        var copy = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = entry.Manifest.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        copy.Children.Add(name);

        var identity = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        if (entry.IsInstalled)
        {
            var dot = new Ellipse { Width = 5, Height = 5, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, "StoreSuccessBrush");
            identity.Children.Add(dot);
        }
        var status = new TextBlock
        {
            Text = entry.IsInstalled ? "INSTALADO" : "DISPONÍVEL",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (entry.IsInstalled) status.SetResourceReference(TextBlock.ForegroundProperty, "StoreSuccessBrush");
        else status.SetResourceReference(TextBlock.ForegroundProperty, "StoreAccentBrush");
        identity.Children.Add(status);
        copy.Children.Add(identity);
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        layout.Children.Add(header);

        var description = new TextBlock
        {
            Text = entry.Manifest.Description,
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 11,
            LineHeight = 15,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.WordEllipsis,
            MaxHeight = 30
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetRow(description, 1);
        layout.Children.Add(description);

        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0), Tag = "plugin-footer" };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var meta = new TextBlock
        {
            Tag = "plugin-meta",
            Text = $"v{entry.InstalledVersion ?? entry.Manifest.Version}  ·  Windows",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        footer.Children.Add(meta);

        var action = new Border
        {
            Tag = "plugin-action",
            MinWidth = 99,
            Height = 31,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Center
        };
        action.SetResourceReference(Border.BackgroundProperty, "StoreActionBrush");
        var actionText = new TextBlock
        {
            Tag = "plugin-action-text",
            Text = entry.IsInstalled ? "Adicionar  +" : "Instalar  ↓",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionText.SetResourceReference(TextBlock.ForegroundProperty, "StoreActionTextBrush");
        action.Child = actionText;
        Grid.SetColumn(action, 1);
        footer.Children.Add(action);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        button.Content = layout;
        button.DataContext = entry;
        return button;
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var cards = _showingInstalled ? _installedCards : _catalogCards;
        var visible = 0;
        foreach (var card in cards)
        {
            var entry = (PluginCatalogEntry)card.DataContext;
            var matches = query.Length == 0
                || entry.Manifest.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || entry.Manifest.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            card.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches) visible++;
        }
        EmptyText.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCardWidths(double? outerWidth = null)
    {
        var viewWidth = outerWidth.GetValueOrDefault(ActualWidth);
        var compact = viewWidth < 620;
        var medium = viewWidth >= 620 && viewWidth < 940;
        ContentGrid.Margin = compact ? new Thickness(17, 20, 17, 22)
            : medium ? new Thickness(28, 25, 28, 28)
            : new Thickness(42, 28, 42, 30);
        StoreTitle.FontSize = compact ? 25 : 29;
        StoreSubtitle.FontSize = compact ? 12 : 13;
        SearchSurface.Width = compact ? double.NaN : 380;
        SearchSurface.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        SearchSurface.Margin = compact ? new Thickness(0, 18, 0, 18) : new Thickness(0, 22, 0, 22);

        var horizontalMargin = ContentGrid.Margin.Left + ContentGrid.Margin.Right;
        var contentWidth = Math.Min(1240, Math.Max(260, viewWidth - horizontalMargin));
        var cardPanelWidth = contentWidth - 14;
        PluginCardsPanel.Width = cardPanelWidth;
        InstalledCardsPanel.Width = cardPanelWidth;
        var columns = cardPanelWidth >= 960 ? 3 : cardPanelWidth >= 610 ? 2 : 1;
        var cardWidth = Math.Max(236, Math.Floor((cardPanelWidth - (columns - 1) * 12) / columns));
        foreach (var cards in new[] { _catalogCards, _installedCards })
        {
            for (var index = 0; index < cards.Count; index++)
            {
                cards[index].Width = cardWidth;
                cards[index].Margin = new Thickness(0, 0, index % columns == columns - 1 ? 0 : 12, 12);
            }
        }

        var narrow = contentWidth < 640;
        Grid.SetRow(ShowInstalledButton, narrow ? 1 : 0);
        Grid.SetColumn(ShowInstalledButton, narrow ? 0 : 2);
        Grid.SetColumnSpan(ShowInstalledButton, narrow ? 3 : 1);
        ShowInstalledButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        ShowInstalledButton.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
        InstalledSummaryCopy.Margin = narrow ? new Thickness(11, 0, 0, 0) : new Thickness(12, 0, 16, 0);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ShowInstalledButton_Click(object sender, RoutedEventArgs e)
    {
        _showingInstalled = true;
        CatalogView.Visibility = Visibility.Collapsed;
        InstalledView.Visibility = Visibility.Visible;
        SearchBox.Text = "";
        ApplyFilter();
        UpdateCardWidths();
        StoreScroller.ScrollToTop();
    }

    private void InstalledBackButton_Click(object sender, RoutedEventArgs e) => ShowCatalog();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void PluginStoreView_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCardWidths(e.NewSize.Width);

}
