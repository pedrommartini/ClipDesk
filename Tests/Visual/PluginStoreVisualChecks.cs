using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipDesk.Services;
using ClipDesk.Views;

internal static class PluginStoreVisualChecks
{
    public static void Run(Application app)
    {
        ThemeService.Apply(true);
        var service = new PluginCatalogService();
        var entries = service.LoadCatalog();
        if (entries.Count != 4 || entries.Any(entry => !entry.IsInstalled))
            throw new Exception("The initial plugin catalog must contain exactly four installed plugins.");

        var view = new PluginStoreView();
        view.Bind(entries);
        Layout(view, app, 980, 720);

        var catalog = (StackPanel)view.FindName("CatalogView");
        var installed = (StackPanel)view.FindName("InstalledView");
        var summary = (TextBlock)view.FindName("InstalledSummaryTitle");
        var catalogCards = (WrapPanel)view.FindName("PluginCardsPanel");
        if (catalog.Visibility != Visibility.Visible || installed.Visibility != Visibility.Collapsed)
            throw new Exception("Plugin store does not open on the catalog summary.");
        if (summary.Text != "4 plugins instalados" || catalogCards.Children.Count != 4)
            throw new Exception("Installed summary or catalog count is incorrect.");

        ((Button)view.FindName("ShowInstalledButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(view, app, 980, 720);
        var installedCards = (WrapPanel)view.FindName("InstalledCardsPanel");
        if (catalog.Visibility != Visibility.Collapsed || installed.Visibility != Visibility.Visible || installedCards.Children.Count != 4)
            throw new Exception("Installed-only list does not open correctly.");

        ((TextBox)view.FindName("SearchBox")).Text = "calculadora";
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        if (installedCards.Children.Cast<UIElement>().Count(child => child.Visibility == Visibility.Visible) != 1)
            throw new Exception("Plugin search does not filter the installed-only list.");

        var actionRaised = false;
        var repairRaised = false;
        var uninstallRaised = false;
        view.PluginActionRequested += _ => actionRaised = true;
        view.PluginRepairRequested += _ => repairRaised = true;
        view.PluginUninstallRequested += _ => uninstallRaised = true;
        var visibleCard = installedCards.Children.Cast<FrameworkElement>().Single(child => child.Visibility == Visibility.Visible);
        Descendants<Button>(visibleCard).Single(button => Equals(button.Tag, "plugin-primary-action"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!actionRaised) throw new Exception("Clicking a plugin does not request adding it to the board.");
        var menuButton = Descendants<Button>(visibleCard).Single(button => Equals(button.Tag, "plugin-action-menu"));
        menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu = menuButton.ContextMenu ?? throw new Exception("The installed plugin dropdown did not open.");
        var menuItems = menu.Items.OfType<MenuItem>().ToArray();
        if (menuItems.Length != 2
            || !Descendants<TextBlock>(menuItems[0]).Any(text => text.Text == "Reparar")
            || !Descendants<TextBlock>(menuItems[1]).Any(text => text.Text == "Desinstalar"))
            throw new Exception("The plugin dropdown does not expose Repair and Uninstall.");
        menuItems[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menuItems[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        if (!repairRaised || !uninstallRaised)
            throw new Exception("The plugin dropdown actions were not routed to the host.");

        ((TextBox)view.FindName("SearchBox")).Text = "";
        Layout(view, app, 420, 720);
        var widestNarrowCard = installedCards.Children.Cast<FrameworkElement>().Max(card => card.ActualWidth);
        if (widestNarrowCard > 420.5)
            throw new Exception($"Plugin cards overflow the narrow layout ({widestNarrowCard:0.##} px).");
        SavePng(view, 420, 720, Path.Combine(AppContext.BaseDirectory, "plugin-store-narrow.png"));

        ((Button)view.FindName("InstalledBackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(view, app, 1280, 760);
        ((ScrollViewer)view.FindName("StoreScroller")).ScrollToTop();
        Layout(view, app, 1280, 760);
        var firstCardPosition = ((FrameworkElement)catalogCards.Children[0]).TranslatePoint(new Point(), view);
        var secondCardPosition = ((FrameworkElement)catalogCards.Children[1]).TranslatePoint(new Point(), view);
        var thirdCardPosition = ((FrameworkElement)catalogCards.Children[2]).TranslatePoint(new Point(), view);
        var fourthCardPosition = ((FrameworkElement)catalogCards.Children[3]).TranslatePoint(new Point(), view);
        var search = (FrameworkElement)view.FindName("SearchSurface");
        var searchPosition = search.TranslatePoint(new Point(), view);
        if (Math.Abs(firstCardPosition.Y - secondCardPosition.Y) > 1
            || Math.Abs(firstCardPosition.Y - thirdCardPosition.Y) > 1
            || fourthCardPosition.Y <= firstCardPosition.Y
            || Math.Abs(firstCardPosition.X - searchPosition.X) > 1
            || search.ActualWidth > 400
            || catalogCards.Children.Cast<FrameworkElement>().Any(card => card.ActualHeight > 151))
            throw new Exception("Plugin store desktop layout is not compact and consistently aligned.");
        var path = Path.Combine(AppContext.BaseDirectory, "plugin-store.png");
        SavePng(view, 1280, 760, path);
        ThemeService.Apply(false);
        var lightView = new PluginStoreView();
        lightView.Bind(entries);
        Layout(lightView, app, 1280, 760);
        SavePng(lightView, 1280, 760, Path.Combine(AppContext.BaseDirectory, "plugin-store-light.png"));
        ThemeService.Apply(true);
        var officialFeed = PluginDeliveryService.LoadBundledFeed()
            ?? throw new Exception("The application did not bundle the optional plugin feed.");
        var allEntries = PluginDeliveryService.AddRemoteEntries(officialFeed, entries, Version.Parse(UpdateService.CurrentVersion));
        view.Bind(allEntries);
        Layout(view, app, 1280, 760);
        SavePng(view, 1280, 760, Path.Combine(AppContext.BaseDirectory, "plugin-store-all.png"));
        if (catalogCards.Children.Count != entries.Count + officialFeed.Packages.Count
            || allEntries.Count(entry => entry.IsInstalled) != 4
            || !officialFeed.Packages.All(package => allEntries.Any(entry =>
                entry.Manifest.Id == package.Id && !entry.IsInstalled && entry.RemotePackage is not null)))
            throw new Exception("The store must show every published optional plugin alongside the four bundled plugins.");
        Console.WriteLine("PASS: plugin store shows a summary, a separate installed-only list, responsive cards and working actions.");
        Console.WriteLine(path);
    }

    private static void Layout(FrameworkElement element, Application app, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        element.UpdateLayout();
    }

    private static void SavePng(FrameworkElement element, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        using var output = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(output);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
