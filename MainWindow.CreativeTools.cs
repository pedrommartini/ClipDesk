using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipDesk.Core;
using ClipDesk.Services;
using ClipDesk.Views;

namespace ClipDesk;

public partial class MainWindow
{
    private enum CreativeTool { Select, Pan, Pen, Text, Note, Shape, Connector }

    private CreativeTool _activeCreativeTool = CreativeTool.Select;
    private BoardObjectView? _selectedBoardObjectView;
    private string? _editingBoardObjectId;
    private Point _creativeStart;
    private bool _creativePointerDown;
    private bool _creativeToolPanning;
    private Point _creativePanOrigin;
    private Point _creativeScrollOrigin;
    private Polyline? _draftStroke;
    private Rectangle? _draftShape;
    private BoardObject? _formatObject;
    private BoardObjectView? _contextBoardObjectView;
    private readonly BoardUtilityService _boardUtilityService = new();
    private readonly List<string> _connectorCardIds = [];
    private BoardObject? _activeConnectorObject;
    private readonly Dictionary<BoardObject, Point> _linkedObjectDragOrigins = [];
    private readonly Dictionary<ItemCard, Point> _linkedCardDragOrigins = [];
    private string? _linkedDragLeadId;
    private Point _linkedDragLeadOrigin;
    private bool _moveConnectedTogether;
    private bool _magnetAlignmentEnabled = true;
    private readonly List<Rect> _alignmentTargets = [];
    private double _penThickness = 3.2;
    private string _penColor = "#A78BFA";
    private bool _penHighlighter;
    private string _shapeKind = "square";
    private string _shapeStrokeColor = "#A78BFA";
    private string _shapeFillColor = "#241096F3";
    private double _shapeThickness = 2;
    private readonly DispatcherTimer _creativeMoveSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private void InitializeCreativeTools()
    {
        PreviewMouseDown += CreativeSelectionDismiss_PreviewMouseDown;
        _boardObjectRealtimeTimer.Tick += async (_, _) => await FlushBoardObjectRealtimeAsync();
        _creativeMoveSaveTimer.Tick += (_, _) =>
        {
            _creativeMoveSaveTimer.Stop();
            Save();
        };
        SetCreativeTool(CreativeTool.Select);
        UpdateMagnetButtonPresentation();
        _ = LoadCurrencyChoicesAsync();
    }

