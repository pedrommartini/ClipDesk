using System.Windows.Controls;
using ClipDesk.Core;
using ClipDesk.Models;
using ClipDesk.Views;

namespace ClipDesk;

public partial class MainWindow
{
    private static bool BoardObjectVisualChanged(BoardObject local,BoardObject incoming) =>
        local.Kind!=incoming.Kind || local.Width!=incoming.Width || local.Height!=incoming.Height
        || local.Rotation!=incoming.Rotation || local.Locked!=incoming.Locked
        || CloudRules.Serialize(local.Style)!=CloudRules.Serialize(incoming.Style)
        || CloudRules.Serialize(local.Content)!=CloudRules.Serialize(incoming.Content);

    // Keep model/view identities for the open board. A remote move should never
    // discard cursors, focus, animation clocks or all the other cards' previews.
    private void ApplySharedBoardSnapshot(WorkspaceBoard incoming)
    {
        var cards=WorkspaceCanvas.Children.OfType<ItemCard>().ToDictionary(v=>v.Item.Id);
        var objects=WorkspaceCanvas.Children.OfType<BoardObjectView>().ToDictionary(v=>v.Object.Id);
        var itemIds=incoming.Items.Select(i=>i.Id).ToHashSet();
        var objectIds=incoming.Objects.Select(i=>i.Id).ToHashSet();
        foreach(var card in cards.Values.Where(v=>!itemIds.Contains(v.Item.Id)))
        {
            _selectedCards.Remove(card);WorkspaceCanvas.Children.Remove(card);
        }
        foreach(var view in objects.Values.Where(v=>!objectIds.Contains(v.Object.Id)))
        {
            if(_selectedBoardObjectView==view)ClearBoardObjectSelection();
            WorkspaceCanvas.Children.Remove(view);
        }
        var mergedItems=new List<ClipboardItem>();
        foreach(var item in incoming.Items)
        {
            if(!cards.TryGetValue(item.Id,out var card))
            {
                mergedItems.Add(item);AddCard(item,playPopIn:true);continue;
            }
            var local=card.Item;
            var contentChanged=local.Type!=item.Type || local.DisplayName!=item.DisplayName
                || local.Text!=item.Text || local.Url!=item.Url || local.StoredFilePath!=item.StoredFilePath
                || !local.FilePaths.SequenceEqual(item.FilePaths)
                || CloudRules.Serialize(local.Children)!=CloudRules.Serialize(item.Children)
                || CloudRules.Serialize(local.Attachments)!=CloudRules.Serialize(item.Attachments);
            local.Type=item.Type;local.DisplayName=item.DisplayName;local.Text=item.Text;local.Url=item.Url;
            local.StoredFilePath=item.StoredFilePath;local.FilePaths=item.FilePaths;
            local.Children=item.Children;local.Attachments=item.Attachments;local.CreatedAt=item.CreatedAt;
            local.X=item.X;local.Y=item.Y;local.Width=item.Width;local.Height=item.Height;local.ZIndex=item.ZIndex;
            if(contentChanged)card.Refresh(_storageService);
            if(item.Width>0)card.Width=item.Width;
            if(item.Height>0)card.Height=item.Height;
            Canvas.SetLeft(card,item.X);Canvas.SetTop(card,item.Y);Panel.SetZIndex(card,item.ZIndex);
            card.SetCloudPresentation(_cloud?.User?.Id,incoming.SyncMode!=WorkspaceSyncMode.Local);
            mergedItems.Add(local);
        }
        var mergedObjects=new List<BoardObject>();
        foreach(var obj in incoming.Objects)
        {
            if(!objects.TryGetValue(obj.Id,out var view))
            {
                mergedObjects.Add(obj);AddBoardObjectView(obj).PlayPopIn();continue;
            }
            var local=view.Object;
            var visualChanged=BoardObjectVisualChanged(local,obj);
            local.WorkspaceId=obj.WorkspaceId;local.SchemaVersion=obj.SchemaVersion;local.Kind=obj.Kind;
            local.X=obj.X;local.Y=obj.Y;local.Width=obj.Width;local.Height=obj.Height;local.Rotation=obj.Rotation;
            local.ZIndex=obj.ZIndex;local.Locked=obj.Locked;local.CreatedBy=obj.CreatedBy;
            local.CreatedAt=obj.CreatedAt;local.UpdatedAt=obj.UpdatedAt;local.Style=obj.Style;local.Content=obj.Content;
            if(visualChanged)view.RefreshFromObject();
            PositionBoardObjectView(view);Panel.SetZIndex(view,obj.Kind==BoardObjectKind.Connector?-2:obj.ZIndex);
            mergedObjects.Add(local);
        }
        _activeWorkspace.Items=mergedItems;_items=mergedItems;_activeWorkspace.Objects=mergedObjects;
        _activeWorkspace.Name=incoming.Name;_activeWorkspace.SyncMode=incoming.SyncMode;
        _activeWorkspace.OwnerId=incoming.OwnerId;_activeWorkspace.WorldWidth=incoming.WorldWidth;
        _activeWorkspace.WorldHeight=incoming.WorldHeight;_activeWorkspace.SchemaVersion=incoming.SchemaVersion;
        _activeWorkspace.CreatedAt=incoming.CreatedAt;_activeWorkspace.UpdatedAt=incoming.UpdatedAt;
        ReconcileDragPreviews();RefreshAllCardConnectors();RefreshRemoteSelections();
    }
}
