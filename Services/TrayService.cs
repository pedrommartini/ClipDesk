using System.IO;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ClipDesk.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly DispatcherTimer _singleClickTimer;

    public TrayService(Dispatcher dispatcher, Action showHistory, Action showWorkspace, Action exit)
    {
        _image = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "Resources", "Brand", "clipdesk.ico"));
        _menu.Items.Add("Histórico do clipboard", null, (_, _) => dispatcher.BeginInvoke(showHistory));
        _menu.Items.Add("Abrir mesa de trabalho", null, (_, _) => dispatcher.BeginInvoke(showWorkspace));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Sair do ClipDesk", null, (_, _) => dispatcher.BeginInvoke(exit));
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "ClipDesk — Histórico do clipboard", ContextMenuStrip = _menu, Visible = true };
        _singleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime + 60) };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            showHistory();
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            if (e.Clicks >= 2)
            {
                _singleClickTimer.Stop();
                dispatcher.BeginInvoke(showWorkspace);
                return;
            }
            _singleClickTimer.Stop();
            _singleClickTimer.Start();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _singleClickTimer.Stop();
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
