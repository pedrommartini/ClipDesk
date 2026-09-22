using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shapes;
using ClipDesk.Core;
using ClipDesk.PluginSdk;
using ClipDesk.Views;

namespace ClipDesk;

public partial class MainWindow
{
    private IReadOnlyList<BoardObjectView.PluginChoice> _availableCurrencyChoices = [];
    private readonly DispatcherTimer _boardObjectRealtimeTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly Dictionary<string, BoardObject> _pendingRealtimeBoardObjects = [];
    private bool _boardObjectRealtimeBusy;
    private readonly Dictionary<string, CancellationTokenSource> _utilityRequests = [];
    private DateTimeOffset _lastRealtimeFailureToast;

    private CancellationTokenSource BeginUtilityRequest(string id)
    {
        if (_utilityRequests.TryGetValue(id, out var previous)) previous.Cancel();
        var current = new CancellationTokenSource();
        _utilityRequests[id] = current;
        return current;
    }

    private void QueueBoardObjectRealtime(BoardObject obj, bool flush = false)
    {
        _pendingRealtimeBoardObjects[obj.Id] = obj;
        if(flush) { _ = FlushBoardObjectRealtimeAsync(); return; }
        if(!_boardObjectRealtimeBusy && !_boardObjectRealtimeTimer.IsEnabled)
        {
            _boardObjectRealtimeTimer.Interval=TimeSpan.FromMilliseconds(Mouse.Captured is BoardObjectView ? 200 : 70);
            _boardObjectRealtimeTimer.Start();
        }
    }

    private async Task FlushBoardObjectRealtimeAsync()
    {
        _boardObjectRealtimeTimer.Stop();
        if(_pendingRealtimeBoardObjects.Count==0) return;
        if(_boardObjectRealtimeBusy) { _boardObjectRealtimeTimer.Start(); return; }
        var objects=_pendingRealtimeBoardObjects.Values.ToList();
        _pendingRealtimeBoardObjects.Clear();
        _boardObjectRealtimeBusy=true;
        var failed=false;
        try
        {
            Save(queueCloud:false);
            if(_cloud is not null) await _cloud.SynchronizeBoardObjectsAsync(_activeWorkspace,objects);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            failed=true;
            // The local snapshot and queued cloud operations are retried by the
            // regular sync loop. Never let a dispatcher timer fault close the app.
            foreach(var obj in objects)
                if(!_pendingRealtimeBoardObjects.ContainsKey(obj.Id)) _pendingRealtimeBoardObjects[obj.Id]=obj;
            if(DateTimeOffset.UtcNow-_lastRealtimeFailureToast>TimeSpan.FromSeconds(30))
            {
                _lastRealtimeFailureToast=DateTimeOffset.UtcNow;
                ShowToast("Alteração pendente: " + ex.Message);
            }
            return;
        }
        finally
        {
            _boardObjectRealtimeBusy=false;
            if(_pendingRealtimeBoardObjects.Count>0)
            {
                _boardObjectRealtimeTimer.Interval=failed?TimeSpan.FromSeconds(2):TimeSpan.FromMilliseconds(70);
                _boardObjectRealtimeTimer.Start();
            }
            else _boardObjectRealtimeTimer.Interval=TimeSpan.FromMilliseconds(70);
        }
    }

    private void AddTextFromCanvasMenu_Click(object sender, RoutedEventArgs e)
    {
        HideCanvasContextMenu();
        CreateTextObject(ClampObjectPosition(_canvasContextPoint, 300, 90), false);
    }

    private void AddChecklistFromCanvasMenu_Click(object sender, RoutedEventArgs e) =>
        CreateBoardWidgetFromCanvasMenu(BoardObjectKind.Checklist, 320, 260,
            new() { ["accent"] = "#78D7FF" },
            new() { ["title"] = "Checklist", ["items"] = "Primeiro item\nSegundo item\nTerceiro item", ["checked"] = "" });

    private void AddCalculatorFromCanvasMenu_Click(object sender, RoutedEventArgs e) =>
        CreateBoardWidgetFromCanvasMenu(BoardObjectKind.Calculator, 300, 390,
            new() { ["accent"] = "#B998FF" }, new() { ["expression"] = "", ["display"] = "0" });

