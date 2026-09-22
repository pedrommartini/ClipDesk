using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClipDesk.Models;
using ClipDesk.Views;

internal static class HistoryPerformanceChecks
{
    public static void Run(Application app)
    {
        var entries = new ObservableCollection<ClipboardHistoryEntry>(Enumerable.Range(0, 1000).Select(i => new ClipboardHistoryEntry
        {
            Title = "Item " + i, Text = "Texto de teste " + i, Preview = "Uma entrada de teste sem dados pessoais.",
            CapturedAt = DateTime.Today.AddDays(-(i / 50)), Type = ClipboardItemType.Text
        }));
        var history = new HistoryView();
        history.Bind(entries);
        var window = new Window { Content = history, Width = 410, Height = 700, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            var list = (ListBox)history.FindName("HistoryList");
            var realized = Descendants(list).OfType<ListBoxItem>().Count();
            if (realized < 1 || realized >= 100) throw new Exception($"History virtualization failed: {realized}/1000 realized");
            Console.WriteLine($"PASS: grouped history realizes {realized} of 1000 entries, not the entire history.");
            list.ScrollIntoView(entries.Last());
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);window.UpdateLayout();
            if (Descendants(list).OfType<ListBoxItem>().Count() >= 100) throw new Exception("Scrolling disabled grouped history virtualization");
            ((TextBox)history.FindName("SearchBox")).Text = "Texto de teste 999";
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();Dispatcher.PushFrame(frame);
            if (list.Items.Count != 1) throw new Exception("Debounced history search did not filter the data");
            Console.WriteLine("PASS: scrolling keeps recycling enabled and debounced search finds an offscreen entry.");
        }
        finally { history.Detach();window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
