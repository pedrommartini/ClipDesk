using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipDesk.Views;

// Clip the fitted image itself, not the larger layout slot around it.
public sealed class PreviewImage : Image
{
    protected override void OnRender(DrawingContext dc)
    {
        if (Source is null || Source.Width <= 0 || Source.Height <= 0) return;
        var scale = Math.Min(ActualWidth / Source.Width, ActualHeight / Source.Height);
        var width = Source.Width * scale;
        var height = Source.Height * scale;
        var bounds = new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
        dc.PushClip(new RectangleGeometry(bounds, 10, 10));
        dc.DrawImage(Source, bounds);
        dc.Pop();
    }
}
