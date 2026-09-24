using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ClipDesk.Core;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Views;

public sealed partial class BoardObjectView : Canvas
{
    public const double SelectionPadding = 14;
    public const double RotationSpace = 34;
    private const double HandleRadius = 5;
    private Point _pointerStart;
    private Rect _objectStart;
    private double _rotationStart;
    private Point _rotationCenter;
    private TransformAction _action;
    private bool _transformStarted;
    private bool _draggingVisual;
    private SolidColorBrush _selectionColor = new((Color)ColorConverter.ConvertFromString("#22D3EE"));
    private SolidColorBrush? _collaboratorSelection;
    private readonly ScaleTransform _motionScale = new(1, 1);
    private readonly TranslateTransform _motionTranslate = new();
    private readonly DropShadowEffect _surfaceShadow = new()
    {
        BlurRadius = 22, ShadowDepth = 7, Direction = 270, Opacity = .28,
        Color = Color.FromRgb(2, 6, 23), RenderingBias = RenderingBias.Performance
    };
    private enum TransformAction { None, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight, Rotate }

    public BoardObjectView(BoardObject boardObject)
    {
        Object = boardObject; Cursor = Cursors.Arrow; Focusable = true;
        RenderTransformOrigin = new Point(.5, .5);
        var motion = new TransformGroup(); motion.Children.Add(_motionScale); motion.Children.Add(_motionTranslate); RenderTransform = motion;
        RefreshFromObject();
    }
    public BoardObject Object { get; }
    public bool IsSelected { get; private set; }
    public bool IsInteractive { get; set; } = true;
    public bool IsDarkMode { get; set; } = true;
    public bool IsMovingTransform => _action == TransformAction.Move;
    public double LayoutLeft => Object.Kind == BoardObjectKind.Connector ? Object.X : Object.X - SelectionPadding;
    public double LayoutTop => Object.Kind == BoardObjectKind.Connector ? Object.Y : Object.Y - RotationSpace;

    public event EventHandler? Selected;
    public event EventHandler? DragStarted;
    public event EventHandler? DragMoved;
    public event EventHandler? DragCompleted;
    public event EventHandler? EditRequested;
    public event Action<BoardObjectView, int>? BreakRequested;
    public event Action<BoardObjectView, string>? WidgetActionRequested;
    public event Action<BoardObjectView, PluginHostAction>? PluginHostActionRequested;
    public event Action<BoardObjectView>? PluginInstallRequested;
    public event EventHandler? ContextActionsRequested;

    private bool UsesFloatingVisual => Object.Kind is BoardObjectKind.Text or BoardObjectKind.Shape or BoardObjectKind.StickyNote
        or BoardObjectKind.Checklist or BoardObjectKind.Calculator or BoardObjectKind.Translator or BoardObjectKind.CurrencyConverter
        or BoardObjectKind.Plugin;

    public void SetSelected(bool selected) { IsSelected = selected; Cursor = selected && !IsPluginKind ? Cursors.SizeAll : Cursors.Arrow; InvalidateVisual(); }
    public void SetSelectionColor(SolidColorBrush color) { _selectionColor = color; InvalidateVisual(); }
    public void SetCollaboratorSelection(SolidColorBrush? color) { _collaboratorSelection=color; InvalidateVisual(); }
    public void RefreshFromObject()
    {
        Width = Math.Max(8, Object.Width) + (Object.Kind == BoardObjectKind.Connector ? 0 : SelectionPadding * 2);
        Height = Math.Max(8, Object.Height) + (Object.Kind == BoardObjectKind.Connector ? 0 : RotationSpace + SelectionPadding);
        RefreshPluginSurface();
        Effect = UsesFloatingVisual ? _surfaceShadow : null;
        InvalidateVisual();
    }

    public void PlayPopIn() => PlayEntrance(TimeSpan.Zero, 14, .91, 235);
    public void PlayWorkspaceEntrance(int index, bool isFirstVisit) =>
        PlayEntrance(TimeSpan.FromMilliseconds(Math.Min(index,12)*30+(isFirstVisit?70:0)),isFirstVisit?24:16,.94,300);

