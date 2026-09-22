using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipDesk.Core;
using ClipDesk.Views;
using ClipDesk.Services;

namespace ClipDesk;

public partial class MainWindow
{
    private TimeSpan _lastCollaborationFrame;
    private sealed class DragPreview(FrameworkElement view,Transform original,TranslateTransform offset,Point target)
    {
        public FrameworkElement View = view;
        public Transform Original = original;
        public TranslateTransform Offset = offset;
        public Point Target = target;
        public long Seen = Stopwatch.GetTimestamp();
        public Point Base;
    }
    private readonly Dictionary<string,DragPreview> _dragPreviews = [];
    private bool _presenceInputDirty;
    private long _lastPresenceSample;
    private Point _lastPublishedCenter;
    private double _lastPublishedZoom;

    private void RenderCollaborationFrame(object? sender,EventArgs e)
    {
        if(e is RenderingEventArgs frame)
        {
            if(frame.RenderingTime==_lastCollaborationFrame)return;
            _lastCollaborationFrame=frame.RenderingTime;
        }
        MotionDiagnostics.Record(MotionDiagnostics.Stage.RenderFrames);
        foreach(var user in _incomingPresence.Keys)
            if(_incomingPresence.TryRemove(user,out var presence))RenderPresence(presence);
        if(_followedMemberId is not null && _presenceTravelTimer.IsEnabled)AdvancePresenceFollow();
        ReconcileDragPreviews();
        // Read the rendered viewport after routed input/scrolling has finished. This
        // also samples animated zoom, which does not necessarily raise MouseMove.
        var zoom=Math.Max(WorkspaceBoardScale.ScaleX,BoardViewport.MinimumZoom);
        var center=new Point((WorkspaceScroll.HorizontalOffset+WorkspaceScroll.ViewportWidth/2)/zoom,
            (WorkspaceScroll.VerticalOffset+WorkspaceScroll.ViewportHeight/2)/zoom);
        if(!_applyingPresenceTravel
            && (_presenceInputDirty || _followedMemberId is null
                && ((center-_lastPublishedCenter).Length>.1 || Math.Abs(zoom-_lastPublishedZoom)>.0001))
            && Stopwatch.GetElapsedTime(_lastPresenceSample).TotalMilliseconds>=12)
        {
            _presenceInputDirty=false;_lastPresenceSample=Stopwatch.GetTimestamp();
            _lastPublishedCenter=center;_lastPublishedZoom=zoom;
            QueuePresence(CurrentPresenceItemId(),Mouse.GetPosition(WorkspaceCanvas));
        }
    }

    private PresenceDrag[]? CapturePresenceDrags()
    {
        if(Mouse.LeftButton!=MouseButtonState.Pressed)return null;
        var drags=new Dictionary<string,PresenceDrag>();
        if(_groupDragLead is not null)
            foreach(var card in _groupDragPositions.Keys.Concat(_linkedCardDragOrigins.Keys).Append(_groupDragLead))
                drags[card.Item.Id]=new(card.Item.Id,Canvas.GetLeft(card),Canvas.GetTop(card));
        if(_selectedBoardObjectView is { IsMouseCaptured:true,IsMovingTransform:true } view)
            drags[view.Object.Id]=new(view.Object.Id,view.Object.X,view.Object.Y);
        if(drags.Count==0)return null;
        foreach(var obj in _linkedObjectDragOrigins.Keys)drags[obj.Id]=new(obj.Id,obj.X,obj.Y);
        foreach(var card in _linkedCardDragOrigins.Keys)drags[card.Item.Id]=new(card.Item.Id,Canvas.GetLeft(card),Canvas.GetTop(card));
        return drags.Values.Take(64).ToArray();
    }

