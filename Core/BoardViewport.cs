namespace ClipDesk.Core;

/// <summary>Shared board dimensions and local camera math, expressed in device-independent board units.</summary>
public static class BoardSpace
{
    public const double DefaultWidth = 12_000;
    public const double DefaultHeight = 8_000;
    public const int SchemaVersion = 1;
}

public sealed class BoardViewportState
{
    public double Zoom { get; set; } = 1;
    public double PanX { get; set; }
    public double PanY { get; set; }
}

public readonly record struct BoardPoint(double X, double Y);

/// <summary>
/// Converts between a persistent board coordinate system and one user's local screen.
/// The viewport state is deliberately never part of workspace synchronization.
/// </summary>
public sealed class BoardViewport
{
    public const double MinimumZoom = .10;
    public const double MaximumZoom = 3;

    public double WorldWidth { get; }
    public double WorldHeight { get; }
    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }
    public double Zoom { get; private set; }
    public double PanX { get; private set; }
    public double PanY { get; private set; }

    public BoardViewport(double worldWidth, double worldHeight, double viewportWidth, double viewportHeight, BoardViewportState? state = null)
    {
        WorldWidth = NormalizeDimension(worldWidth, BoardSpace.DefaultWidth);
        WorldHeight = NormalizeDimension(worldHeight, BoardSpace.DefaultHeight);
        ViewportWidth = Math.Max(0, viewportWidth);
        ViewportHeight = Math.Max(0, viewportHeight);
        Zoom = ClampZoom(state?.Zoom ?? 1);
        PanX = state?.PanX ?? 0;
        PanY = state?.PanY ?? 0;
        ClampPan();
    }

    public BoardViewportState Snapshot() => new() { Zoom = Zoom, PanX = PanX, PanY = PanY };

    public void Resize(double width, double height)
    {
        ViewportWidth = Math.Max(0, width);
        ViewportHeight = Math.Max(0, height);
        ClampPan();
    }

    public BoardPoint ScreenToWorld(BoardPoint point) => new((point.X - PanX) / Zoom, (point.Y - PanY) / Zoom);
    public BoardPoint WorldToScreen(BoardPoint point) => new(point.X * Zoom + PanX, point.Y * Zoom + PanY);

    public void PanBy(double screenDeltaX, double screenDeltaY)
    {
        PanX += screenDeltaX;
        PanY += screenDeltaY;
        ClampPan();
    }

    public void SetZoomAt(double zoom, BoardPoint screenAnchor)
    {
        var worldAnchor = ScreenToWorld(screenAnchor);
        Zoom = ClampZoom(zoom);
        PanX = screenAnchor.X - worldAnchor.X * Zoom;
        PanY = screenAnchor.Y - worldAnchor.Y * Zoom;
        ClampPan();
    }

    public void Fit()
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0) return;
        Zoom = ClampZoom(Math.Min(ViewportWidth / WorldWidth, ViewportHeight / WorldHeight));
        PanX = (ViewportWidth - WorldWidth * Zoom) / 2;
        PanY = (ViewportHeight - WorldHeight * Zoom) / 2;
    }

    public BoardPoint ClampWorldPoint(BoardPoint point, double objectWidth = 0, double objectHeight = 0)
    {
        return new BoardPoint(
            Math.Clamp(point.X, 0, Math.Max(0, WorldWidth - Math.Max(0, objectWidth))),
            Math.Clamp(point.Y, 0, Math.Max(0, WorldHeight - Math.Max(0, objectHeight))));
    }

    private void ClampPan()
    {
        PanX = ClampAxis(PanX, ViewportWidth, WorldWidth * Zoom);
        PanY = ClampAxis(PanY, ViewportHeight, WorldHeight * Zoom);
    }

    private static double ClampAxis(double pan, double viewport, double scaledWorld)
    {
        if (scaledWorld <= viewport) return (viewport - scaledWorld) / 2;
        return Math.Clamp(pan, viewport - scaledWorld, 0);
    }

    private static double ClampZoom(double zoom) => Math.Clamp(double.IsFinite(zoom) ? zoom : 1, MinimumZoom, MaximumZoom);
    private static double NormalizeDimension(double value, double fallback) => double.IsFinite(value) && value >= 1 ? value : fallback;
}