    private void PlayEntrance(TimeSpan delay,double offset,double scale,double durationMs)
    {
        if(!UsesFloatingVisual)return;
        Opacity=0; _motionScale.ScaleX=scale; _motionScale.ScaleY=scale; _motionTranslate.Y=offset;
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        var fade=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(Math.Min(durationMs,220))){BeginTime=delay,EasingFunction=ease};
        fade.Completed+=(_,_)=> {BeginAnimation(OpacityProperty,null);Opacity=1;};
        BeginAnimation(OpacityProperty,fade);
        _motionScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(scale,1,TimeSpan.FromMilliseconds(durationMs)){BeginTime=delay,EasingFunction=ease});
        _motionScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(scale,1,TimeSpan.FromMilliseconds(durationMs)){BeginTime=delay,EasingFunction=ease});
        _motionTranslate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(offset,0,TimeSpan.FromMilliseconds(durationMs)){BeginTime=delay,EasingFunction=ease});
    }

    private void SetDraggingVisual(bool active)
    {
        if(_draggingVisual==active)return;
        _draggingVisual=active;
        var duration=TimeSpan.FromMilliseconds(active?90:180); var ease=new CubicEase{EasingMode=EasingMode.EaseOut}; var scale=active?1.018:1;
        _motionScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(scale,duration){EasingFunction=ease});
        _motionScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(scale,duration){EasingFunction=ease});
        if(UsesFloatingVisual)
        {
            _surfaceShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,new DoubleAnimation(active?34:22,duration){EasingFunction=ease});
            _surfaceShadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty,new DoubleAnimation(active?13:7,duration){EasingFunction=ease});
            _surfaceShadow.BeginAnimation(DropShadowEffect.OpacityProperty,new DoubleAnimation(active ? .38 : .28,duration){EasingFunction=ease});
        }
        InvalidateVisual();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        if (Object.Kind != BoardObjectKind.Connector) return base.HitTestCore(parameters);
        var point = parameters.HitPoint; var points = ParsePoints(Object.Content.GetValueOrDefault("points"));
        for (var i = 0; i < points.Count - 1; i++)
            if ((point - Midpoint(points[i], points[i + 1])).Length <= 13 || DistanceToSegment(point, points[i], points[i + 1]) <= 6) return new PointHitTestResult(this, point);
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Object.Kind == BoardObjectKind.Connector)
        {
            var points = ParsePoints(Object.Content.GetValueOrDefault("points")); var point = e.GetPosition(this);
            var segment = Enumerable.Range(0, Math.Max(0, points.Count - 1)).OrderBy(i => (point - Midpoint(points[i], points[i + 1])).Length).FirstOrDefault(-1);
            if (segment >= 0 && (point - Midpoint(points[segment], points[segment + 1])).Length <= 14) { BreakRequested?.Invoke(this, segment); e.Handled = true; }
            return;
        }
        if (!IsInteractive || Object.Locked) return;
        if (IsPluginKind && IsPluginInteractiveSource(e.OriginalSource as DependencyObject)) return;
        Focus(); Selected?.Invoke(this, EventArgs.Empty);
        var local = Unrotate(e.GetPosition(this));
        if (e.ClickCount >= 2 && Object.Kind is BoardObjectKind.Text or BoardObjectKind.StickyNote && ContentBounds.Contains(local)) { EditRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        _action = HitAction(local);
        if (_action == TransformAction.None) { e.Handled = true; return; }
        _pointerStart = e.GetPosition(Parent as IInputElement ?? this); _objectStart = new Rect(Object.X, Object.Y, Object.Width, Object.Height); _rotationStart = Object.Rotation;
        _rotationCenter = new Point(LayoutLeft + SelectionPadding + Object.Width / 2, LayoutTop + RotationSpace + Object.Height / 2);
        _transformStarted=false;CaptureMouse();e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (Object.Kind == BoardObjectKind.Connector || !IsInteractive) return;
        Focus(); Selected?.Invoke(this, EventArgs.Empty); ContextActionsRequested?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || _action == TransformAction.None)
        {
            if (IsSelected && Object.Kind != BoardObjectKind.Connector) Cursor = CursorFor(HitAction(Unrotate(e.GetPosition(this))));
            return;
        }
        var current = e.GetPosition(Parent as IInputElement ?? this);
        if(!_transformStarted)
        {
            if(_action==TransformAction.Move&&(current-_pointerStart).Length<4)return;
            _transformStarted=true;SetDraggingVisual(true);DragStarted?.Invoke(this,EventArgs.Empty);
        }
        if (_action == TransformAction.Rotate)
        {
            var start = Math.Atan2(_pointerStart.Y - _rotationCenter.Y, _pointerStart.X - _rotationCenter.X); var angle = Math.Atan2(current.Y - _rotationCenter.Y, current.X - _rotationCenter.X);
            Object.Rotation = NormalizeDegrees(_rotationStart + (angle - start) * 180 / Math.PI);
        }
        else if (_action == TransformAction.Move) { Object.X = _objectStart.X + current.X - _pointerStart.X; Object.Y = _objectStart.Y + current.Y - _pointerStart.Y; }
        else ResizeFromStart(RotateVector(current - _pointerStart, -_rotationStart));
        // Translation changes neither content nor size. Rebuilding plugin surfaces
        // and invalidating their shadow on every pointer event causes UI stalls.
        if(_action!=TransformAction.Move)RefreshFromObject();
        DragMoved?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return; ReleaseMouseCapture(); _action = TransformAction.None; if(_transformStarted)DragCompleted?.Invoke(this, EventArgs.Empty);_transformStarted=false;SetDraggingVisual(false);e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (Object.Kind == BoardObjectKind.Connector) { DrawConnector(dc); return; }
        var bounds = ContentBounds; var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2); dc.PushTransform(new RotateTransform(Object.Rotation, center.X, center.Y));
        var stroke = Brush(Object.Style.GetValueOrDefault("stroke"), IsDarkMode ? "#E5E7EB" : "#334155");
        if (Object.Kind == BoardObjectKind.Stroke && Object.Style.GetValueOrDefault("mode") == "highlight" && stroke is SolidColorBrush marker) { marker = marker.Clone(); marker.Opacity = .38; stroke = marker; }
        var fill = Brush(Object.Style.GetValueOrDefault("fill"), Object.Kind == BoardObjectKind.StickyNote ? "#DCCBFF" : "#241096F3");
        var thickness = Number(Object.Style.GetValueOrDefault("thickness"), Object.Kind == BoardObjectKind.Stroke ? 4 : 2);
        var pen = new Pen(stroke, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (Object.Kind)
        {
            case BoardObjectKind.Stroke: DrawStroke(dc, pen, bounds); break;
            case BoardObjectKind.Shape: DrawShape(dc, fill, pen, bounds); break;
            case BoardObjectKind.StickyNote: DrawStickyNote(dc, fill, bounds); DrawText(dc, Object.Content.GetValueOrDefault("text", "Nova nota"), Object.Style.GetValueOrDefault("color", "#211C32"), Number(Object.Style.GetValueOrDefault("fontSize"), 24), bounds, new Thickness(16)); break;
            case BoardObjectKind.Text: DrawText(dc, Object.Content.GetValueOrDefault("text", "Texto"), Object.Style.GetValueOrDefault("color", IsDarkMode ? "#F8FAFC" : "#1F2937"), Number(Object.Style.GetValueOrDefault("fontSize"), 24), bounds, new Thickness(4)); break;
            case BoardObjectKind.Checklist:
            case BoardObjectKind.Calculator:
            case BoardObjectKind.Translator:
            case BoardObjectKind.CurrencyConverter:
            case BoardObjectKind.Plugin: break;
        }
        if(_collaboratorSelection is not null) dc.DrawRoundedRectangle(null,new Pen(_collaboratorSelection,2.2),bounds,10,10);
        if(_draggingVisual) dc.DrawRoundedRectangle(null,new Pen(_selectionColor,2.4){DashStyle=new DashStyle([8d,3d],0)},bounds,10,10);
        if (IsSelected) DrawSelection(dc, bounds); dc.Pop();
    }

    private Rect ContentBounds => new(SelectionPadding, RotationSpace, Math.Max(8, Object.Width), Math.Max(8, Object.Height));
    private void DrawSelection(DrawingContext dc, Rect bounds)
    {
        var accent = _selectionColor; var line = new Pen(accent, 1.4) { DashStyle = new DashStyle([4d, 3d], 0) }; dc.DrawRectangle(null, line, bounds);
        var points = HandlePoints(bounds); foreach (var point in points.Take(8)) dc.DrawRoundedRectangle(Brushes.White, new Pen(accent, 1.5), new Rect(point.X - HandleRadius, point.Y - HandleRadius, HandleRadius * 2, HandleRadius * 2), 2.5, 2.5);
        var rotate = points[8]; dc.DrawLine(new Pen(accent, 1.2), new Point(bounds.Left + bounds.Width / 2, bounds.Top), rotate); dc.DrawEllipse(Brush(IsDarkMode ? "#172132" : "#FFFFFF", "#172132"), new Pen(accent, 1.5), rotate, 6, 6);
    }

    private TransformAction HitAction(Point point)
    {
        if (IsSelected)
        {
            var actions = new[] { TransformAction.TopLeft, TransformAction.Top, TransformAction.TopRight, TransformAction.Right, TransformAction.BottomRight, TransformAction.Bottom, TransformAction.BottomLeft, TransformAction.Left, TransformAction.Rotate };
            var points = HandlePoints(ContentBounds); for (var i = 0; i < points.Length; i++) if ((point - points[i]).Length <= 10) return actions[i];
        }
        if (!ContentBounds.Contains(point)) return TransformAction.None;
        if (IsPluginKind) return PluginHeaderBounds.Contains(point) ? TransformAction.Move : TransformAction.None;
        return TransformAction.Move;
    }
    private static Point[] HandlePoints(Rect b) => [b.TopLeft, new Point(b.Left+b.Width/2,b.Top), b.TopRight, new Point(b.Right,b.Top+b.Height/2), b.BottomRight, new Point(b.Left+b.Width/2,b.Bottom), b.BottomLeft, new Point(b.Left,b.Top+b.Height/2), new Point(b.Left+b.Width/2,b.Top-24)];
    private void ResizeFromStart(Vector delta)
    {
        var (minWidth, minHeight) = Object.Kind switch
        {
            BoardObjectKind.Checklist => (220d, 170d), BoardObjectKind.Calculator => (215d, 285d),
            BoardObjectKind.Translator => (270d, 205d), BoardObjectKind.CurrencyConverter => (260d, 190d),
            BoardObjectKind.Plugin => (190d, 140d),
            _ => (48d, 32d)
        };
        var left = _objectStart.Left; var top = _objectStart.Top; var right = _objectStart.Right; var bottom = _objectStart.Bottom;
        if (_action is TransformAction.Left or TransformAction.TopLeft or TransformAction.BottomLeft) left = Math.Min(right - minWidth, left + delta.X);
        if (_action is TransformAction.Right or TransformAction.TopRight or TransformAction.BottomRight) right = Math.Max(left + minWidth, right + delta.X);
        if (_action is TransformAction.Top or TransformAction.TopLeft or TransformAction.TopRight) top = Math.Min(bottom - minHeight, top + delta.Y);
        if (_action is TransformAction.Bottom or TransformAction.BottomLeft or TransformAction.BottomRight) bottom = Math.Max(top + minHeight, bottom + delta.Y);
        Object.X = left; Object.Y = top; Object.Width = right - left; Object.Height = bottom - top;
    }
    private Point Unrotate(Point point) { var b = ContentBounds; var center = new Point(b.Left + b.Width / 2, b.Top + b.Height / 2); return center + RotateVector(point - center, -Object.Rotation); }
    private static Vector RotateVector(Vector vector, double degrees) { var radians = degrees * Math.PI / 180; var c = Math.Cos(radians); var s = Math.Sin(radians); return new Vector(vector.X*c-vector.Y*s,vector.X*s+vector.Y*c); }
    private static Cursor CursorFor(TransformAction action) => action switch { TransformAction.Left or TransformAction.Right => Cursors.SizeWE, TransformAction.Top or TransformAction.Bottom => Cursors.SizeNS, TransformAction.TopLeft or TransformAction.BottomRight => Cursors.SizeNWSE, TransformAction.TopRight or TransformAction.BottomLeft => Cursors.SizeNESW, TransformAction.Rotate => Cursors.Hand, TransformAction.Move => Cursors.SizeAll, _ => Cursors.Arrow };

    private void DrawStroke(DrawingContext dc, Pen pen, Rect bounds)
    {
        var points = ParsePoints(Object.Content.GetValueOrDefault("points")); if (points.Count < 2) return;
        var naturalWidth = Number(Object.Content.GetValueOrDefault("naturalWidth"), Math.Max(1, points.Max(p => p.X))); var naturalHeight = Number(Object.Content.GetValueOrDefault("naturalHeight"), Math.Max(1, points.Max(p => p.Y)));
        var scaled = points.Select(p => new Point(bounds.Left+p.X/naturalWidth*bounds.Width,bounds.Top+p.Y/naturalHeight*bounds.Height)).ToList(); var geometry = new StreamGeometry(); using (var context=geometry.Open()) { context.BeginFigure(scaled[0],false,false); context.PolyLineTo(scaled.Skip(1).ToList(),true,false); } geometry.Freeze(); dc.DrawGeometry(null,pen,geometry);
    }
    private void DrawConnector(DrawingContext dc)
    {
        var points=ParsePoints(Object.Content.GetValueOrDefault("points")); if(points.Count<2)return; var color=Brush(Object.Style.GetValueOrDefault("stroke"),"#8393AD"); var pen=new Pen(color,Number(Object.Style.GetValueOrDefault("thickness"),1.35)){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        for(var i=0;i<points.Count-1;i++){dc.DrawLine(pen,points[i],points[i+1]);var middle=Midpoint(points[i],points[i+1]);dc.DrawEllipse(Brush(IsDarkMode?"#142033":"#FFFFFF","#142033"),new Pen(color,1.2),middle,9,9);var cut=new Pen(Brush(IsDarkMode?"#C9D4E5":"#516078","#C9D4E5"),1.25){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};dc.DrawLine(cut,middle+new Vector(-3,-3),middle+new Vector(3,3));dc.DrawLine(cut,middle+new Vector(3,-3),middle+new Vector(-3,3));}
    }
    private void DrawShape(DrawingContext dc,Brush fill,Pen pen,Rect bounds)
    {
        switch(Object.Style.GetValueOrDefault("shape","square")){case "line":var start=new Point(bounds.Left+bounds.Width*Number(Object.Style.GetValueOrDefault("lineStartX"),0),bounds.Top+bounds.Height*Number(Object.Style.GetValueOrDefault("lineStartY"),0));var end=new Point(bounds.Left+bounds.Width*Number(Object.Style.GetValueOrDefault("lineEndX"),1),bounds.Top+bounds.Height*Number(Object.Style.GetValueOrDefault("lineEndY"),1));dc.DrawLine(pen,start,end);break;case "circle":dc.DrawEllipse(fill,pen,new Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2),bounds.Width/2,bounds.Height/2);break;case "triangle":var triangle=new StreamGeometry();using(var context=triangle.Open()){context.BeginFigure(new Point(bounds.Left+bounds.Width/2,bounds.Top),true,true);context.LineTo(bounds.BottomRight,true,false);context.LineTo(bounds.BottomLeft,true,false);}triangle.Freeze();dc.DrawGeometry(fill,pen,triangle);break;default:dc.DrawRoundedRectangle(fill,pen,bounds,9,9);break;}
    }
    private static void DrawStickyNote(DrawingContext dc,Brush fill,Rect bounds){dc.DrawRoundedRectangle(fill,null,bounds,16,16);dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb(36,255,255,255),Colors.Transparent,90),null,new Rect(bounds.X+1,bounds.Y+1,Math.Max(1,bounds.Width-2),Math.Min(42,bounds.Height-2)),15,15);var fold=new StreamGeometry();using(var context=fold.Open()){context.BeginFigure(new Point(bounds.Right-25,bounds.Bottom),true,true);context.LineTo(new Point(bounds.Right,bounds.Bottom-25),true,false);context.LineTo(bounds.BottomRight,true,false);}fold.Freeze();dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(34,33,28,50)),null,fold);}
    private void DrawText(DrawingContext dc,string text,string color,double size,Rect bounds,Thickness padding){var formatted=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Variable Text"),size,Brush(color,"#F8FAFC"),VisualTreeHelper.GetDpi(this).PixelsPerDip){MaxTextWidth=Math.Max(1,bounds.Width-padding.Left-padding.Right),MaxTextHeight=Math.Max(1,bounds.Height-padding.Top-padding.Bottom),Trimming=TextTrimming.WordEllipsis,TextAlignment=Object.Style.GetValueOrDefault("align",Object.Kind==BoardObjectKind.StickyNote?"center":"left") switch{"center"=>TextAlignment.Center,"right"=>TextAlignment.Right,_=>TextAlignment.Left}};dc.DrawText(formatted,new Point(bounds.Left+padding.Left,bounds.Top+padding.Top));}

    private void DrawWidgetShell(DrawingContext dc, Rect bounds, string title, string glyph, string accent = "#A78BFA")
    {
        var surface = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(IsDarkMode ? "#F21C2637" : "#FAFFFFFF"), (Color)ColorConverter.ConvertFromString(IsDarkMode ? "#EA121B29" : "#F4F7FB"), 105);
        dc.DrawRoundedRectangle(surface, new Pen(Brush(IsDarkMode ? "#536A88" : "#CBD5E1", "#536A88"), 1.1), bounds, 18, 18);
        dc.DrawRoundedRectangle(Brush("#15FFFFFF", "#15FFFFFF"), null, new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(1, bounds.Width - 2), 48), 17, 17);
        DrawLabel(dc, glyph, new Point(bounds.Left + 16, bounds.Top + 10), 22, accent, "Segoe UI Symbol", FontWeights.SemiBold);
        DrawLabel(dc, title, new Point(bounds.Left + 49, bounds.Top + 14), 15, IsDarkMode ? "#F5F7FF" : "#223047", "Segoe UI Variable Display", FontWeights.SemiBold);
        dc.DrawLine(new Pen(Brush(IsDarkMode ? "#32465F" : "#DFE6EF", "#32465F"), 1), new Point(bounds.Left + 14, bounds.Top + 48), new Point(bounds.Right - 14, bounds.Top + 48));
    }

    private void DrawChecklist(DrawingContext dc, Rect bounds)
    {
        DrawWidgetShell(dc, bounds, Object.Content.GetValueOrDefault("title", "Checklist"), "✓", "#78D7FF");
        var items = ChecklistItems(); var checkedItems = CheckedChecklistItems();
        var top = bounds.Top + 61;
        for (var index = 0; index < items.Count && top + 30 <= bounds.Bottom - 10; index++, top += 34)
        {
            var box = new Rect(bounds.Left + 16, top + 3, 20, 20);
            dc.DrawRoundedRectangle(checkedItems.Contains(index) ? Brush("#8B5CF6", "#8B5CF6") : null, new Pen(Brush(checkedItems.Contains(index) ? "#BDA9FF" : "#8292AA", "#8292AA"), 1.4), box, 5, 5);
            if (checkedItems.Contains(index)) DrawLabel(dc, "✓", new Point(box.Left + 3, box.Top - 2), 18, "#FFFFFF", "Segoe UI", FontWeights.Bold);
            DrawLabel(dc, items[index], new Point(bounds.Left + 47, top + 3), 14, checkedItems.Contains(index) ? "#7E8DA3" : (IsDarkMode ? "#E8EDF7" : "#2A374B"), "Segoe UI Variable Text", FontWeights.Normal, Math.Max(20, bounds.Width - 65), checkedItems.Contains(index));
        }
        if (items.Count == 0) DrawLabel(dc, "Duplo clique para adicionar itens", new Point(bounds.Left + 17, top + 4), 13, "#8797AD");
    }

    private static readonly string[][] CalculatorKeys =
    [
        ["C", "±", "%", "÷"], ["7", "8", "9", "×"], ["4", "5", "6", "-"], ["1", "2", "3", "+"], ["0", ",", "back", "="]
    ];

    private void DrawCalculator(DrawingContext dc, Rect bounds)
    {
        DrawWidgetShell(dc, bounds, "Calculadora", "∑", "#B998FF");
        var display = Object.Content.GetValueOrDefault("display", "0");
        var displayRect = new Rect(bounds.Left + 14, bounds.Top + 60, Math.Max(60, bounds.Width - 28), 54);
        dc.DrawRoundedRectangle(Brush(IsDarkMode ? "#A70B1320" : "#EEF2F7", "#0B1320"), new Pen(Brush(IsDarkMode ? "#344962" : "#D5DEE9", "#344962"), 1), displayRect, 11, 11);
        DrawLabel(dc, display, new Point(displayRect.Left + 8, displayRect.Top + 10), 24, IsDarkMode ? "#FAFBFF" : "#172033", "Segoe UI Variable Display", FontWeights.SemiBold, displayRect.Width - 16, false, TextAlignment.Right);
        var grid = CalculatorGrid(bounds);
        for (var row = 0; row < CalculatorKeys.Length; row++)
        for (var column = 0; column < CalculatorKeys[row].Length; column++)
        {
            var key = CalculatorKeys[row][column]; var cell = CalculatorCell(grid, row, column);
            var accent = column == 3 || key == "=";
            dc.DrawRoundedRectangle(Brush(accent ? "#5E4BC5" : (IsDarkMode ? "#26344A" : "#E7EDF5"), "#26344A"), null, cell, 10, 10);
            DrawLabel(dc, key == "back" ? "⌫" : key, new Point(cell.Left, cell.Top + 7), key == "back" ? 18 : 18, accent ? "#FFFFFF" : (IsDarkMode ? "#EAF0FA" : "#263449"), key == "back" ? "Segoe UI Symbol" : "Segoe UI Variable Display", FontWeights.Medium, cell.Width, false, TextAlignment.Center);
        }
    }

    private void DrawTranslator(DrawingContext dc, Rect bounds)
    {
        DrawWidgetShell(dc, bounds, "Tradutor", "A", "#72D5FF");
        var source = Object.Content.GetValueOrDefault("sourceLanguage", "pt-BR"); var target = Object.Content.GetValueOrDefault("targetLanguage", "en");
        DrawLanguagePill(dc, new Rect(bounds.Left + 15, bounds.Top + 61, 83, 30), source);
        DrawLabel(dc, "↔", new Point(bounds.Left + bounds.Width / 2 - 10, bounds.Top + 63), 20, "#91A4BC", "Segoe UI Symbol");
        DrawLanguagePill(dc, new Rect(bounds.Right - 98, bounds.Top + 61, 83, 30), target);
        DrawLabel(dc, Object.Content.GetValueOrDefault("input", "Digite um texto"), new Point(bounds.Left + 17, bounds.Top + 105), 14, IsDarkMode ? "#E9EEF8" : "#27354A", maxWidth: bounds.Width - 34);
        dc.DrawLine(new Pen(Brush(IsDarkMode ? "#32465F" : "#DDE5EE", "#32465F"), 1), new Point(bounds.Left + 16, bounds.Top + 151), new Point(bounds.Right - 16, bounds.Top + 151));
        DrawLabel(dc, Object.Content.GetValueOrDefault("output", "Duplo clique para traduzir"), new Point(bounds.Left + 17, bounds.Top + 165), 14, "#BCA9FF", maxWidth: bounds.Width - 34);
        DrawActionHint(dc, bounds, "Abrir tradutor");
    }

    private void DrawCurrencyConverter(DrawingContext dc, Rect bounds)
    {
        DrawWidgetShell(dc, bounds, "Conversor de moeda", "$", "#62DDB0");
        var source = Object.Content.GetValueOrDefault("sourceCurrency", "BRL"); var target = Object.Content.GetValueOrDefault("targetCurrency", "USD");
        DrawLanguagePill(dc, new Rect(bounds.Left + 15, bounds.Top + 63, 78, 30), source);
        DrawLabel(dc, "↔", new Point(bounds.Left + bounds.Width / 2 - 10, bounds.Top + 65), 20, "#91A4BC", "Segoe UI Symbol");
        DrawLanguagePill(dc, new Rect(bounds.Right - 93, bounds.Top + 63, 78, 30), target);
        var amount = Object.Content.GetValueOrDefault("amount", "1");
        DrawLabel(dc, amount, new Point(bounds.Left + 17, bounds.Top + 111), 22, IsDarkMode ? "#F5F7FC" : "#233047", "Segoe UI Variable Display", FontWeights.SemiBold, bounds.Width - 34);
        var result = Object.Content.GetValueOrDefault("result", "Duplo clique para converter");
        DrawLabel(dc, result, new Point(bounds.Left + 17, bounds.Top + 155), 16, "#7EE2BD", "Segoe UI Variable Display", FontWeights.SemiBold, bounds.Width - 34);
        DrawActionHint(dc, bounds, "Abrir conversor");
    }

    private void DrawLanguagePill(DrawingContext dc, Rect rect, string value)
    {
        dc.DrawRoundedRectangle(Brush(IsDarkMode ? "#293750" : "#E6EDF6", "#293750"), null, rect, 9, 9);
        DrawLabel(dc, value, new Point(rect.Left, rect.Top + 6), 12, IsDarkMode ? "#E8EDF7" : "#263449", "Segoe UI Variable Text", FontWeights.SemiBold, rect.Width, false, TextAlignment.Center);
    }

    private void DrawActionHint(DrawingContext dc, Rect bounds, string text)
    {
        if (bounds.Height < 215) return;
        var rect = new Rect(bounds.Left + 15, bounds.Bottom - 42, bounds.Width - 30, 28);
        dc.DrawRoundedRectangle(Brush("#2E7C5CF6", "#2E7C5CF6"), null, rect, 9, 9);
        DrawLabel(dc, text, new Point(rect.Left, rect.Top + 5), 11, "#CBBEFF", "Segoe UI Variable Text", FontWeights.SemiBold, rect.Width, false, TextAlignment.Center);
    }

    private static Rect CalculatorGrid(Rect bounds) => new(bounds.Left + 14, bounds.Top + 125, Math.Max(80, bounds.Width - 28), Math.Max(100, bounds.Height - 139));
    private static Rect CalculatorCell(Rect grid, int row, int column)
    {
        const double gap = 7; var width = (grid.Width - gap * 3) / 4; var height = (grid.Height - gap * 4) / 5;
        return new Rect(grid.Left + column * (width + gap), grid.Top + row * (height + gap), width, height);
    }

    private bool TryHandleWidgetClick(Point point)
    {
        if (Object.Kind == BoardObjectKind.Calculator)
        {
            var grid = CalculatorGrid(ContentBounds);
            for (var row = 0; row < CalculatorKeys.Length; row++)
            for (var column = 0; column < CalculatorKeys[row].Length; column++)
                if (CalculatorCell(grid, row, column).Contains(point)) { WidgetActionRequested?.Invoke(this, $"calculator:{CalculatorKeys[row][column]}"); return true; }
        }
        if (Object.Kind == BoardObjectKind.Checklist)
        {
            var index = (int)Math.Floor((point.Y - (ContentBounds.Top + 61)) / 34);
            if (index >= 0 && index < ChecklistItems().Count) { WidgetActionRequested?.Invoke(this, $"checklist:{index}"); return true; }
        }
        return false;
    }

    private List<string> ChecklistItems() => Object.Content.GetValueOrDefault("items", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    private HashSet<int> CheckedChecklistItems() => Object.Content.GetValueOrDefault("checked", "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value => int.TryParse(value, out var index) ? index : -1).Where(index => index >= 0).ToHashSet();

    private void DrawLabel(DrawingContext dc, string text, Point point, double size, string color, string family = "Segoe UI Variable Text", FontWeight? weight = null, double maxWidth = 1000, bool strike = false, TextAlignment alignment = TextAlignment.Left)
    {
        var fontFamily = family == "Material Symbols Rounded"
            ? new FontFamily("/ClipDesk;component/Resources/Fonts/#Material Symbols Rounded")
            : new FontFamily(family);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(fontFamily, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, Brush(color, "#F8FAFC"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth), MaxTextHeight = Math.Max(20, size * 2.6), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = alignment
        };
        dc.DrawText(formatted, point);
        if (strike) dc.DrawLine(new Pen(Brush(color, "#7E8DA3"), 1.2), new Point(point.X, point.Y + size * .62), new Point(point.X + Math.Min(maxWidth, formatted.Width), point.Y + size * .62));
    }

    public static List<Point> ParsePoints(string? source){if(string.IsNullOrWhiteSpace(source))return[];var result=new List<Point>();foreach(var pair in source.Split(';',StringSplitOptions.RemoveEmptyEntries)){var parts=pair.Split(',');if(parts.Length==2&&double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var x)&&double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var y))result.Add(new Point(x,y));}return result;}
    private static Point Midpoint(Point a,Point b)=>new((a.X+b.X)/2,(a.Y+b.Y)/2);
    private static double DistanceToSegment(Point p,Point a,Point b){var ab=b-a;if(ab.LengthSquared<.001)return(p-a).Length;var t=Math.Clamp(Vector.Multiply(p-a,ab)/ab.LengthSquared,0,1);return(p-(a+ab*t)).Length;}
    private static double NormalizeDegrees(double value){value%=360;return value<0?value+360:value;}
    private static double Number(string? value,double fallback)=>double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)?number:fallback;
    private static Brush Brush(string? value,string fallback){try{return new SolidColorBrush((Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(value)?fallback:value));}catch{return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));}}
}
