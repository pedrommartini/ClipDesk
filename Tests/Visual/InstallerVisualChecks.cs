using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class InstallerVisualChecks
{
    public static void Run()
    {
        foreach (var uninstall in new[] {false, true})
        {
            var window = new ClipDesk.Installer.MainWindow(uninstall ? ClipDesk.Installer.InstallerEngine.DefaultInstallPath : null);
            var content = (FrameworkElement)window.Content;
            if (!uninstall)
            {
                ((UIElement)window.FindName("IntroState")).Visibility = Visibility.Collapsed;
                var existing = (UIElement)window.FindName("ExistingState");existing.Visibility = Visibility.Visible;existing.Opacity = 1;
            }
            var check = (CheckBox)window.FindName(uninstall ? "DeleteDataCheck" : "PreserveDataCheck");
            if (check.IsChecked != !uninstall) throw new Exception("Installer data option has an unsafe default");
            content.Measure(new Size(1200,720));content.Arrange(new Rect(0,0,1200,720));content.UpdateLayout();
            var bounds = check.TransformToAncestor(content).TransformBounds(new Rect(check.RenderSize));
            if (bounds.Width < 100 || bounds.Height < 20 || bounds.Bottom > 680) throw new Exception("Installer data choice is clipped");
            var bitmap = new RenderTargetBitmap(1200,720,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
            var path = Path.Combine(AppContext.BaseDirectory, uninstall ? "installer-uninstall.png" : "installer-preserve.png");
            using var file = File.Create(path);var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(file);
            // Do not show/click installation actions; no registry, application or personal data changes.
        }
        Console.WriteLine("PASS: installer preservation is checked; uninstall deletion is opt-in; both custom controls fit the layout.");
    }
}