    private void CreativeSelectionDismiss_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (BoardObjectContextMenu.Visibility == Visibility.Visible && !BoardObjectContextMenu.IsMouseOver
            && (CreativeFormatMenu.Visibility != Visibility.Visible || !CreativeFormatMenu.IsMouseOver))
            HideBoardObjectContextMenu();
        if(CreativeFormatMenu.Visibility!=Visibility.Visible||CreativeFormatMenu.IsMouseOver)return;
        var source=e.OriginalSource as DependencyObject;var objectView=FindAncestor<BoardObjectView>(source);
        if(objectView is not null && _selectedBoardObjectViews.Contains(objectView))
        {
            if(objectView?.IsPluginInteractionSource(source)==true) ClearBoardObjectSelection();
            return;
        }
        var editor=FindAncestor<TextBox>(source);if(editor is not null&&WorkspaceCanvas.Children.Contains(editor))return;
        ClearBoardObjectSelection();
    }

    private void CreativeToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<CreativeTool>(tag, out var tool)) SetCreativeTool(tool);
        e.Handled = true;
    }

    private void ConnectorToolButton_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        _moveConnectedTogether = !_moveConnectedTogether;
        SetCreativeTool(CreativeTool.Select);
        ConnectorToolButton.ToolTip = _moveConnectedTogether
            ? "Mover conexões juntas: ligado · duplo clique para desligar"
            : "Conectar cards (C) · duplo clique: mover conexões juntas";
        ConnectorMoveIndicator.Visibility = _moveConnectedTogether ? Visibility.Visible : Visibility.Collapsed;
        ShowToast(_moveConnectedTogether ? "Mover cards conectados: ligado" : "Mover cards conectados: desligado");
        e.Handled = true;
    }

    private void MagnetToolButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMagnetAlignment();
        e.Handled = true;
    }

    private void ToggleMagnetAlignment()
    {
        _magnetAlignmentEnabled = !_magnetAlignmentEnabled;
        UpdateMagnetButtonPresentation();
        if (!_magnetAlignmentEnabled) FinishAlignmentDrag();
        _appearanceSaveTimer.Stop();
        _appearanceSaveTimer.Start();
        ShowToast(_magnetAlignmentEnabled ? "Alinhamento magnético ligado" : "Alinhamento magnético desligado");
    }

    private void UpdateMagnetButtonPresentation()
    {
        if (MagnetToolButton is null) return;
        MagnetToolButton.Background = _magnetAlignmentEnabled
            ? (Brush)new BrushConverter().ConvertFromString("#4C5B36B8")!
            : Brushes.Transparent;
        MagnetToolButton.ToolTip = _magnetAlignmentEnabled
            ? "Alinhamento magnético ligado (M) · segure Alt para suspender"
            : "Alinhamento magnético desligado (M)";
        MagnetToolIndicator.Visibility = _magnetAlignmentEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetCreativeTool(CreativeTool tool, bool keepFormatMenu = false)
    {
        CancelCreativeDraft();
        if (!keepFormatMenu) HideCreativeFormatMenu();
        if (tool != CreativeTool.Connector) ResetConnectorSession();
        _activeCreativeTool = tool;
        var active = (Brush)new BrushConverter().ConvertFromString("#5B4BC4")!;
        foreach (var button in CreativeToolButtons()) button.Background = Equals(button.Tag, tool.ToString()) ? active : Brushes.Transparent;
        SelectionToolButton.Background = tool == CreativeTool.Select ? active : Brushes.Transparent;
        Cursor = tool switch
        {
            CreativeTool.Pan => Cursors.Hand,
            CreativeTool.Pen or CreativeTool.Shape or CreativeTool.Connector => Cursors.Cross,
            CreativeTool.Text or CreativeTool.Note => Cursors.IBeam,
            _ => Cursors.Arrow
        };
        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>()) card.IsHitTestVisible = tool is CreativeTool.Select or CreativeTool.Connector;
        foreach (var view in WorkspaceCanvas.Children.OfType<BoardObjectView>()) view.IsInteractive = tool == CreativeTool.Select;
        if (_moveConnectedTogether && tool != CreativeTool.Connector)
            ConnectorToolButton.Background = (Brush)new BrushConverter().ConvertFromString("#3442B8A6")!;
    }

    private IEnumerable<Button> CreativeToolButtons()
    {
        yield return SelectionToolButton;
        yield return PanToolButton;
        yield return PenToolButton;
        yield return TextToolButton;
        yield return NoteToolButton;
        yield return ShapeToolButton;
        yield return ConnectorToolButton;
    }

    private bool HandleCreativeMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _activeCreativeTool == CreativeTool.Select) return false;
        HideCanvasContextMenu();
        ClearSelection();
        _creativeStart = ClampCreativePoint(e.GetPosition(WorkspaceCanvas));

        if (_activeCreativeTool == CreativeTool.Connector)
        {
            var card = FindAncestor<ItemCard>(e.OriginalSource as DependencyObject);
            var objectView = FindAncestor<BoardObjectView>(e.OriginalSource as DependencyObject);
            if (card is not null) ConnectNode(CardNodeId(card.Item.Id));
            else if (objectView is not null && objectView.Object.Kind != BoardObjectKind.Connector) ConnectNode(ObjectNodeId(objectView.Object.Id));
            else if (_connectorCardIds.Count > 0) { ResetConnectorSession(); ShowToast("Conexão concluída"); }
            return true;
        }

        if (_activeCreativeTool == CreativeTool.Text)
        {
            CreateTextObject(_creativeStart, false);
            return true;
        }
        if (_activeCreativeTool == CreativeTool.Note)
        {
            CreateTextObject(_creativeStart, true);
            return true;
        }
        if (_activeCreativeTool == CreativeTool.Pan)
        {
            _creativeToolPanning = true;
            _creativePanOrigin = e.GetPosition(Root);
            _creativeScrollOrigin = new Point(WorkspaceScroll.HorizontalOffset, WorkspaceScroll.VerticalOffset);
            Root.CaptureMouse();
            Cursor = Cursors.SizeAll;
            return true;
        }

        RegisterUndoSnapshot();
        _creativePointerDown = true;
        Root.CaptureMouse();
        if (_activeCreativeTool == CreativeTool.Pen)
        {
            _draftStroke = new Polyline
            {
                Stroke = StrokeBrush(_penColor, _penHighlighter), StrokeThickness = _penThickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false, Points = new PointCollection { _creativeStart }
            };
            Panel.SetZIndex(_draftStroke, 120);
            WorkspaceCanvas.Children.Add(_draftStroke);
        }
        else if (_activeCreativeTool == CreativeTool.Shape)
        {
            _draftShape = new Rectangle { Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A78BFA")), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(28, 139, 92, 246)), RadiusX = 9, RadiusY = 9, IsHitTestVisible = false };
            Canvas.SetLeft(_draftShape, _creativeStart.X); Canvas.SetTop(_draftShape, _creativeStart.Y);
            Panel.SetZIndex(_draftShape, 120); WorkspaceCanvas.Children.Add(_draftShape);
        }
        return true;
    }

    private bool HandleCreativeMouseMove(MouseEventArgs e)
    {
        if (_creativeToolPanning)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { FinishCreativePointer(); return true; }
            var delta = e.GetPosition(Root) - _creativePanOrigin;
            WorkspaceScroll.ScrollToHorizontalOffset(Math.Clamp(_creativeScrollOrigin.X - delta.X, 0, WorkspaceScroll.ScrollableWidth));
            WorkspaceScroll.ScrollToVerticalOffset(Math.Clamp(_creativeScrollOrigin.Y - delta.Y, 0, WorkspaceScroll.ScrollableHeight));
            return true;
        }
        if (!_creativePointerDown || e.LeftButton != MouseButtonState.Pressed) return false;
        var point = ClampCreativePoint(e.GetPosition(WorkspaceCanvas));
        if (_draftStroke is not null)
        {
            if ((_draftStroke.Points[^1] - point).Length >= 2.2) _draftStroke.Points.Add(point);
        }
        else if (_draftShape is not null)
        {
            var rect = RectFromPoints(_creativeStart, point);
            Canvas.SetLeft(_draftShape, rect.X); Canvas.SetTop(_draftShape, rect.Y); _draftShape.Width = rect.Width; _draftShape.Height = rect.Height;
        }
        return true;
    }

    private bool HandleCreativeMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || (!_creativePointerDown && !_creativeToolPanning)) return false;
        if (_creativeToolPanning)
        {
            FinishCreativePointer();
            _appearanceSaveTimer.Stop(); _appearanceSaveTimer.Start();
            return true;
        }
        var end = ClampCreativePoint(e.GetPosition(WorkspaceCanvas));
        BoardObject? created = null;
        if (_draftStroke is not null && _draftStroke.Points.Count > 1) created = CreateStrokeObject(_draftStroke.Points);
        else if (_draftShape is not null)
        {
            var rect = RectFromPoints(_creativeStart, end);
            var isLine = _shapeKind == "line" || (Math.Max(rect.Width, rect.Height) >= 12 && Math.Min(rect.Width, rect.Height) <= 10);
            if (isLine)
            {
                // Um arrasto quase horizontal ou vertical é uma linha, sem exigir a troca manual de ferramenta.
                created = NewObject(BoardObjectKind.Shape, rect.X, rect.Y, Math.Max(8, rect.Width), Math.Max(8, rect.Height),
                    new() { ["stroke"] = _shapeStrokeColor, ["fill"] = "#00000000", ["thickness"] = _shapeThickness.ToString(CultureInfo.InvariantCulture), ["shape"] = "line",
                        ["lineStartX"] = _creativeStart.X >= end.X ? "1" : "0", ["lineStartY"] = _creativeStart.Y >= end.Y ? "1" : "0",
                        ["lineEndX"] = _creativeStart.X >= end.X ? "0" : "1", ["lineEndY"] = _creativeStart.Y >= end.Y ? "0" : "1" });
            }
            else if (rect.Width >= 12 && rect.Height >= 12)
            {
                created = NewObject(BoardObjectKind.Shape, rect.X, rect.Y, rect.Width, rect.Height,
                    new() { ["stroke"] = _shapeStrokeColor, ["fill"] = _shapeFillColor, ["thickness"] = _shapeThickness.ToString(CultureInfo.InvariantCulture), ["shape"] = _shapeKind });
            }
        }
        CancelCreativeDraft();
        if (created is not null)
        {
            _activeWorkspace.Objects.Add(created);
            var view = AddBoardObjectView(created); view.PlayPopIn();
            Save();
            if (_activeCreativeTool == CreativeTool.Pen)
            {
                // A caneta permanece ativa para permitir vários traços consecutivos.
                ClearBoardObjectSelection();
            }
            else
            {
                SelectBoardObject(view);
                ShowCreativeFormatMenu(created, view);
            }
        }
        if (created is not null && _activeCreativeTool != CreativeTool.Pen)
            SetCreativeTool(CreativeTool.Select, keepFormatMenu: true);
        return true;
    }

    private BoardObject CreateStrokeObject(IEnumerable<Point> source)
    {
        var points = source.ToList();
        var left = points.Min(p => p.X); var top = points.Min(p => p.Y);
        var right = points.Max(p => p.X); var bottom = points.Max(p => p.Y);
        var normalized = points.Select(p => new Point(p.X - left + 4, p.Y - top + 4)).ToList();
        return NewObject(BoardObjectKind.Stroke, Math.Max(0, left - 4), Math.Max(0, top - 4), Math.Max(8, right - left + 8), Math.Max(8, bottom - top + 8),
            new() { ["stroke"] = _penColor, ["thickness"] = _penThickness.ToString(CultureInfo.InvariantCulture), ["mode"] = _penHighlighter ? "highlight" : "draw" },
            new() { ["points"] = SerializePoints(normalized), ["naturalWidth"] = Math.Max(8, right-left+8).ToString(CultureInfo.InvariantCulture), ["naturalHeight"] = Math.Max(8, bottom-top+8).ToString(CultureInfo.InvariantCulture) });
    }

    private void CreateTextObject(Point position, bool note)
    {
        RegisterUndoSnapshot();
        var obj = NewObject(note ? BoardObjectKind.StickyNote : BoardObjectKind.Text, position.X, position.Y,
            note ? 220 : 300, note ? 180 : 90,
            note ? new() { ["fill"] = "#DCCBFF", ["color"] = "#2A2417", ["fontSize"] = "24", ["align"] = "center" }
                 : new() { ["color"] = _isDarkMode ? "#F8FAFC" : "#1F2937", ["fontSize"] = "24", ["align"] = "left" },
            new() { ["text"] = note ? "Nova nota" : "Digite seu texto" });
        _activeWorkspace.Objects.Add(obj);
        var view = AddBoardObjectView(obj); view.PlayPopIn();
        SelectBoardObject(view);
        Save();
        ShowCreativeFormatMenu(obj, view);
        SetCreativeTool(CreativeTool.Select, keepFormatMenu: true);
        Dispatcher.BeginInvoke(() => BeginBoardObjectEdit(view), DispatcherPriority.Input);
    }

    private BoardObject NewObject(BoardObjectKind kind, double x, double y, double width, double height,
        Dictionary<string, string>? style = null, Dictionary<string, string>? content = null) => new()
    {
        WorkspaceId = _activeWorkspace.Id.ToString("N"), Kind = kind, X = x, Y = y, Width = width, Height = height,
        PluginId = BoardPluginIdentity.FromKind(kind), PluginName = BoardPluginIdentity.NameFromKind(kind),
        PluginVersion = BoardPluginIdentity.FromKind(kind) is null ? null : BoardPluginIdentity.InitialVersion,
        CreatedBy = _cloud?.User?.Id, Style = style ?? [], Content = content ?? [], ZIndex = NextBoardZIndex(), UpdatedAt = DateTimeOffset.UtcNow
    };

    private BoardObjectView AddBoardObjectView(BoardObject obj)
    {
        if (obj.Kind == BoardObjectKind.Connector) UpdateConnectorGeometry(obj);
        var view = new BoardObjectView(obj) { IsDarkMode = _isDarkMode, IsInteractive = obj.Kind != BoardObjectKind.Connector && _activeCreativeTool == CreativeTool.Select };
        view.SetViewportZoom(_workspaceZoom);
        view.SetSelectionColor(LocalPresenceColor());
        if (_availableCurrencyChoices.Count > 0) view.SetCurrencyChoices(_availableCurrencyChoices);
        PositionBoardObjectView(view); Panel.SetZIndex(view, obj.Kind == BoardObjectKind.Connector ? -2 : obj.ZIndex);
        view.Selected += (_, _) => SelectBoardObject(view);
        view.EditRequested += (_, _) => BeginBoardObjectEdit(view);
        view.WidgetActionRequested += BoardObjectWidgetActionRequested;
        view.PluginHostActionRequested += BoardObjectPluginHostActionRequested;
        view.PluginInstallRequested += BoardObjectPluginInstallRequested;
        view.ContextActionsRequested += (_, _) => ShowBoardObjectContextMenu(view);
        view.BreakRequested += (_, segment) => { ClearSelection(); BreakConnector(obj, segment); };
        view.DragStarted += (_, _) => BeginBoardObjectTransform(view);
        view.DragMoved += (_, _) =>
        {
            obj.Width = Math.Min(obj.Width, _activeWorkspace.WorldWidth);
            obj.Height = Math.Min(obj.Height, _activeWorkspace.WorldHeight);
            if (view.IsMovingTransform) ApplyMagnetToBoardObject(obj);
            else HideAlignmentGuides();
            obj.X = Math.Clamp(obj.X, 0, Math.Max(0, _activeWorkspace.WorldWidth - obj.Width));
            obj.Y = Math.Clamp(obj.Y, 0, Math.Max(0, _activeWorkspace.WorldHeight - obj.Height));
            PositionBoardObjectView(view);
            if (view.IsMovingTransform && _groupDragObjectLead == view && _groupDragObjectPositions.TryGetValue(view, out var origin))
                MoveSelectedGroup(new Vector(obj.X - origin.X, obj.Y - origin.Y), null, view);
            MoveLinkedNodes(ObjectNodeId(obj.Id), new Point(obj.X, obj.Y)); obj.UpdatedAt = DateTimeOffset.UtcNow;
            RefreshConnectorsForNodes(ConnectedComponent(ObjectNodeId(obj.Id)));
            _presenceInputDirty=true;
            QueueBoardObjectRealtime(obj);
            PositionCreativeFormatMenu(obj);
        };
        view.DragCompleted += (_, _) =>
        {
            _creativeMoveSaveTimer.Stop(); FinishAlignmentDrag(); ClearLinkedDrag();
            foreach (var moved in _groupDragObjectPositions.Keys) QueueBoardObjectRealtime(moved.Object);
            _groupDragObjectPositions.Clear(); _groupDragPositions.Clear(); _groupDragObjectLead = null;
            QueueBoardObjectRealtime(obj, flush: true);
        };
        WorkspaceCanvas.Children.Add(view);
        return view;
    }

    private void UpdatePluginViewportZoom()
    {
        foreach (var view in WorkspaceCanvas.Children.OfType<BoardObjectView>())
            view.SetViewportZoom(_workspaceZoom);
    }

    private void RenderCreativeObjects(bool animate = false, bool isFirstVisit = false)
    {
        var index=0;
        foreach (var obj in _activeWorkspace.Objects.OrderBy(o => o.ZIndex))
        {
            var view=AddBoardObjectView(obj);
            if(animate) view.PlayWorkspaceEntrance(index++,isFirstVisit);
        }
    }

    private void SelectBoardObject(BoardObjectView view)
    {
        HideBoardObjectContextMenu();
        if (Mouse.LeftButton == MouseButtonState.Pressed && view.IsSelected
            && _selectedCards.Count + _selectedBoardObjectViews.Count > 1) return;
        ClearSelection();
        _selectedBoardObjectView = view;
        _selectedBoardObjectViews.Add(view);
        view.SetSelectionColor(LocalPresenceColor());
        view.SetSelected(true);
        ShowCreativeFormatMenu(view.Object, view);
    }

    private void ClearBoardObjectSelection()
    {
        foreach (var view in _selectedBoardObjectViews) view.SetSelected(false);
        _selectedBoardObjectViews.Clear();
        _selectedBoardObjectView?.SetSelected(false);
        _selectedBoardObjectView = null;
        HideCreativeFormatMenu();
        HideBoardObjectContextMenu();
    }

    private bool DeleteSelectedBoardObject()
    {
        if (_selectedBoardObjectView is null) return false;
        RegisterUndoSnapshot();
        RemoveNodesFromConnectors([ObjectNodeId(_selectedBoardObjectView.Object.Id)]);
        _activeWorkspace.Objects.Remove(_selectedBoardObjectView.Object);
        WorkspaceCanvas.Children.Remove(_selectedBoardObjectView);
        _selectedBoardObjectView = null;
        HideCreativeFormatMenu();
        Save();
        ShowToast("Objeto excluído");
        return true;
    }

    private void BeginBoardObjectEdit(BoardObjectView view)
    {
        if (view.Object.Kind is not (BoardObjectKind.Text or BoardObjectKind.StickyNote)) return;
        if (_editingBoardObjectId is not null) return;
        _editingBoardObjectId = view.Object.Id;
        RegisterUndoSnapshot();
        var originalText = view.Object.Content.GetValueOrDefault("text", "");
        view.Visibility = Visibility.Hidden;
        var note = view.Object.Kind == BoardObjectKind.StickyNote;
        var originalSize = new Size(view.Object.Width, view.Object.Height);
        var editor = new TextBox
        {
            Text = view.Object.Content.GetValueOrDefault("text", ""), Width = view.Object.Width, Height = view.Object.Height,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = Number(view.Object.Style.GetValueOrDefault("fontSize"), note ? 16 : 18), Padding = note ? new Thickness(16) : new Thickness(4),
            TextAlignment = ReadAlignment(view.Object.Style.GetValueOrDefault("align", note ? "center" : "left")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(view.Object.Style.GetValueOrDefault("color", note ? "#2A2417" : (_isDarkMode ? "#F8FAFC" : "#1F2937")))),
            Background = note ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(view.Object.Style.GetValueOrDefault("fill", "#DCCBFF"))) : new SolidColorBrush(Color.FromArgb(220, 16, 26, 43)),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A78BFA")), BorderThickness = new Thickness(1.5), CaretBrush = Brushes.White,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Canvas.SetLeft(editor, view.Object.X); Canvas.SetTop(editor, view.Object.Y); Panel.SetZIndex(editor, 130); WorkspaceCanvas.Children.Add(editor);
        editor.TextChanged += (_, _) =>
        {
            if (note)
            {
                var measuredHeight = MeasureNoteHeight(editor.Text, editor.FontSize, editor.Width);
                editor.Height = Math.Max(originalSize.Height, measuredHeight);
                view.Object.Width = editor.Width; view.Object.Height = editor.Height;
                PositionCreativeFormatMenu(view.Object);
            }
            view.Object.Content["text"] = editor.Text;
            view.Object.UpdatedAt = DateTimeOffset.UtcNow;
            QueueBoardObjectRealtime(view.Object);
        };
        var completed = false;
        void Complete(bool save)
        {
            if (completed) return; completed = true;
            if (save)
            {
                view.Object.Content["text"] = string.IsNullOrWhiteSpace(editor.Text) ? (note ? "Nova nota" : "Texto") : editor.Text.TrimEnd();
                if (note) { view.Object.Width = editor.Width; view.Object.Height = editor.Height; }
                else ResizeTextToContent(view.Object);
                view.Object.UpdatedAt = DateTimeOffset.UtcNow; view.RefreshFromObject(); Save(queueCloud:false);
            }
            else
            {
                view.Object.Content["text"] = originalText;
                if (note) { view.Object.Width = originalSize.Width; view.Object.Height = originalSize.Height; }
                view.Object.UpdatedAt = DateTimeOffset.UtcNow;
            }
            WorkspaceCanvas.Children.Remove(editor); view.Visibility = Visibility.Visible; view.RefreshFromObject(); view.Focus();
            _editingBoardObjectId = null;
            QueueBoardObjectRealtime(view.Object,flush:true);
        }
        editor.LostKeyboardFocus += (_, _) => Complete(true);
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Complete(false); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Complete(true); e.Handled = true; }
        };
        editor.Focus(); editor.SelectAll();
    }

    private void ShowCreativeFormatMenu(BoardObject obj, BoardObjectView view)
    {
        if (IsPluginObject(obj))
        {
            ShowPluginAccentMenu(view);
            return;
        }
        _formatObject = obj;
        CreativeFormatMenuContent.Children.Clear();
        if (obj.Kind is BoardObjectKind.Text or BoardObjectKind.StickyNote)
        {
            var sizes = obj.Kind == BoardObjectKind.StickyNote ? new[] { 24d, 48d, 88d } : new[] { 24d, 56d, 112d };
            CreativeFormatMenuContent.Children.Add(FormatRow("Tamanho", [
                FormatButton("A", () => ApplyObjectStyle(obj, view, "fontSize", sizes[0].ToString(CultureInfo.InvariantCulture)), 10),
                FormatButton("A", () => ApplyObjectStyle(obj, view, "fontSize", sizes[1].ToString(CultureInfo.InvariantCulture)), 18),
                FormatButton("A", () => ApplyObjectStyle(obj, view, "fontSize", sizes[2].ToString(CultureInfo.InvariantCulture)), 27)
            ]));
            CreativeFormatMenuContent.Children.Add(FormatRow("Alinhar", [
                FormatButton("\uE236", () => ApplyObjectStyle(obj, view, "align", "left"), materialIcon: true),
                FormatButton("\uE234", () => ApplyObjectStyle(obj, view, "align", "center"), materialIcon: true),
                FormatButton("\uE237", () => ApplyObjectStyle(obj, view, "align", "right"), materialIcon: true)
            ]));
            CreativeFormatMenuContent.Children.Add(FormatRow("Cor do texto", TextColorButtons(obj, view)));
            if (obj.Kind == BoardObjectKind.StickyNote)
                CreativeFormatMenuContent.Children.Add(FormatRow("Nota", FillColorButtons(obj, view)));
        }
        else if (obj.Kind == BoardObjectKind.Shape)
        {
            CreativeFormatMenuContent.Children.Add(FormatRow("Forma", [
                FormatButton("\uE836", () => { _shapeKind = "circle"; ApplyObjectStyle(obj, view, "shape", "circle"); }, materialIcon: true, toolTip: "Círculo"),
                FormatButton("\uE835", () => { _shapeKind = "square"; ApplyObjectStyle(obj, view, "shape", "square"); }, materialIcon: true, toolTip: "Quadrado"),
                FormatButton("\uE86B", () => { _shapeKind = "triangle"; ApplyObjectStyle(obj, view, "shape", "triangle"); }, materialIcon: true, toolTip: "Triângulo"),
                FormatButton("╱", () => { _shapeKind = "line"; ApplyObjectStyle(obj, view, "shape", "line"); }, 19, toolTip: "Linha")
            ]));
            CreativeFormatMenuContent.Children.Add(FormatRow("Contorno", ShapeStrokeColorButtons(obj, view)));
            CreativeFormatMenuContent.Children.Add(FormatRow("Preencher", FillColorButtons(obj, view, rememberForShape: true)));
            CreativeFormatMenuContent.Children.Add(FormatRow("Espessura", [
                FormatButton("1", () => ApplyShapeThickness(obj, view, 1), toolTip: "Contorno fino"),
                FormatButton("2", () => ApplyShapeThickness(obj, view, 2), toolTip: "Contorno médio"),
                FormatButton("4", () => ApplyShapeThickness(obj, view, 4), toolTip: "Contorno espesso")
            ]));
        }
        else if (obj.Kind == BoardObjectKind.Stroke)
        {
            CreativeFormatMenuContent.Children.Add(FormatRow("Traço", [
                FormatButton("•", () => ApplyPenSize(obj, view, 4), 12),
                FormatButton("●", () => ApplyPenSize(obj, view, 14), 16),
                FormatButton("●", () => ApplyPenSize(obj, view, 32), 25),
                FormatButton("\uE6D3", () => ApplyPenMode(obj, view, false), 20, materialIcon: true, toolTip: "Caneta"),
                FormatButton("\uE6D1", () => ApplyPenMode(obj, view, true), 20, materialIcon: true, toolTip: "Marca-texto")
            ]));
            CreativeFormatMenuContent.Children.Add(FormatRow("Cor", [
                ColorButton("#A78BFA", obj, view), ColorButton("#38BDF8", obj, view),
                ColorButton("#34D399", obj, view), ColorButton("#FBBF24", obj, view), ColorButton("#FB7185", obj, view)
            ]));
        }
        else return;
        CreativeFormatMenu.Visibility = Visibility.Visible;
        CreativeFormatMenu.UpdateLayout();
        PositionCreativeFormatMenu(obj);
    }

    private StackPanel FormatRow(string label, IEnumerable<Button> buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        row.Children.Add(new TextBlock { Text = label, Width = 53, FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_isDarkMode ? "#91A1B7" : "#64748B")), VerticalAlignment = VerticalAlignment.Center });
        foreach (var button in buttons) row.Children.Add(button);
        return row;
    }

    private Button FormatButton(string text, Action action, double fontSize = 13, bool materialIcon = false, string? toolTip = null)
    {
        var content = new TextBlock { Text = text, FontSize = fontSize, FontFamily = materialIcon ? (FontFamily)FindResource("MaterialSymbols") : FontFamily, Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#243044")!, TextAlignment = TextAlignment.Center };
        var button = new Button { Content = content, Height = 38, MinWidth = text.Length > 2 ? 52 : 38, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(2, 0, 0, 0), Background = new SolidColorBrush(Color.FromArgb(42, 125, 107, 210)), ToolTip = toolTip };
        button.Click += (_, e) => { action(); e.Handled = true; };
        return button;
    }

    private IEnumerable<Button> TextColorButtons(BoardObject obj, BoardObjectView view) =>
    [
        ColorButton("#F8FAFC", obj, view, "color"), ColorButton("#1F2937", obj, view, "color"),
        ColorButton("#A78BFA", obj, view, "color"), ColorButton("#38BDF8", obj, view, "color"),
        ColorButton("#FB7185", obj, view, "color")
    ];

    private IEnumerable<Button> ShapeStrokeColorButtons(BoardObject obj, BoardObjectView view) =>
    [
        ColorButton("#A78BFA", obj, view, "stroke", color => _shapeStrokeColor = color),
        ColorButton("#38BDF8", obj, view, "stroke", color => _shapeStrokeColor = color),
        ColorButton("#34D399", obj, view, "stroke", color => _shapeStrokeColor = color),
        ColorButton("#FBBF24", obj, view, "stroke", color => _shapeStrokeColor = color),
        ColorButton("#FB7185", obj, view, "stroke", color => _shapeStrokeColor = color)
    ];

    private IEnumerable<Button> FillColorButtons(BoardObject obj, BoardObjectView view, bool rememberForShape = false) =>
    [
        ColorButton("#00000000", obj, view, "fill", rememberForShape ? color => _shapeFillColor = color : null, "Sem preenchimento"),
        ColorButton("#241096F3", obj, view, "fill", rememberForShape ? color => _shapeFillColor = color : null),
        ColorButton("#4438BDF8", obj, view, "fill", rememberForShape ? color => _shapeFillColor = color : null),
        ColorButton("#4434D399", obj, view, "fill", rememberForShape ? color => _shapeFillColor = color : null),
        ColorButton("#44FB7185", obj, view, "fill", rememberForShape ? color => _shapeFillColor = color : null)
    ];

    private Button ColorButton(string color, BoardObject obj, BoardObjectView view, string styleKey = "stroke", Action<string>? remember = null, string? toolTip = null)
    {
        var button = new Button { Width = 30, Height = 30, Padding = new Thickness(6), Margin = new Thickness(2, 0, 0, 0), ToolTip = toolTip };
        button.Content = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), StrokeThickness = 1 };
        button.Click += (_, e) => { if (styleKey == "stroke" && obj.Kind == BoardObjectKind.Stroke) _penColor = color; remember?.Invoke(color); ApplyObjectStyle(obj, view, styleKey, color); e.Handled = true; };
        return button;
    }

    private void ApplyShapeThickness(BoardObject obj, BoardObjectView view, double thickness)
    {
        _shapeThickness = thickness;
        ApplyObjectStyle(obj, view, "thickness", thickness.ToString(CultureInfo.InvariantCulture));
    }

    private void ApplyObjectStyle(BoardObject obj, BoardObjectView view, string key, string value)
    {
        RegisterUndoSnapshot();
        obj.Style[key] = value;
        if (obj.Kind == BoardObjectKind.StickyNote) EnsureNoteContainsContent(obj);
        else if (obj.Kind == BoardObjectKind.Text && key == "fontSize") ResizeTextToContent(obj);
        obj.UpdatedAt = DateTimeOffset.UtcNow;
        view.RefreshFromObject();
        Save();
        PositionCreativeFormatMenu(obj);
    }

    private void ApplyPenSize(BoardObject obj, BoardObjectView view, double size)
    {
        _penThickness = size;
        ApplyObjectStyle(obj, view, "thickness", size.ToString(CultureInfo.InvariantCulture));
    }

    private void ApplyPenMode(BoardObject obj, BoardObjectView view, bool highlighter)
    {
        _penHighlighter = highlighter;
        if (highlighter && _penThickness < 8) _penThickness = 12;
        ApplyObjectStyle(obj, view, "mode", highlighter ? "highlight" : "draw");
        if (highlighter) ApplyObjectStyle(obj, view, "thickness", _penThickness.ToString(CultureInfo.InvariantCulture));
    }

    private void EnsureNoteContainsContent(BoardObject obj)
    {
        var text = obj.Content.GetValueOrDefault("text", "Nova nota");
        obj.Width = Math.Clamp(Math.Max(220, obj.Width), 220, 1000);
        obj.Height = Math.Max(obj.Height, MeasureNoteHeight(text, Number(obj.Style.GetValueOrDefault("fontSize"), 24), obj.Width));
    }

    private static Size MeasureNote(string text, double fontSize)
    {
        var baseWidth = text.Length switch { < 80 => 220d, < 180 => 280d, _ => 340d };
        var width = Math.Clamp(baseWidth * Math.Sqrt(Math.Max(1, fontSize / 24)), 220, 520);
        var probe = new TextBlock { Text = text, FontSize = fontSize, TextWrapping = TextWrapping.Wrap, Width = width - 32, FontFamily = new FontFamily("Segoe UI Variable Text") };
        probe.Measure(new Size(width - 32, double.PositiveInfinity));
        return new Size(width, Math.Clamp(probe.DesiredSize.Height + 42, 110, 1000));
    }

    private static double MeasureNoteHeight(string text, double fontSize, double width)
    {
        var usableWidth = Math.Max(48, width - 32);
        var probe = new TextBlock { Text = text, FontSize = fontSize, TextWrapping = TextWrapping.Wrap, Width = usableWidth, FontFamily = new FontFamily("Segoe UI Variable Text") };
        probe.Measure(new Size(usableWidth, double.PositiveInfinity));
        return Math.Clamp(probe.DesiredSize.Height + 42, 110, 1400);
    }

    private static void ResizeTextToContent(BoardObject obj)
    {
        var text=obj.Content.GetValueOrDefault("text","Texto");var fontSize=Number(obj.Style.GetValueOrDefault("fontSize"),24);var width=Math.Clamp(Math.Sqrt(Math.Max(1,text.Length))*fontSize*2.2,180,900);
        var probe=new TextBlock{Text=text,FontSize=fontSize,TextWrapping=TextWrapping.Wrap,Width=width-8,FontFamily=new FontFamily("Segoe UI Variable Text")};probe.Measure(new Size(width-8,double.PositiveInfinity));obj.Width=width;obj.Height=Math.Clamp(probe.DesiredSize.Height+12,fontSize*1.45,1000);
    }

    private void PositionCreativeFormatMenu(BoardObject obj)
    {
        if (CreativeFormatMenu.Visibility != Visibility.Visible || _formatObject != obj) return;
        var x = obj.X * _workspaceZoom - WorkspaceScroll.HorizontalOffset;
        var y = obj.Y * _workspaceZoom - WorkspaceScroll.VerticalOffset - CreativeFormatMenu.ActualHeight - 10;
        x = Math.Clamp(x, 12, Math.Max(12, Root.ActualWidth - Math.Max(280, CreativeFormatMenu.ActualWidth) - 12));
        y = Math.Clamp(y, 82, Math.Max(82, Root.ActualHeight - CreativeFormatMenu.ActualHeight - 12));
        CreativeFormatMenu.Margin = new Thickness(x, y, 0, 0);
    }

    private void HideCreativeFormatMenu()
    {
        _formatObject = null;
        if (CreativeFormatMenu is not null) CreativeFormatMenu.Visibility = Visibility.Collapsed;
    }

    private static TextAlignment ReadAlignment(string value) => value switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left };

    private static double Number(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static Brush StrokeBrush(string color, bool highlighter)
    {
        var parsed = (Color)ColorConverter.ConvertFromString(color);
        if (highlighter) parsed.A = 92;
        return new SolidColorBrush(parsed);
    }

    private void ConnectCard(ItemCard card) => ConnectNode(CardNodeId(card.Item.Id));

    private void ConnectNode(string nodeId)
    {
        if (_connectorCardIds.Contains(nodeId)) { if (_connectorCardIds.Count <= 2) return; _connectorCardIds.Remove(nodeId); }
        else _connectorCardIds.Add(nodeId);
        UpdateConnectorSelectionVisuals();
        if (_connectorCardIds.Count == 1) { ShowToast("Agora selecione outro elemento"); return; }
        RegisterUndoSnapshot();
        if (_activeConnectorObject is null)
        {
            _activeConnectorObject = NewObject(BoardObjectKind.Connector, 0, 0, 8, 8, new() { ["stroke"] = "#8393AD", ["thickness"] = "1.35" }, new() { ["nodeIds"] = string.Join(';', _connectorCardIds) });
            _activeWorkspace.Objects.Add(_activeConnectorObject); UpdateConnectorGeometry(_activeConnectorObject); AddBoardObjectView(_activeConnectorObject);
        }
        else { _activeConnectorObject.Content["nodeIds"] = string.Join(';', _connectorCardIds); UpdateConnectorGeometry(_activeConnectorObject); RefreshConnectorView(_activeConnectorObject); }
        _activeConnectorObject.UpdatedAt = DateTimeOffset.UtcNow; Save(); ShowToast("Conexão criada");
        SetCreativeTool(CreativeTool.Select);
    }

    private void UpdateConnectorSelectionVisuals()
    {
        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>()) card.SetSelected(_connectorCardIds.Contains(CardNodeId(card.Item.Id)));
        foreach (var view in WorkspaceCanvas.Children.OfType<BoardObjectView>().Where(v => v.Object.Kind != BoardObjectKind.Connector))
        {
            var selected = _connectorCardIds.Contains(ObjectNodeId(view.Object.Id));
            if (selected) view.SetSelectionColor(LocalPresenceColor());
            view.SetSelected(selected);
        }
    }

    private void ResetConnectorSession() { _connectorCardIds.Clear(); _activeConnectorObject = null; UpdateConnectorSelectionVisuals(); }

    private bool UpdateConnectorGeometry(BoardObject connector)
    {
        var centers = ConnectorIds(connector).Select(TryGetNodeCenter).Where(point => point.HasValue).Select(point => point!.Value).ToList(); if (centers.Count < 2) return false;
        var left = centers.Min(p => p.X) - 12; var top = centers.Min(p => p.Y) - 12; connector.X = Math.Max(0, left); connector.Y = Math.Max(0, top);
        connector.Width = Math.Max(24, centers.Max(p => p.X) - connector.X + 12); connector.Height = Math.Max(24, centers.Max(p => p.Y) - connector.Y + 12);
        connector.Content["points"] = SerializePoints(centers.Select(p => new Point(p.X - connector.X, p.Y - connector.Y))); return true;
    }

    private Point? TryGetNodeCenter(string nodeId)
    {
        if (nodeId.StartsWith("card:", StringComparison.Ordinal))
        {
            var id=nodeId[5..]; var card=WorkspaceCanvas.Children.OfType<ItemCard>().FirstOrDefault(candidate=>candidate.Item.Id==id); if(card is null)return null;
            return new Point(Canvas.GetLeft(card)+Math.Max(card.ActualWidth,card.Width)/2,Canvas.GetTop(card)+Math.Max(card.ActualHeight,card.Height)/2);
        }
        if (nodeId.StartsWith("object:", StringComparison.Ordinal))
        {
            var id=nodeId[7..]; var obj=_activeWorkspace.Objects.FirstOrDefault(candidate=>candidate.Id==id&&candidate.Kind!=BoardObjectKind.Connector); if(obj is null)return null;
            return new Point(obj.X+obj.Width/2,obj.Y+obj.Height/2);
        }
        return null;
    }

    private void RefreshConnectorView(BoardObject connector) { var view=WorkspaceCanvas.Children.OfType<BoardObjectView>().FirstOrDefault(v=>v.Object==connector); if(view is null)return; view.RefreshFromObject(); PositionBoardObjectView(view); }
    private void RefreshConnectorsForNodes(IEnumerable<string> nodeIds) { var changed=nodeIds.ToHashSet(StringComparer.Ordinal); foreach(var connector in _activeWorkspace.Objects.Where(o=>o.Kind==BoardObjectKind.Connector&&ConnectorIds(o).Any(changed.Contains))) if(UpdateConnectorGeometry(connector))RefreshConnectorView(connector); }
    private void RefreshConnectorsForCards(IEnumerable<string> cardIds)=>RefreshConnectorsForNodes(cardIds.Select(CardNodeId));
    private void RefreshAllCardConnectors(){foreach(var connector in _activeWorkspace.Objects.Where(o=>o.Kind==BoardObjectKind.Connector))if(UpdateConnectorGeometry(connector))RefreshConnectorView(connector);}

    private void ExpandConnectedDragSelection(ItemCard lead)
    {
        if(!_moveConnectedTogether)return;var connected=ConnectedComponent(CardNodeId(lead.Item.Id));PrepareLinkedDrag(CardNodeId(lead.Item.Id),new Point(Canvas.GetLeft(lead),Canvas.GetTop(lead)),connected);
        foreach(var card in WorkspaceCanvas.Children.OfType<ItemCard>().Where(c=>connected.Contains(CardNodeId(c.Item.Id)))){if(!_selectedCards.Contains(card))_selectedCards.Add(card);card.SetSelected(true);}
    }

    private HashSet<string> ConnectedComponent(string seed)
    {
        var result=new HashSet<string>(StringComparer.Ordinal){seed};var queue=new Queue<string>();queue.Enqueue(seed);var connectors=_activeWorkspace.Objects.Where(o=>o.Kind==BoardObjectKind.Connector).Select(ConnectorIds).Where(ids=>ids.Count>1).ToList();
        while(queue.TryDequeue(out var current))foreach(var ids in connectors.Where(ids=>ids.Contains(current)))foreach(var id in ids)if(result.Add(id))queue.Enqueue(id);return result;
    }

    private void RemoveCardsFromConnectors(IEnumerable<string> removedIds)=>RemoveNodesFromConnectors(removedIds.Select(CardNodeId));
    private void RemoveNodesFromConnectors(IEnumerable<string> removedIds)
    {
        var removed=removedIds.ToHashSet(StringComparer.Ordinal);foreach(var connector in _activeWorkspace.Objects.Where(o=>o.Kind==BoardObjectKind.Connector).ToList())
        {var remaining=ConnectorIds(connector).Where(id=>!removed.Contains(id)).ToList();if(remaining.Count<2)RemoveConnector(connector);else{connector.Content["nodeIds"]=string.Join(';',remaining);UpdateConnectorGeometry(connector);RefreshConnectorView(connector);}}
    }

    private void BreakConnector(BoardObject connector,int segment)
    {
        var nodes=ConnectorIds(connector);if(segment<0||segment>=nodes.Count-1)return;RegisterUndoSnapshot();var left=nodes.Take(segment+1).ToList();var right=nodes.Skip(segment+1).ToList();RemoveConnector(connector);
        foreach(var part in new[]{left,right}.Where(part=>part.Count>=2)){var split=NewObject(BoardObjectKind.Connector,0,0,8,8,new(){["stroke"]="#8393AD",["thickness"]="1.35"},new(){["nodeIds"]=string.Join(';',part)});_activeWorkspace.Objects.Add(split);UpdateConnectorGeometry(split);AddBoardObjectView(split);}Save();ShowToast("Conexão removida");
    }
    private void RemoveConnector(BoardObject connector){_activeWorkspace.Objects.Remove(connector);var view=WorkspaceCanvas.Children.OfType<BoardObjectView>().FirstOrDefault(v=>v.Object==connector);if(view is not null)WorkspaceCanvas.Children.Remove(view);}

    private static List<string> ConnectorIds(BoardObject connector)
    {
        var source=connector.Content.GetValueOrDefault("nodeIds","");if(string.IsNullOrWhiteSpace(source))source=string.Join(';',connector.Content.GetValueOrDefault("cardIds","").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(CardNodeId));
        return source.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
    }
    private static string CardNodeId(string id)=>$"card:{id}";
    private static string ObjectNodeId(string id)=>$"object:{id}";

    private int NextBoardZIndex()
    {
        var highestCard=_items.Count==0?0:_items.Max(item=>item.ZIndex);var objects=_activeWorkspace.Objects.Where(obj=>obj.Kind!=BoardObjectKind.Connector).ToList();var highestObject=objects.Count==0?0:objects.Max(obj=>obj.ZIndex);
        var next=Math.Max(highestCard,highestObject)+1;if(next<1_000_000)return next;
        var ordered=WorkspaceCanvas.Children.Cast<UIElement>().Where(element=>element is ItemCard||element is BoardObjectView {Object.Kind:not BoardObjectKind.Connector}).OrderBy(Panel.GetZIndex).ToList();var z=1;
        foreach(var element in ordered){Panel.SetZIndex(element,z);if(element is ItemCard card)card.Item.ZIndex=z;else if(element is BoardObjectView view)view.Object.ZIndex=z;z++;}return z;
    }

    private void PositionBoardObjectView(BoardObjectView view){Canvas.SetLeft(view,view.LayoutLeft);Canvas.SetTop(view,view.LayoutTop);Panel.SetZIndex(view,view.Object.Kind==BoardObjectKind.Connector?-2:view.Object.ZIndex);}
    private void BringBoardObjectToFront(BoardObjectView view){if(view.Object.Kind==BoardObjectKind.Connector)return;view.Object.ZIndex=NextBoardZIndex();Panel.SetZIndex(view,view.Object.ZIndex);}

    private void BeginBoardObjectTransform(BoardObjectView view)
    {
        RegisterUndoSnapshot(); BringBoardObjectToFront(view);
        _groupDragObjectLead = view.IsMovingTransform ? view : null;
        _groupDragPositions.Clear();
        _groupDragObjectPositions.Clear();
        if (view.IsMovingTransform)
        {
            foreach (var card in _selectedCards.Where(card => card.Visibility == Visibility.Visible))
                _groupDragPositions[card] = new Point(Canvas.GetLeft(card), Canvas.GetTop(card));
            CaptureSelectedBoardObjectDragPositions();
        }
        var node=ObjectNodeId(view.Object.Id);
        if(_moveConnectedTogether)PrepareLinkedDrag(node,new Point(view.Object.X,view.Object.Y),ConnectedComponent(node));
        PrepareAlignmentTargets(_groupDragPositions.Keys.Concat(_linkedCardDragOrigins.Keys),
            _groupDragObjectPositions.Keys.Select(selected => selected.Object).Concat(_linkedObjectDragOrigins.Keys).Append(view.Object));
    }

    private void PrepareLinkedDrag(string leadId,Point origin,IEnumerable<string> connected)
    {
        _linkedDragLeadId=leadId;_linkedDragLeadOrigin=origin;_linkedObjectDragOrigins.Clear();_linkedCardDragOrigins.Clear();
        foreach(var node in connected)
        {
            if(node.StartsWith("card:",StringComparison.Ordinal)){var id=node[5..];var card=WorkspaceCanvas.Children.OfType<ItemCard>().FirstOrDefault(candidate=>candidate.Item.Id==id);if(card is not null)_linkedCardDragOrigins[card]=new Point(Canvas.GetLeft(card),Canvas.GetTop(card));}
            else if(node.StartsWith("object:",StringComparison.Ordinal)){var id=node[7..];var obj=_activeWorkspace.Objects.FirstOrDefault(candidate=>candidate.Id==id&&candidate.Kind!=BoardObjectKind.Connector);if(obj is not null)_linkedObjectDragOrigins[obj]=new Point(obj.X,obj.Y);}
        }
    }

    private void MoveLinkedNodes(string leadId,Point current)
    {
        if(!_moveConnectedTogether||_linkedDragLeadId!=leadId)return;var delta=current-_linkedDragLeadOrigin;
        if(leadId.StartsWith("object:",StringComparison.Ordinal))foreach(var (card,origin) in _linkedCardDragOrigins){var position=ClampCardPosition(card,origin+delta);Canvas.SetLeft(card,position.X);Canvas.SetTop(card,position.Y);card.Item.X=position.X;card.Item.Y=position.Y;}
        foreach(var (obj,origin) in _linkedObjectDragOrigins){if(ObjectNodeId(obj.Id)==leadId)continue;obj.X=Math.Clamp(origin.X+delta.X,0,Math.Max(0,_activeWorkspace.WorldWidth-obj.Width));obj.Y=Math.Clamp(origin.Y+delta.Y,0,Math.Max(0,_activeWorkspace.WorldHeight-obj.Height));var view=WorkspaceCanvas.Children.OfType<BoardObjectView>().FirstOrDefault(candidate=>candidate.Object==obj);if(view is not null)PositionBoardObjectView(view);obj.UpdatedAt=DateTimeOffset.UtcNow;}
    }
    private void ClearLinkedDrag(){_linkedDragLeadId=null;_linkedObjectDragOrigins.Clear();_linkedCardDragOrigins.Clear();}

    private void PrepareAlignmentTargets(IEnumerable<ItemCard> excludedCards, IEnumerable<BoardObject> excludedObjects)
    {
        _alignmentTargets.Clear();
        if (!_magnetAlignmentEnabled) return;
        var cardExclusions = excludedCards.ToHashSet();
        var objectExclusions = excludedObjects.ToHashSet();
        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>().Where(card => card.Visibility == Visibility.Visible && !cardExclusions.Contains(card)))
        {
            var width = Math.Max(card.ActualWidth, card.Width) * card.WorkspaceScaleFactor;
            var height = Math.Max(card.ActualHeight, card.Height) * card.WorkspaceScaleFactor;
            _alignmentTargets.Add(new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), width, height));
        }
        foreach (var view in WorkspaceCanvas.Children.OfType<BoardObjectView>().Where(view => view.Visibility == Visibility.Visible && view.Object.Kind != BoardObjectKind.Connector && !objectExclusions.Contains(view.Object)))
            _alignmentTargets.Add(new Rect(view.Object.X, view.Object.Y, view.Object.Width, view.Object.Height));
    }

    private Point SnapAlignment(Rect moving, out double? guideX, out double? guideY)
    {
        guideX = null;
        guideY = null;
        if (!_magnetAlignmentEnabled || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return moving.TopLeft;
        var tolerance = Math.Clamp(9 / Math.Max(_workspaceZoom, BoardViewport.MinimumZoom), 6, 44);
        var bestX = tolerance + 1;
        var bestY = tolerance + 1;
        var deltaX = 0d;
        var deltaY = 0d;
        double? matchedGuideX = null;
        double? matchedGuideY = null;
        var movingX = new[] { moving.Left, moving.Left + moving.Width / 2, moving.Right };
        var movingY = new[] { moving.Top, moving.Top + moving.Height / 2, moving.Bottom };

        void MatchX(double target)
        {
            foreach (var source in movingX)
            {
                var distance = Math.Abs(target - source);
                if (distance > tolerance || distance >= bestX) continue;
                bestX = distance;
                deltaX = target - source;
                matchedGuideX = target;
            }
        }

        void MatchY(double target)
        {
            foreach (var source in movingY)
            {
                var distance = Math.Abs(target - source);
                if (distance > tolerance || distance >= bestY) continue;
                bestY = distance;
                deltaY = target - source;
                matchedGuideY = target;
            }
        }

        MatchX(_activeWorkspace.WorldWidth / 2);
        MatchY(_activeWorkspace.WorldHeight / 2);
        foreach (var target in _alignmentTargets)
        {
            MatchX(target.Left);
            MatchX(target.Left + target.Width / 2);
            MatchX(target.Right);
            MatchY(target.Top);
            MatchY(target.Top + target.Height / 2);
            MatchY(target.Bottom);
        }
        guideX = matchedGuideX;
        guideY = matchedGuideY;
        return new Point(moving.X + deltaX, moving.Y + deltaY);
    }

    private void ApplyMagnetToCard(ItemCard card)
    {
        if (!_magnetAlignmentEnabled || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { HideAlignmentGuides(); return; }
        var width = Math.Max(card.ActualWidth, card.Width) * card.WorkspaceScaleFactor;
        var height = Math.Max(card.ActualHeight, card.Height) * card.WorkspaceScaleFactor;
        var snapped = SnapAlignment(new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), width, height), out var guideX, out var guideY);
        snapped = ClampCardPosition(card, snapped);
        Canvas.SetLeft(card, snapped.X);
        Canvas.SetTop(card, snapped.Y);
        card.Item.X = snapped.X;
        card.Item.Y = snapped.Y;
        ShowAlignmentGuides(guideX, guideY);
    }

    private void ApplyMagnetToBoardObject(BoardObject obj)
    {
        if (!_magnetAlignmentEnabled || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { HideAlignmentGuides(); return; }
        var snapped = SnapAlignment(new Rect(obj.X, obj.Y, obj.Width, obj.Height), out var guideX, out var guideY);
        obj.X = Math.Clamp(snapped.X, 0, Math.Max(0, _activeWorkspace.WorldWidth - obj.Width));
        obj.Y = Math.Clamp(snapped.Y, 0, Math.Max(0, _activeWorkspace.WorldHeight - obj.Height));
        ShowAlignmentGuides(guideX, guideY);
    }

    private void ShowAlignmentGuides(double? worldX, double? worldY)
    {
        var visible = false;
        if (worldX is { } x)
        {
            var screenX = x * _workspaceZoom - WorkspaceScroll.HorizontalOffset;
            if (screenX >= 0 && screenX <= Root.ActualWidth)
            {
                VerticalAlignmentGuide.X1 = screenX;
                VerticalAlignmentGuide.X2 = screenX;
                VerticalAlignmentGuide.Y1 = 82;
                VerticalAlignmentGuide.Y2 = Root.ActualHeight;
                VerticalAlignmentGuide.Visibility = Visibility.Visible;
                visible = true;
            }
            else VerticalAlignmentGuide.Visibility = Visibility.Collapsed;
        }
        else VerticalAlignmentGuide.Visibility = Visibility.Collapsed;

        if (worldY is { } y)
        {
            var screenY = y * _workspaceZoom - WorkspaceScroll.VerticalOffset;
            if (screenY >= 0 && screenY <= Root.ActualHeight)
            {
                HorizontalAlignmentGuide.X1 = 0;
                HorizontalAlignmentGuide.X2 = Root.ActualWidth;
                HorizontalAlignmentGuide.Y1 = screenY;
                HorizontalAlignmentGuide.Y2 = screenY;
                HorizontalAlignmentGuide.Visibility = Visibility.Visible;
                visible = true;
            }
            else HorizontalAlignmentGuide.Visibility = Visibility.Collapsed;
        }
        else HorizontalAlignmentGuide.Visibility = Visibility.Collapsed;
        AlignmentGuideLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideAlignmentGuides()
    {
        if (AlignmentGuideLayer is null) return;
        AlignmentGuideLayer.Visibility = Visibility.Collapsed;
        VerticalAlignmentGuide.Visibility = Visibility.Collapsed;
        HorizontalAlignmentGuide.Visibility = Visibility.Collapsed;
    }

    private void FinishAlignmentDrag()
    {
        _alignmentTargets.Clear();
        HideAlignmentGuides();
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void CancelCreativeDraft()
    {
        if (_draftStroke is not null) WorkspaceCanvas.Children.Remove(_draftStroke);
        if (_draftShape is not null) WorkspaceCanvas.Children.Remove(_draftShape);
        _draftStroke = null; _draftShape = null; _creativePointerDown = false;
        if (Mouse.Captured == Root) Root.ReleaseMouseCapture();
    }

    private void FinishCreativePointer()
    {
        _creativePointerDown = false; _creativeToolPanning = false;
        if (Mouse.Captured == Root) Root.ReleaseMouseCapture();
        Cursor = _activeCreativeTool == CreativeTool.Pan ? Cursors.Hand : Cursors.Arrow;
    }

    private bool HandleCreativeShortcut(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return false;
        if (e.Key == Key.M) { ToggleMagnetAlignment(); e.Handled = true; return true; }
        var tool = e.Key switch
        {
            Key.V => CreativeTool.Select, Key.H => CreativeTool.Pan, Key.P => CreativeTool.Pen, Key.T => CreativeTool.Text,
            Key.N => CreativeTool.Note, Key.R => CreativeTool.Shape, Key.C => CreativeTool.Connector, _ => (CreativeTool?)null
        };
        if (tool is null) return false;
        SetCreativeTool(tool.Value); e.Handled = true; return true;
    }

    private Point ClampCreativePoint(Point point) => new(Math.Clamp(point.X, 0, _activeWorkspace.WorldWidth), Math.Clamp(point.Y, 0, _activeWorkspace.WorldHeight));
    private static Rect RectFromPoints(Point a, Point b) => new(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    private static string SerializePoints(IEnumerable<Point> points) => string.Join(';', points.Select(p => $"{p.X.ToString("0.###", CultureInfo.InvariantCulture)},{p.Y.ToString("0.###", CultureInfo.InvariantCulture)}"));
}
