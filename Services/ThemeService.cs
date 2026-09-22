using System.Windows;
using System.Windows.Media;

namespace ClipDesk.Services;

public static class ThemeService
{
    public static void Apply(bool dark)
    {
        Set("SurfaceBrush", dark ? "#1D2330" : "#FFFFFF");
        Set("CardBrush", dark ? "#262E3D" : "#F6F7FB");
        Set("BorderBrush", dark ? "#394357" : "#E2E5EE");
        Set("TextBrush", dark ? "#F2F4FA" : "#222536");
        Set("MutedBrush", dark ? "#A9B3C7" : "#626C82");
        Set("AccentBrush", dark ? "#B59AFF" : "#7046CB");
        Set("AccentStrongBrush", dark ? "#9B7DFF" : "#6132C7");
        Set("HoverBrush", dark ? "#36354F" : "#EEE8FC");
        Set("InvitePrimaryBrush", dark ? "#A88CFF" : "#7046CB");
        Set("InvitePrimaryTextBrush", dark ? "#211B37" : "#FFFFFF");
        Set("InviteBackdropBrush", dark ? "#8A07101F" : "#66222A3D");
    }

    private static void Set(string key, string color) =>
        Application.Current.Resources[key] = (Brush)new BrushConverter().ConvertFromString(color)!;
}