    private void AddTranslatorFromCanvasMenu_Click(object sender, RoutedEventArgs e) =>
        CreateBoardWidgetFromCanvasMenu(BoardObjectKind.Translator, 370, 270,
            new() { ["accent"] = "#72D5FF" },
            new() { ["sourceLanguage"] = "auto", ["targetLanguage"] = "en", ["input"] = "", ["output"] = "A tradução aparece aqui" });

    private void AddCurrencyConverterFromCanvasMenu_Click(object sender, RoutedEventArgs e) =>
        CreateBoardWidgetFromCanvasMenu(BoardObjectKind.CurrencyConverter, 350, 245,
            new() { ["accent"] = "#62DDB0" },
            new() { ["sourceCurrency"] = "BRL", ["targetCurrency"] = "USD", ["amount"] = "1", ["result"] = "Escolha as moedas e converta" });

    private void MorePluginsFromCanvasMenu_Click(object sender, RoutedEventArgs e)
    {
        var point = _canvasContextPoint;
        HideCanvasContextMenu();
        ShowPluginStore(point);
    }

    private void CreateBoardWidgetFromCanvasMenu(BoardObjectKind kind, double width, double height,
        Dictionary<string, string> style, Dictionary<string, string> content)
    {
        HideCanvasContextMenu();
        RegisterUndoSnapshot();
        var point = ClampObjectPosition(_canvasContextPoint, width, height);
        var obj = NewObject(kind, point.X, point.Y, width, height, style, content);
        if (obj.PluginId is { } pluginId)
            obj.PluginVersion = _pluginCatalogService.GetInstalledPackage(pluginId)?.Manifest.Version ?? BoardPluginIdentity.InitialVersion;
        _activeWorkspace.Objects.Add(obj);
        var view = AddBoardObjectView(obj); view.PlayPopIn();
        SelectBoardObject(view);
        Save();
        if(kind == BoardObjectKind.CurrencyConverter) view.StartAutomaticUtility();
    }

    private Point ClampObjectPosition(Point point, double width, double height) => new(
        Math.Clamp(point.X, 0, Math.Max(0, _activeWorkspace.WorldWidth - width)),
        Math.Clamp(point.Y, 0, Math.Max(0, _activeWorkspace.WorldHeight - height)));

