using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipDesk.Core;
using ClipDesk.Models;

namespace ClipDesk.Views;

public sealed partial class ItemCard
{
    public event EventHandler? CloudFileActionRequested;

    public string? ExistingLocalPath()
    {
        if(Item.Type is ClipboardItemType.Text or ClipboardItemType.Link or ClipboardItemType.AppFolder) return null;
        var candidates=Item.FilePaths
            .Append(Item.StoredFilePath)
            .Concat(Item.Attachments.Select(attachment=>attachment.LocalPath));
        return candidates.FirstOrDefault(PathExists);
    }

    public void SetCloudFileActionBusy(bool busy)
    {
        CloudFileAction.IsEnabled=!busy;
        if(!busy) return;
        CloudFileAction.ToolTip="Baixando arquivo...";
        System.Windows.Automation.AutomationProperties.SetName(CloudFileAction,(string)CloudFileAction.ToolTip);
    }

    public void SetCloudPresentation(string? userId,bool cloudEnabled)
    {
        var pending=cloudEnabled && Item.Attachments.Any(a=>!a.Uploaded);
        // Keep the dimming below the entrance animation, which animates the UserControl opacity.
        Root.Opacity=pending?0.52:1;

        // A large pending attachment still needs its explicit Drive upload action. Once uploaded,
        // the button is determined only by whether this computer really has a usable local copy.
        var pendingDrive=cloudEnabled?Item.Attachments.FirstOrDefault(a=>a.IsDrive && !a.Uploaded):null;
        if(pendingDrive is not null)
        {
            var canUpload=pendingDrive.OwnerId==userId && PathExists(pendingDrive.LocalPath);
            ShowCloudFileAction("\uE2C3",canUpload,canUpload?"Enviar ao Drive":"Aguardando upload no computador de origem");
            return;
        }

        var localPath=ExistingLocalPath();
        if(localPath is not null)
        {
            ShowCloudFileAction("\uE2C8",true,Directory.Exists(localPath)?"Abrir pasta":"Mostrar na pasta");
            return;
        }

        var availableInCloud=cloudEnabled && Item.Attachments.Any(a=>a.Uploaded);
        if(availableInCloud)
        {
            ShowCloudFileAction("\uE2C0",true,"Baixar arquivo");
            return;
        }

        CloudFileAction.Visibility=Visibility.Collapsed;
        CloudSavedGlyph.Visibility=cloudEnabled && Item.Attachments.Count>0 && Item.Attachments.All(a=>a.Uploaded)
            ?Visibility.Visible:Visibility.Collapsed;
    }

    private void ShowCloudFileAction(string glyph,bool enabled,string tooltip)
    {
        CloudFileAction.Visibility=Visibility.Visible;
        CloudSavedGlyph.Visibility=Visibility.Collapsed;
        CloudFileGlyph.Text=glyph;
        CloudFileAction.IsEnabled=enabled;
        CloudFileAction.ToolTip=tooltip;
        System.Windows.Automation.AutomationProperties.SetName(CloudFileAction,(string)CloudFileAction.ToolTip);
    }

    private static bool PathExists(string? path)=>!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

    public void SetCollaboratorSelection(Brush? color)
    {
        CollaboratorSelection.Stroke=color;
        CollaboratorSelection.Visibility=color is null?Visibility.Collapsed:Visibility.Visible;
    }
    private void CloudFileAction_Click(object sender,RoutedEventArgs e) { e.Handled=true; CloudFileActionRequested?.Invoke(this,EventArgs.Empty); }
}
