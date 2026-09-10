using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipDesk.Models;

namespace ClipDesk.Views;

public partial class FolderWindow : Window
{
    public const string FolderItemDragFormat = "ClipDesk.FolderItem";

    private readonly ClipboardItem _folder;
    private Point _dragStart;

    public FolderWindow(ClipboardItem folder)
    {
        InitializeComponent();
        _folder = folder;
        TitleText.Text = folder.DisplayName;
        ItemsList.ItemsSource = folder.Children;
        ItemsList.PreviewMouseLeftButtonDown += ItemsList_PreviewMouseLeftButtonDown;
    }

    public event EventHandler<ClipboardItem>? ItemOpenRequested;

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ItemsList);
    }

    private void ItemsList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ItemsList.SelectedItem is not ClipboardItem item)
        {
            return;
        }

        var current = e.GetPosition(ItemsList);
        if (Math.Abs(current.X - _dragStart.X) + Math.Abs(current.Y - _dragStart.Y) < 8)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(FolderItemDragFormat, new FolderDragData(_folder.Id, item.Id));
        DragDrop.DoDragDrop(ItemsList, data, DragDropEffects.Move);
        ItemsList.Items.Refresh();
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is ClipboardItem item)
        {
            ItemOpenRequested?.Invoke(this, item);
        }
    }
}