    private async void BoardObjectWidgetActionRequested(BoardObjectView view, string action)
    {
        if (action == "persist")
        {
            view.Object.UpdatedAt = DateTimeOffset.UtcNow;
            QueueBoardObjectRealtime(view.Object);
            return;
        }
        if (action == "settle") { QueueCloudSync(); return; }

        if (action == "plugin:before-change") { RegisterUndoSnapshot(); return; }

        if (action.StartsWith("calculator:", StringComparison.Ordinal))
        {
            RegisterUndoSnapshot();
            var key = action[11..];
            var state = BoardCalculator.Press(view.Object.Content.GetValueOrDefault("expression", ""), key);
            view.Object.Content["expression"] = state.Expression;
            view.Object.Content["display"] = state.Display;
            view.Object.UpdatedAt = DateTimeOffset.UtcNow;
            view.RefreshFromObject();
            QueueBoardObjectRealtime(view.Object,flush:true);
            return;
        }

        if (action.StartsWith("checklist:", StringComparison.Ordinal) && int.TryParse(action[10..], out var index))
        {
            RegisterUndoSnapshot();
            var checkedItems = view.Object.Content.GetValueOrDefault("checked", "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var parsed) ? parsed : -1)
                .Where(value => value >= 0).ToHashSet();
            if (!checkedItems.Add(index)) checkedItems.Remove(index);
            view.Object.Content["checked"] = string.Join(';', checkedItems.Order());
            view.Object.UpdatedAt = DateTimeOffset.UtcNow;
            view.RefreshFromObject();
            QueueBoardObjectRealtime(view.Object,flush:true);
            return;
        }

        if (action == "translate")
        {
            var input = view.Object.Content.GetValueOrDefault("input", "").Trim();
            var source = view.Object.Content.GetValueOrDefault("sourceLanguage", "auto");
            var target = view.Object.Content.GetValueOrDefault("targetLanguage", "en");
            var request = BeginUtilityRequest(view.Object.Id);
            if (string.IsNullOrWhiteSpace(input))
            {
                view.Object.Content["output"] = "A tradução aparece aqui";
                view.Object.Content.Remove("status");
                view.SetUtilityLoading(false);
                view.Object.UpdatedAt = DateTimeOffset.UtcNow;
                QueueBoardObjectRealtime(view.Object,flush:true);
                _utilityRequests.Remove(view.Object.Id);
                request.Dispose();
                return;
            }
            view.SetUtilityLoading(true);
            try
            {
                var translated = await _boardUtilityService.TranslateAsync(input, source, target, request.Token);
                if (view.Object.Content.GetValueOrDefault("input", "").Trim() == input
                    && view.Object.Content.GetValueOrDefault("sourceLanguage", "auto") == source
                    && view.Object.Content.GetValueOrDefault("targetLanguage", "en") == target)
                    view.Object.Content["output"] = translated;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!request.IsCancellationRequested) { view.Object.Content["output"] = "Não foi possível traduzir agora."; ShowToast($"Tradutor: {ex.Message}"); } }
            finally
            {
                if (_utilityRequests.TryGetValue(view.Object.Id, out var latest) && ReferenceEquals(latest, request))
                {
                    _utilityRequests.Remove(view.Object.Id);
                    view.Object.Content.Remove("status");
                    view.Object.UpdatedAt = DateTimeOffset.UtcNow;
                    view.SetUtilityLoading(false);
                    QueueBoardObjectRealtime(view.Object,flush:true);
                }
                request.Dispose();
            }
            return;
        }

        if (action == "convert")
        {
            var amountText = view.Object.Content.GetValueOrDefault("amount", "1");
            var source = view.Object.Content.GetValueOrDefault("sourceCurrency", "BRL");
            var target = view.Object.Content.GetValueOrDefault("targetCurrency", "USD");
            var request = BeginUtilityRequest(view.Object.Id);
            if (!TryReadDecimal(amountText, out var amount))
            {
                view.Object.Content["result"] = "Digite um valor válido";
                view.Object.Content.Remove("status");
                view.SetUtilityLoading(false);
                view.Object.UpdatedAt = DateTimeOffset.UtcNow;
                QueueBoardObjectRealtime(view.Object,flush:true);
                _utilityRequests.Remove(view.Object.Id);
                request.Dispose();
                return;
            }
            view.SetUtilityLoading(true);
            try
            {
                var conversion = await _boardUtilityService.ConvertCurrencyAsync(amount, source, target, request.Token);
                if (view.Object.Content.GetValueOrDefault("amount", "1") == amountText
                    && view.Object.Content.GetValueOrDefault("sourceCurrency", "BRL") == source
                    && view.Object.Content.GetValueOrDefault("targetCurrency", "USD") == target)
                {
                    view.Object.Content["result"] = $"{target} {conversion.Converted:0.00}";
                    view.Object.Content["rate"] = conversion.Rate.ToString(CultureInfo.InvariantCulture);
                    view.Object.Content["rateDate"] = conversion.Date;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!request.IsCancellationRequested) { view.Object.Content["result"] = "Cotação indisponível"; ShowToast($"Conversor: {ex.Message}"); } }
            finally
            {
                if (_utilityRequests.TryGetValue(view.Object.Id, out var latest) && ReferenceEquals(latest, request))
                {
                    _utilityRequests.Remove(view.Object.Id);
                    view.Object.Content.Remove("status");
                    view.Object.UpdatedAt = DateTimeOffset.UtcNow;
                    view.SetUtilityLoading(false);
                    QueueBoardObjectRealtime(view.Object,flush:true);
                }
                request.Dispose();
            }
        }
    }

    private async Task LoadCurrencyChoicesAsync()
    {
        try
        {
            var currencies = await _boardUtilityService.GetCurrenciesAsync();
            _availableCurrencyChoices = currencies.Select(entry => new BoardObjectView.PluginChoice(entry.Key, entry.Value)).ToArray();
            foreach (var view in WorkspaceCanvas.Children.OfType<BoardObjectView>()) view.SetCurrencyChoices(_availableCurrencyChoices);
        }
        catch
        {
            // Each plugin keeps its complete embedded fallback catalog when the service is offline.
        }
    }