    private void ApplyDragPreviews(CloudPresence presence)
    {
        if(presence.Drags is null)return;
        foreach(var drag in presence.Drags)
        {
            var view=WorkspaceCanvas.Children.OfType<FrameworkElement>().FirstOrDefault(v=>
                v is ItemCard c && c.Item.Id==drag.Id || v is BoardObjectView o && o.Object.Id==drag.Id);
            if(view is null || view.IsMouseCaptureWithin || _groupDragPositions.Keys.Any(c=>c==view)
                || drag.Id==_editingBoardObjectId || _pendingRealtimeBoardObjects.ContainsKey(drag.Id))continue;
            var target=view is BoardObjectView {Object.Kind: not BoardObjectKind.Connector}
                ? new Point(drag.X-BoardObjectView.SelectionPadding,drag.Y-BoardObjectView.RotationSpace) : new Point(drag.X,drag.Y);
            // Translate the rendered surface, leaving Canvas coordinates authoritative.
            // Canvas.Left/Top animations trigger layout and used to leak into Save().
            if(!_dragPreviews.TryGetValue(drag.Id,out var preview) || preview.View!=view)
            {
                if(preview is not null)ReleaseDragPreview(preview);
                var original=view.RenderTransform;var offset=new TranslateTransform();
                var transforms=new TransformGroup();transforms.Children.Add(original);transforms.Children.Add(offset);
                view.RenderTransform=transforms;
                preview=new DragPreview(view,original,offset,target){Base=new Point(Canvas.GetLeft(view),Canvas.GetTop(view))};
                _dragPreviews[drag.Id]=preview;
            }
            preview.Target=target;preview.Seen=Stopwatch.GetTimestamp();
            RebaseDragPreview(preview,new Point(Canvas.GetLeft(view),Canvas.GetTop(view)));
            SmoothRemoteCursor(preview.Offset,target.X-preview.Base.X,target.Y-preview.Base.Y);
        }
    }

    private void ReconcileDragPreviews()
    {
        foreach(var entry in _dragPreviews.ToArray())
        {
            var preview=entry.Value;var view=preview.View;var target=preview.Target;
            var left=(double)view.GetAnimationBaseValue(Canvas.LeftProperty);
            var top=(double)view.GetAnimationBaseValue(Canvas.TopProperty);
            var confirmed=Math.Abs(left-target.X)<.01 && Math.Abs(top-target.Y)<.01;
            if(view.IsMouseCaptureWithin || view.Parent!=WorkspaceCanvas || confirmed
                || Stopwatch.GetElapsedTime(preview.Seen)>TimeSpan.FromSeconds(3))
            {
                ReleaseDragPreview(preview);
                _dragPreviews.Remove(entry.Key);
            }
            else if(preview.Base!=new Point(left,top))
            {
                // A durable intermediate position arrived behind the live preview.
                // Rebase the translation without jumping back to that old position.
                RebaseDragPreview(preview,new Point(left,top));
                SmoothRemoteCursor(preview.Offset,target.X-left,target.Y-top);
            }
        }
    }

    private static void RebaseDragPreview(DragPreview preview,Point currentBase)
    {
        if(preview.Base==currentBase)return;
        var rendered=preview.Base+new Vector(preview.Offset.X,preview.Offset.Y);
        preview.Offset.BeginAnimation(TranslateTransform.XProperty,null);
        preview.Offset.BeginAnimation(TranslateTransform.YProperty,null);
        preview.Offset.X=rendered.X-currentBase.X;preview.Offset.Y=rendered.Y-currentBase.Y;
        preview.Base=currentBase;
    }

    private static void ReleaseDragPreview(DragPreview preview) => preview.View.RenderTransform=preview.Original;

    private static void SmoothRemoteCursor(TranslateTransform position,double x,double y)
    {
        // WPF interpolates on rendered frames, independent of network packet cadence.
        var fromX=position.X;var fromY=position.Y;
        if(Math.Abs(fromX-x)<.01 && Math.Abs(fromY-y)<.01)return;
        position.X=x;position.Y=y;
        position.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(fromX,x,TimeSpan.FromMilliseconds(28)));
        position.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(fromY,y,TimeSpan.FromMilliseconds(28)));
    }
}
