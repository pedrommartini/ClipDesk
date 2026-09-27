namespace ClipDesk.Core;

public readonly record struct BoardContentRect(double X, double Y, double Width, double Height);

public readonly record struct BoardOpeningView(double Zoom, double ScrollX, double ScrollY)
{
    public static bool ShowsAny(IEnumerable<BoardContentRect> content, double zoom,
        double scrollX, double scrollY, double viewportWidth, double viewportHeight)
    {
        if (!double.IsFinite(zoom) || zoom <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return false;
        return content.Any(rect => double.IsFinite(rect.X) && double.IsFinite(rect.Y)
            && double.IsFinite(rect.Width) && double.IsFinite(rect.Height)
            && (rect.X + Math.Max(rect.Width, 1)) * zoom > scrollX + 24
            && rect.X * zoom < scrollX + viewportWidth - 24
            && (rect.Y + Math.Max(rect.Height, 1)) * zoom > scrollY + 110
            && rect.Y * zoom < scrollY + viewportHeight - 24);
    }

    public static BoardOpeningView ForContent(IEnumerable<BoardContentRect> content,
        double viewportWidth, double viewportHeight, double preferredZoom = 1)
    {
        var rectangles = content.Where(rect => double.IsFinite(rect.X) && double.IsFinite(rect.Y)
            && double.IsFinite(rect.Width) && double.IsFinite(rect.Height)
            && rect.Width >= 0 && rect.Height >= 0).ToArray();
        var zoom = Math.Clamp(double.IsFinite(preferredZoom) ? preferredZoom : 1,
            BoardViewport.MinimumZoom, 1);
        if (rectangles.Length == 0 || viewportWidth <= 0 || viewportHeight <= 0)
            return new BoardOpeningView(zoom, 0, 0);

        var left = rectangles.Min(rect => rect.X);
        var top = rectangles.Min(rect => rect.Y);
        var right = rectangles.Max(rect => rect.X + Math.Max(rect.Width, 1));
        var bottom = rectangles.Max(rect => rect.Y + Math.Max(rect.Height, 1));
        const double sidePadding = 80;
        const double topPadding = 130;
        const double bottomPadding = 80;
        zoom = Math.Clamp(Math.Min(1, Math.Min(
            (viewportWidth - sidePadding * 2) / Math.Max(1, right - left),
            (viewportHeight - topPadding - bottomPadding) / Math.Max(1, bottom - top))),
            BoardViewport.MinimumZoom, 1);
        return new BoardOpeningView(zoom,
            Math.Max(0, left * zoom - sidePadding),
            Math.Max(0, top * zoom - topPadding));
    }
}
