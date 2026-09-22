using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipDesk.Core;

namespace ClipDesk.Views;

public sealed partial class ItemCard
{
    public event EventHandler? CloudFileActionRequested;
    public void SetCloudPresentation(string? userId,bool cloudEnabled)
    {
        var pending=cloudEnabled && Item.Attachments.Any(a=>!a.Uploaded);
        // Keep the dimming below the entrance animation, which animates the UserControl opacity.
        Root.Opacity=pending?0.52:1;
        var downloaded=Item.Attachments.FirstOrDefault(a=>a.IsDrive && a.Uploaded && System.IO.File.Exists(a.LocalPath)
            && Services.StorageService.IsUserDownload(a.LocalPath));
        var drive=Item.Attachments.FirstOrDefault(a=>a.IsDrive && (!a.Uploaded || !System.IO.File.Exists(a.LocalPath)
            || a.OwnerId!=userId && !Services.StorageService.IsUserDownload(a.LocalPath)));
        CloudFileAction.Visibility=cloudEnabled && (downloaded is not null || drive is not null)?Visibility.Visible:Visibility.Collapsed;
        CloudSavedGlyph.Visibility=cloudEnabled && downloaded is null && drive is null && Item.Attachments.Count>0 && Item.Attachments.All(a=>a.Uploaded)?Visibility.Visible:Visibility.Collapsed;
        if(downloaded is not null)
        {
            CloudFileGlyph.Text="\uE2C8";
            CloudFileAction.IsEnabled=true;
            CloudFileAction.ToolTip="Mostrar arquivo na pasta";
            System.Windows.Automation.AutomationProperties.SetName(CloudFileAction,(string)CloudFileAction.ToolTip);
            return;
        }
        if(drive is null) return;
        var download=drive.Uploaded;
        CloudFileGlyph.Text=download?"\uE2C0":"\uE2C3";
        CloudFileAction.IsEnabled=download || drive.OwnerId==userId;
        CloudFileAction.ToolTip=download?"Baixar arquivo":drive.OwnerId==userId?"Enviar ao Drive":"Aguardando upload do proprietário";
        System.Windows.Automation.AutomationProperties.SetName(CloudFileAction,(string)CloudFileAction.ToolTip);
    }
    public void SetCollaboratorSelection(Brush? color)
    {
        CollaboratorSelection.BorderBrush=color;
        CollaboratorSelection.Visibility=color is null?Visibility.Collapsed:Visibility.Visible;
    }
    private void CloudFileAction_Click(object sender,RoutedEventArgs e) { e.Handled=true; CloudFileActionRequested?.Invoke(this,EventArgs.Empty); }
}