    private void ShowBoardObjectContextMenu(BoardObjectView view)
    {
        HideCreativeFormatMenu();
        _contextBoardObjectView = view;
        BoardObjectAccentButton.Visibility = IsPluginObject(view.Object) ? Visibility.Visible : Visibility.Collapsed;
        BoardObjectContextMenu.Visibility = Visibility.Visible;
        BoardObjectContextMenu.UpdateLayout();
        var objectRight = (view.Object.X + view.Object.Width) * _workspaceZoom - WorkspaceScroll.HorizontalOffset;
        var objectTop = view.Object.Y * _workspaceZoom - WorkspaceScroll.VerticalOffset;
        var left = Math.Clamp(objectRight - BoardObjectContextMenu.ActualWidth, 12, Math.Max(12, Root.ActualWidth - BoardObjectContextMenu.ActualWidth - 12));
        var top = Math.Clamp(objectTop - BoardObjectContextMenu.ActualHeight - 8, 78, Math.Max(78, Root.ActualHeight - BoardObjectContextMenu.ActualHeight - 12));
        BoardObjectContextMenu.Margin = new Thickness(left, top, 0, 0);
        BoardObjectContextMenu.Opacity = 0;
        BoardObjectContextMenuScale.ScaleX = .88;
        BoardObjectContextMenuScale.ScaleY = .88;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BoardObjectContextMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        BoardObjectContextMenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        BoardObjectContextMenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
    }

    private void HideBoardObjectContextMenu()
    {
        _contextBoardObjectView = null;
        if (BoardObjectContextMenu is not null) BoardObjectContextMenu.Visibility = Visibility.Collapsed;
        HideCreativeFormatMenu();
    }

    private static bool IsPluginObject(BoardObject obj) => obj.Kind is BoardObjectKind.Checklist
        or BoardObjectKind.Calculator or BoardObjectKind.Translator or BoardObjectKind.CurrencyConverter
        or BoardObjectKind.Plugin || !string.IsNullOrWhiteSpace(obj.PluginId);

    private static readonly string[] PluginAccentPalette =
    [
        "#A78BFA", "#38BDF8", "#22D3EE", "#34D399",
        "#FBBF24", "#FB923C", "#FB7185", "#F472B6"
    ];

    private void BoardObjectAccent_Click(object sender, RoutedEventArgs e)
    {
        if (_contextBoardObjectView is not { } view || !IsPluginObject(view.Object)) return;
        ShowPluginAccentMenu(view);
        e.Handled = true;
    }

    private void ShowPluginAccentMenu(BoardObjectView view)
    {
        var obj = view.Object;
        _formatObject = obj;
        CreativeFormatMenuContent.Children.Clear();
        CreativeFormatMenuContent.Children.Add(FormatRow("Destaque",
            PluginAccentPalette.Select(color => PluginAccentColorButton(color, obj, view))));
        CreativeFormatMenuContent.Children.Add(FormatRow("Opções",
        [
            FormatButton("Personalizada…", () => ChooseCustomPluginAccent(obj, view), toolTip: "Escolher qualquer cor"),
            FormatButton("Padrão", () => ResetPluginAccent(obj, view), toolTip: "Usar a cor definida pelo plugin")
        ]));
        CreativeFormatMenu.Visibility = Visibility.Visible;
        CreativeFormatMenu.UpdateLayout();
        PositionCreativeFormatMenu(obj);
    }

