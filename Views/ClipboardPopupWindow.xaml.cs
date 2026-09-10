using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace ClipDesk.Views;

public partial class ClipboardPopupWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    public ClipboardPopupWindow()
    {
        InitializeComponent();
        History.CloseRequested += (_, _) => Hide();
        Deactivated += (_, _) => { if (!History.IsDragging) Hide(); };
        Closed += (_, _) => History.Detach();
    }

    public void ShowAtCursor()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, screen.WorkingArea.Left + 16, screen.WorkingArea.Top + 16, 0, 0, 0x0015);
        var source = HwndSource.FromHwnd(handle);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        Width = Math.Min(380, bottomRight.X - topLeft.X - 24);
        Height = Math.Min(540, bottomRight.Y - topLeft.Y - 24);
        var cursor = transform.Transform(new Point(Forms.Cursor.Position.X, Forms.Cursor.Position.Y));
        // The preview starts at the click itself and only shifts when that would leave its monitor.
        Left = Math.Min(Math.Max(topLeft.X, cursor.X), bottomRight.X - Width - 8);
        Top = Math.Min(Math.Max(topLeft.Y, cursor.Y), bottomRight.Y - Height - 8);
        Show();
        Activate();
        History.FocusSearch();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
    }
}
