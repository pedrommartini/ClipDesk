using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipDesk.Models;
using ClipDesk.Services;
using ClipDesk.Views;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--presence-protocol")) { PresenceProtocolChecks.Run(); return; }
        Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT",Path.Combine(Path.GetTempPath(),"ClipDesk-Visual-Checks",Guid.NewGuid().ToString("N")));
        var app = new Application();
        if(args.Contains("--collaborator-plugins")) { PluginCollaboratorChecks.Run(); return; }
        if(args.Contains("--plugin-store")) { PluginStoreVisualChecks.Run(app); return; }
        if(args.Contains("--plugin-delivery")) { PluginDeliveryChecks.Run(app); return; }
        if(args.Contains("--plugin-board-files")) { PluginBoardFileChecks.Run(); return; }
        if(args.Contains("--plugin-v2")) { PluginV2Checks.Run(); return; }
        PresenceProtocolChecks.Run();
        var storage = new StorageService(); // No user data is loaded or saved.
        var canvas = new Canvas { Width = 1100, Height = 780, Background = new SolidColorBrush(Color.FromRgb(16,21,32)) };
        ItemCard Card(ClipboardItem item, double x, double y)
        {
            var card = new ItemCard(item, new FileIconService(), storage);
            card.ApplyTheme(true);
            canvas.Children.Add(card); Canvas.SetLeft(card,x); Canvas.SetTop(card,y);
            return card;
        }
        List<ClipboardItem> Children() => [
            new() { Type = ClipboardItemType.Image },
            new() { Type = ClipboardItemType.File, FilePaths = ["example.mp4"] },
            new() { Type = ClipboardItemType.File, FilePaths = ["example.mp3"] },
            new() { Type = ClipboardItemType.File, FilePaths = ["example.zip"] }];
        var folder = Card(new() { Type = ClipboardItemType.AppFolder, DisplayName = "Assets do projeto", Width=550,Height=325, Children=Children() },10,10);
        var small = Card(new() { Type = ClipboardItemType.AppFolder, DisplayName = "Pasta compacta", Width=260,Height=220, Children=Children() },580,10);
        var unsupported = Card(new() { Type=ClipboardItemType.File, DisplayName="153.psd", Width=340,Height=170, FilePaths=["153.psd"] },10,655);
        Card(new() { Type=ClipboardItemType.Text, DisplayName="Roteiro do vídeo", Width=490,Height=285, Text="•  Abertura impactante (0–5s)\n•  Apresentar o problema\n•  Mostrar a solução com o produto\n•  Depoimento do cliente\n•  Encerramento + CTA" },10,355);
        Card(new() { Type=ClipboardItemType.Link, DisplayName="Briefing do cliente", Width=470,Height=175, Url="docs.google.com/briefing" },570,330);
        void Layout()
        {
            canvas.Measure(new Size(1100,780)); canvas.Arrange(new Rect(0,0,1100,780)); canvas.UpdateLayout();
            app.Dispatcher.Invoke(() => { },DispatcherPriority.ContextIdle); canvas.UpdateLayout();
        }
        Layout();
        var anchoredLeft = Canvas.GetLeft(unsupported);
        var anchoredTop = Canvas.GetTop(unsupported);
        unsupported.SetWorkspaceZoom(.75, animate:false);
        Layout();
        var fullBounds = unsupported.GetVisualBounds(canvas);
        unsupported.SetWorkspaceZoom(.375, animate:false);
        Layout();
        var halfBounds = unsupported.GetVisualBounds(canvas);
        if (Canvas.GetLeft(unsupported) != anchoredLeft || Canvas.GetTop(unsupported) != anchoredTop)
            throw new Exception("Zoom changed the card coordinates.");
        if (Math.Abs(halfBounds.Left-fullBounds.Left) > .1 || Math.Abs(halfBounds.Top-fullBounds.Top) > .1)
            throw new Exception("Zoom changed the card anchor.");
        if (Math.Abs(halfBounds.Width/fullBounds.Width-.5) > .02 || Math.Abs(halfBounds.Height/fullBounds.Height-.5) > .02)
            throw new Exception("Per-card zoom scale is not proportional.");
        unsupported.SetWorkspaceZoom(.75, animate:false);
        Console.WriteLine("PASS: zoom preserves each card position and anchor.");
        foreach (var card in new[] { folder,small })
        {
            var surface=(Border)card.FindName("PreviewSurface");
            var panel=(WrapPanel)card.FindName("FolderPreview");
            foreach (FrameworkElement child in panel.Children)
            {
                var p=child.TranslatePoint(new Point(),surface);
                if(p.X < 0 || p.Y < 0 || p.X+child.ActualWidth > surface.ActualWidth+1 || p.Y+child.ActualHeight > surface.ActualHeight+1)
                    throw new Exception("Category falls outside folder preview.");
            }
        }
        if (((FrameworkElement)unsupported.FindName("HeaderLayout")).Visibility != Visibility.Collapsed)
            throw new Exception("Unsupported file repeats its title in the header.");
        Console.WriteLine("PASS: unsupported files use a single compact title.");
        // Solid fixture reveals clipped corners clearly at both aspect ratios.
        var source = BitmapSource.Create(120,80,96,96,PixelFormats.Bgra32,null,
            Enumerable.Range(0,120*80).SelectMany(_=>new byte[]{210,100,250,255}).ToArray(),120*4);
        var image = new PreviewImage { Source=source, Width=350,Height=230 };
        canvas.Children.Add(image); Canvas.SetLeft(image,590); Canvas.SetTop(image,520);
        Layout();
        var bitmap=new RenderTargetBitmap(1100,780,96,96,PixelFormats.Pbgra32); bitmap.Render(canvas);
        var path=Path.Combine(AppContext.BaseDirectory,"cards-preview.png");
        using(var file=File.Create(path)) { var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(file); }
        Console.WriteLine("PASS: folder categories stay within bounds at normal and minimum sizes.");
        foreach (var size in new[] { new Size(240,180), new Size(180,240), new Size(120,80) })
        {
            var rounded = new PreviewImage { Source=source,Width=size.Width,Height=size.Height };
            rounded.Measure(size); rounded.Arrange(new Rect(size)); rounded.UpdateLayout();
            var render=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);
            render.Render(rounded);
            var pixels=new byte[(int)size.Width*(int)size.Height*4];
            render.CopyPixels(pixels,(int)size.Width*4,0);
            var ratio=Math.Min(size.Width/120,size.Height/80);
            var left=(int)((size.Width-120*ratio)/2);
            var top=(int)((size.Height-80*ratio)/2);
            if(pixels[(top*(int)size.Width+left)*4+3] > 20) throw new Exception("Image corner is not clipped.");
            if(pixels[((int)(size.Height/2)*(int)size.Width+(int)(size.Width/2))*4+3] !=255) throw new Exception("Image center missing.");
        }
        Console.WriteLine("PASS: image corners are transparent and center is intact in landscape, portrait and native-size slots.");
        CloudVisualChecks.Run(app);
        HistoryPerformanceChecks.Run(app);
        InstallerVisualChecks.Run();
        if (args.FirstOrDefault() is { Length: > 0 } pdfPath)
        {
            var pdf = new PdfPreviewService().RenderFirstPageAsync(pdfPath).GetAwaiter().GetResult();
            if (pdf is null || pdf.PixelWidth < 100 || pdf.PixelHeight < 100)
                throw new Exception("PDF first page was not rendered.");
            Console.WriteLine($"PASS: PDF first page rendered at {pdf.PixelWidth}x{pdf.PixelHeight}.");
        }
        Console.WriteLine(path);
    }
}