    private Button PluginAccentColorButton(string color, BoardObject obj, BoardObjectView view)
    {
        var selected = obj.Style.GetValueOrDefault(PluginStyleKeys.AccentColor, PluginDefaultAccent(obj))
            .Equals(color, StringComparison.OrdinalIgnoreCase);
        var button = new Button
        {
            Width = 32, Height = 32, Padding = new Thickness(5), Margin = new Thickness(2, 0, 0, 0),
            ToolTip = color, BorderThickness = new Thickness(selected ? 2 : 0),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#F8FAFC" : "#263449"))
        };
        button.Content = new Ellipse
        {
            Width = 17, Height = 17, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), StrokeThickness = 1
        };
        button.Click += (_, e) => { ApplyPluginAccent(obj, view, color); e.Handled = true; };
        return button;
    }

    private void ChooseCustomPluginAccent(BoardObject obj, BoardObjectView view)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        try
        {
            var current = (Color)ColorConverter.ConvertFromString(obj.Style.GetValueOrDefault(
                PluginStyleKeys.AccentColor, PluginDefaultAccent(obj)));
            dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException) { }
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        ApplyPluginAccent(obj, view, $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
    }

    private void ResetPluginAccent(BoardObject obj, BoardObjectView view)
    {
        if (!obj.Style.ContainsKey(PluginStyleKeys.AccentColor)) return;
        RegisterUndoSnapshot();
        obj.Style.Remove(PluginStyleKeys.AccentColor);
        FinishPluginAccentChange(obj, view);
    }

    private void ApplyPluginAccent(BoardObject obj, BoardObjectView view, string color)
    {
        if (obj.Style.GetValueOrDefault(PluginStyleKeys.AccentColor)
            ?.Equals(color, StringComparison.OrdinalIgnoreCase) == true) return;
        RegisterUndoSnapshot();
        obj.Style[PluginStyleKeys.AccentColor] = color;
        FinishPluginAccentChange(obj, view);
    }

    private void FinishPluginAccentChange(BoardObject obj, BoardObjectView view)
    {
        obj.UpdatedAt = DateTimeOffset.UtcNow;
        view.RefreshFromObject();
        Save();
        QueueBoardObjectRealtime(obj, flush: true);
        ShowPluginAccentMenu(view);
    }

    private string PluginDefaultAccent(BoardObject obj)
    {
        var pluginId = obj.PluginId ?? BoardPluginIdentity.FromKind(obj.Kind);
        return _pluginCatalogEntries.FirstOrDefault(entry => entry.Manifest.Id == pluginId)?.Manifest.AccentColor
            ?? obj.Kind switch
            {
                BoardObjectKind.Checklist => "#38BDF8",
                BoardObjectKind.Calculator => "#A78BFA",
                BoardObjectKind.Translator => "#38BDF8",
                BoardObjectKind.CurrencyConverter => "#34D399",
                _ => "#9B7DFF"
            };
    }

    private void BoardObjectCopy_Click(object sender, RoutedEventArgs e)
    {
        var view = _contextBoardObjectView;
        if (view is null) return;
        try
        {
            Clipboard.SetText(BoardObjectClipboardText(view.Object));
            ShowToast("Conteúdo copiado");
        }
        catch (Exception ex) { ShowToast($"Não foi possível copiar: {ex.Message}"); }
        HideBoardObjectContextMenu();
        e.Handled = true;
    }

    private void BoardObjectDuplicate_Click(object sender, RoutedEventArgs e)
    {
        var view = _contextBoardObjectView;
        if (view is null) return;
        RegisterUndoSnapshot();
        var source = view.Object;
        var copy = NewObject(source.Kind,
            Math.Clamp(source.X + 28, 0, Math.Max(0, _activeWorkspace.WorldWidth - source.Width)),
            Math.Clamp(source.Y + 28, 0, Math.Max(0, _activeWorkspace.WorldHeight - source.Height)),
            source.Width, source.Height, new(source.Style), new(source.Content));
        copy.Rotation = source.Rotation;
        copy.PluginId = source.PluginId;
        copy.PluginVersion = source.PluginVersion;
        _activeWorkspace.Objects.Add(copy);
        var copyView = AddBoardObjectView(copy);
        HideBoardObjectContextMenu();
        SelectBoardObject(copyView);
        Save();
        ShowToast("Objeto duplicado");
        e.Handled = true;
    }

    private void BoardObjectBringForward_Click(object sender, RoutedEventArgs e)
    {
        if (_contextBoardObjectView is not null) ChangeWorkspaceElementLayer(_contextBoardObjectView, 1);
        e.Handled = true;
    }

    private void BoardObjectSendBackward_Click(object sender, RoutedEventArgs e)
    {
        if (_contextBoardObjectView is not null) ChangeWorkspaceElementLayer(_contextBoardObjectView, -1);
        e.Handled = true;
    }

    private void Card_BringForwardRequested(object? sender, EventArgs e)
    {
        if (sender is ItemCard card) ChangeWorkspaceElementLayer(card, 1);
    }

    private void Card_SendBackwardRequested(object? sender, EventArgs e)
    {
        if (sender is ItemCard card) ChangeWorkspaceElementLayer(card, -1);
    }

    private void ChangeWorkspaceElementLayer(UIElement source, int direction)
    {
        if (source is BoardObjectView { Object.Kind: BoardObjectKind.Connector }) return;

        var sourceBounds = WorkspaceElementBounds(source);
        var ordered = WorkspaceCanvas.Children.Cast<UIElement>()
            .Where(element => element is ItemCard || element is BoardObjectView { Object.Kind: not BoardObjectKind.Connector })
            .Where(element => ReferenceEquals(element, source) || AreLayerNeighbors(sourceBounds, WorkspaceElementBounds(element)))
            .OrderBy(Panel.GetZIndex)
            .ToList();
        var index = ordered.IndexOf(source);
        if (index < 0) return;

        var destination = Math.Clamp(index + direction, 0, ordered.Count - 1);
        if (index == destination)
        {
            ShowToast(ordered.Count == 1 ? "Não há elementos próximos para reordenar" : direction > 0 ? "O elemento já está à frente dos próximos" : "O elemento já está atrás dos próximos");
            return;
        }

        RegisterUndoSnapshot();
        var neighbor = ordered[destination];
        var sourceZIndex = Panel.GetZIndex(source);
        SetWorkspaceElementZIndex(source, Panel.GetZIndex(neighbor));
        SetWorkspaceElementZIndex(neighbor, sourceZIndex);
        Save();
        if (source is BoardObjectView sourceView) QueueBoardObjectRealtime(sourceView.Object, flush: true);
        if (neighbor is BoardObjectView neighborView) QueueBoardObjectRealtime(neighborView.Object, flush: true);
        ShowToast(direction > 0 ? "Elemento subiu uma camada" : "Elemento desceu uma camada");
    }

    private static bool AreLayerNeighbors(Rect source, Rect candidate)
    {
        var horizontalGap = Math.Max(0, Math.Max(source.Left - candidate.Right, candidate.Left - source.Right));
        var verticalGap = Math.Max(0, Math.Max(source.Top - candidate.Bottom, candidate.Top - source.Bottom));
        var distance = Math.Sqrt(horizontalGap * horizontalGap + verticalGap * verticalGap);
        var maximumDistance = Math.Clamp((Math.Max(source.Width, source.Height) + Math.Max(candidate.Width, candidate.Height)) / 2, 160, 360);
        return distance <= maximumDistance;
    }

    private Rect WorkspaceElementBounds(UIElement element) => element switch
    {
        ItemCard card => card.GetVisualBounds(WorkspaceCanvas),
        BoardObjectView view => new Rect(view.Object.X, view.Object.Y, view.Object.Width, view.Object.Height),
        _ => Rect.Empty
    };

    private static void SetWorkspaceElementZIndex(UIElement element, int zIndex)
    {
        Panel.SetZIndex(element, zIndex);
        if (element is ItemCard card) card.Item.ZIndex = zIndex;
        else if (element is BoardObjectView view)
        {
            view.Object.ZIndex = zIndex;
            view.Object.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private void BoardObjectInfo_Click(object sender, RoutedEventArgs e)
    {
        var view = _contextBoardObjectView;
        if (view is null) return;
        ShowToast($"{BoardObjectLabel(view.Object.Kind)} · {Math.Round(view.Object.Width)} × {Math.Round(view.Object.Height)}");
        HideBoardObjectContextMenu();
        e.Handled = true;
    }

    private void BoardObjectDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_contextBoardObjectView is not null) _selectedBoardObjectView = _contextBoardObjectView;
        HideBoardObjectContextMenu();
        DeleteSelectedBoardObject();
        e.Handled = true;
    }

    private static string BoardObjectClipboardText(BoardObject obj) => obj.Kind switch
    {
        BoardObjectKind.Text or BoardObjectKind.StickyNote => obj.Content.GetValueOrDefault("text", ""),
        BoardObjectKind.Checklist => string.Join(Environment.NewLine, obj.Content.GetValueOrDefault("items", "").Split('\n').Select((item, index) => $"{(obj.Content.GetValueOrDefault("checked", "").Split(';').Contains(index.ToString(CultureInfo.InvariantCulture)) ? "☑" : "☐")} {item}")),
        BoardObjectKind.Calculator => obj.Content.GetValueOrDefault("display", "0"),
        BoardObjectKind.Translator => obj.Content.GetValueOrDefault("output", obj.Content.GetValueOrDefault("input", "")),
        BoardObjectKind.CurrencyConverter => obj.Content.GetValueOrDefault("result", obj.Content.GetValueOrDefault("amount", "")),
        _ => BoardObjectLabel(obj.Kind)
    };

    private static string BoardObjectLabel(BoardObjectKind kind) => kind switch
    {
        BoardObjectKind.StickyNote => "Nota", BoardObjectKind.Text => "Texto", BoardObjectKind.Shape => "Forma",
        BoardObjectKind.Stroke => "Traço", BoardObjectKind.Checklist => "Checklist", BoardObjectKind.Calculator => "Calculadora",
        BoardObjectKind.Translator => "Tradutor", BoardObjectKind.CurrencyConverter => "Conversor de moeda", _ => "Objeto"
    };

    private static bool TryReadDecimal(string text, out decimal value) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) || decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
