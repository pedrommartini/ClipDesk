using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipDesk.Plugin.ImageCompressor.Models;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.ImageCompressor.UI;

/// <summary>
/// Componente visual para exibição de cada item individual da fila de compressão.
/// </summary>
public sealed class BatchQueueItemView : Border
{
    private readonly BatchQueueItem _item;
    private readonly WindowsPluginViewContext _context;
    private readonly Action<BatchQueueItem> _onRemove;

    private readonly TextBlock _titleBlock;
    private readonly TextBlock _detailsBlock;
    private readonly Border _statusBadge;
    private readonly TextBlock _statusBadgeText;
    private readonly Button _removeButton;
    private readonly Image _thumbImage;
    private readonly Border _thumbContainer;

    public BatchQueueItemView(BatchQueueItem item, WindowsPluginViewContext context, Action<BatchQueueItem> onRemove)
    {
        _item = item;
        _context = context;
        _onRemove = onRemove;

        bool dark = context.IsDarkMode;
        double scale = Math.Max(1.0, context.Scale);

        Background = dark ? UiStyles.GetBrush("#1E293B") : UiStyles.GetBrush("#FFFFFF");
        BorderBrush = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8 * scale);
        Padding = new Thickness(8 * scale);
        Margin = new Thickness(0, 0, 0, 6 * scale);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Thumbnail
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Badge
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Remove button

        // 1. Thumbnail Container (44x44)
        _thumbContainer = new Border
        {
            Width = 44 * scale,
            Height = 44 * scale,
            CornerRadius = new CornerRadius(6 * scale),
            Background = dark ? UiStyles.GetBrush("#0F172A") : UiStyles.GetBrush("#F1F5F9"),
            ClipToBounds = true,
            Margin = new Thickness(0, 0, 10 * scale, 0)
        };

        _thumbImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Source = _item.Thumbnail
        };
        _thumbContainer.Child = _thumbImage;
        Grid.SetColumn(_thumbContainer, 0);
        grid.Children.Add(_thumbContainer);

        // 2. Info Stack
        var infoStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8 * scale, 0)
        };

        _titleBlock = new TextBlock
        {
            Text = _item.FileName,
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13 * scale,
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = _item.SourcePath
        };
        infoStack.Children.Add(_titleBlock);

        _detailsBlock = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontSize = 11 * scale,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            Margin = new Thickness(0, 2 * scale, 0, 0)
        };
        infoStack.Children.Add(_detailsBlock);

        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        // 3. Status Badge
        _statusBadge = new Border
        {
            CornerRadius = new CornerRadius(10 * scale),
            Padding = new Thickness(8 * scale, 3 * scale, 8 * scale, 3 * scale),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6 * scale, 0)
        };

        _statusBadgeText = new TextBlock
        {
            FontFamily = UiStyles.TextFont,
            FontSize = 11 * scale,
            FontWeight = FontWeights.Bold
        };
        _statusBadge.Child = _statusBadgeText;

        Grid.SetColumn(_statusBadge, 2);
        grid.Children.Add(_statusBadge);

        // 4. Remove Button
        _removeButton = new Button
        {
            Content = "\uE711", // Close icon
            FontFamily = UiStyles.IconFont,
            FontSize = 10 * scale,
            Width = 24 * scale,
            Height = 24 * scale,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Remover da fila",
            Tag = "plugin-interactive"
        };
        _removeButton.Click += (_, _) => _onRemove(_item);

        Grid.SetColumn(_removeButton, 3);
        grid.Children.Add(_removeButton);

        Child = grid;

        UpdateView();

        _item.PropertyChanged += OnItemPropertyChanged;
        Unloaded += (_, _) => _item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(UpdateView);
    }

    private void UpdateView()
    {
        bool dark = _context.IsDarkMode;

        if (_thumbImage.Source == null && _item.Thumbnail != null)
        {
            _thumbImage.Source = _item.Thumbnail;
        }

        string origSizeStr = UiStyles.FormatBytes(_item.OriginalSize);
        string origDimStr = _item.OriginalWidth > 0 && _item.OriginalHeight > 0
            ? $"{_item.OriginalWidth}×{_item.OriginalHeight}"
            : "";

        switch (_item.Status)
        {
            case BatchItemStatus.Pending:
                _detailsBlock.Text = string.IsNullOrEmpty(origDimStr)
                    ? origSizeStr
                    : $"{origDimStr} • {origSizeStr}";

                _statusBadge.Background = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#F1F5F9");
                _statusBadgeText.Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B");
                _statusBadgeText.Text = "Aguardando";
                _statusBadge.ToolTip = null;
                _removeButton.Visibility = Visibility.Visible;
                break;

            case BatchItemStatus.Compressing:
                _detailsBlock.Text = string.IsNullOrEmpty(origDimStr)
                    ? $"{origSizeStr} • Otimizando..."
                    : $"{origDimStr} • Otimizando...";

                _statusBadge.Background = dark ? UiStyles.GetBrush("#1E3A8A") : UiStyles.GetBrush("#DBEAFE");
                _statusBadgeText.Foreground = UiStyles.GetBrush("#3B82F6");
                _statusBadgeText.Text = "Comprimindo...";
                _statusBadge.ToolTip = null;
                _removeButton.Visibility = Visibility.Collapsed;
                break;

            case BatchItemStatus.Completed:
                string compSizeStr = UiStyles.FormatBytes(_item.CompressedSize);
                string compDimStr = _item.OutputWidth > 0 && _item.OutputHeight > 0
                    ? $"{_item.OutputWidth}×{_item.OutputHeight}"
                    : "";

                _detailsBlock.Text = $"{origSizeStr} → {compSizeStr} ({(string.IsNullOrEmpty(compDimStr) ? origDimStr : compDimStr)})";

                _statusBadge.Background = dark ? UiStyles.GetBrush("#064E3B") : UiStyles.GetBrush("#D1FAE5");
                _statusBadgeText.Foreground = UiStyles.GetBrush("#10B981");
                _statusBadgeText.Text = $"-{_item.ReductionPercentage:F0}%";
                _statusBadge.ToolTip = $"Economia: {UiStyles.FormatBytes(_item.SavedBytes)}";
                _removeButton.Visibility = Visibility.Visible;
                break;

            case BatchItemStatus.Failed:
                _detailsBlock.Text = $"{origSizeStr} • Erro na compressão";
                _statusBadge.Background = dark ? UiStyles.GetBrush("#7F1D1D") : UiStyles.GetBrush("#FEE2E2");
                _statusBadgeText.Foreground = UiStyles.GetBrush("#EF4444");
                _statusBadgeText.Text = "Falha";
                _statusBadge.ToolTip = _item.ErrorMessage ?? "Erro desconhecido";
                _removeButton.Visibility = Visibility.Visible;
                break;
        }
    }
}
