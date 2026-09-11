using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDesk.Models;

namespace ClipDesk.Views;

public partial class HistoryView : UserControl
{
    public const string DragFormat = "ClipDesk.ClipboardHistoryEntry";
    private ObservableCollection<ClipboardHistoryEntry>? _entries;
    private ICollectionView? _view;
    private Point _dragStart;
    private ClipboardHistoryEntry? _dragEntry;
    private ListBoxItem? _dragContainer;
    public bool IsDragging { get; private set; }
    public event EventHandler? CloseRequested;
    public event EventHandler? OpenWorkspaceRequested;
    public event EventHandler<ClipboardHistoryEntry>? CopyRequested;
    public event EventHandler<ClipboardHistoryEntry>? AddRequested;

    public HistoryView() => InitializeComponent();

    public void Bind(ObservableCollection<ClipboardHistoryEntry> entries, bool compact = false)
    {
        if (_entries is not null) _entries.CollectionChanged -= EntriesChanged;
        _entries = entries;
        // Each surface has its own search without filtering the other surface.
        _view = new ListCollectionView(entries) { Filter = MatchesSearch };
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ClipboardHistoryEntry.CapturedDayLabel)));
        HistoryList.ItemsSource = _view;
        _entries.CollectionChanged += EntriesChanged;
        OpenWorkspaceButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        RefreshSummary();
    }

    public void ShowStatus(string message) => StatusText.Text = message;
    public void FocusSearch() => SearchBox.Focus();
    public void Detach() { if (_entries is not null) _entries.CollectionChanged -= EntriesChanged; }

    private bool MatchesSearch(object value)
    {
        if (value is not ClipboardHistoryEntry entry) return false;
        var query = SearchBox.Text.Trim();
        return query.Length == 0 || new[] { entry.Title, entry.Text, entry.Url, entry.Preview, entry.TypeLabel, entry.SourceSummary }
            .Any(text => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        RefreshSummary();
    }

    private void EntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSummary();
    private void RefreshSummary()
    {
        if (CountText is null || HistoryList is null || EmptyState is null) return;
        var count = _view?.Cast<object>().Count() ?? 0;
        CountText.Text = count == 1 ? "1 item salvo no histórico" : $"{count} itens salvos no histórico";
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searching = !string.IsNullOrWhiteSpace(SearchBox.Text);
        EmptyTitle.Text = searching ? "Nenhum resultado" : "Seu próximo item começa com Ctrl+C";
        EmptyHint.Text = searching ? "Tente outra palavra ou tipo de conteúdo." : "Copie um texto, link, imagem ou arquivo. Ele ficará salvo aqui entre sessões.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OpenWorkspace_Click(object sender, RoutedEventArgs e) => OpenWorkspaceRequested?.Invoke(this, EventArgs.Empty);
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardHistoryEntry entry) CopyRequested?.Invoke(this, entry);
        e.Handled = true;
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ClipboardHistoryEntry entry) AddRequested?.Invoke(this, entry);
        e.Handled = true;
    }
    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T value) return value;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }
    private void List_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(HistoryList);
        _dragEntry = Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) is null
            ? Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as ClipboardHistoryEntry
            : null;
    }
    private void List_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _dragEntry = null; return; }
        if (_dragEntry is null || IsDragging) return;
        var delta = e.GetPosition(HistoryList) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var data = new DataObject(DragFormat, _dragEntry);
        _dragContainer = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        _dragEntry = null;
        IsDragging = true;
        AnimateDragSource(true);
        try { DragDrop.DoDragDrop(HistoryList, data, DragDropEffects.Copy); }
        finally
        {
            AnimateDragSource(false);
            _dragContainer = null;
            IsDragging = false;
        }
        e.Handled = true;
    }

    private void AnimateDragSource(bool dragging)
    {
        if (_dragContainer is null) return;
        _dragContainer.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = _dragContainer.RenderTransform as ScaleTransform;
        if (scale is null)
        {
            scale = new ScaleTransform(1, 1);
            _dragContainer.RenderTransform = scale;
        }
        var duration = TimeSpan.FromMilliseconds(dragging ? 120 : 180);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(dragging ? 0.97 : 1, duration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(dragging ? 0.97 : 1, duration));
        _dragContainer.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(dragging ? 0.48 : 1, duration));
    }
    private void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        if (Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is ClipboardHistoryEntry entry)
        {
            CopyRequested?.Invoke(this, entry);
            e.Handled = true;
        }
    }
    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && HistoryList.SelectedItem is ClipboardHistoryEntry entry)
        {
            CopyRequested?.Invoke(this, entry);
            e.Handled = true;
        }
    }
}

public sealed class HistoryThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 96;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
