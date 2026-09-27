using System.Net;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClipDesk.Core;
using ClipDesk.Models;
using ClipDesk.Services;

internal static class SupabaseResilienceChecks
{
    public static async Task Run(Action<bool, string> check, string root)
    {
        var database = new LocalDatabase(Path.Combine(root, "supabase-resilience.db"));
        var deniedId = Guid.NewGuid().ToString("N");
        var healthyId = Guid.NewGuid().ToString("N");
        var itemId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid().ToString();
        database.Accept(new CloudEntity(healthyId, "workspace", null, 1,
            new JsonObject { ["name"] = "Healthy", ["ownerId"] = userId }));
        database.Stage(new CloudEntity(deniedId, "workspace", null, 0,
            new JsonObject { ["name"] = "Denied", ["ownerId"] = userId }));
        database.Stage(new CloudEntity(itemId, "boardObject", healthyId, 0,
            new JsonObject { ["text"] = "keep this private", ["x"] = 10 }));
        var storage = (StorageService)RuntimeHelpers.GetUninitializedObject(typeof(StorageService));
        typeof(StorageService).GetField("<Database>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(storage, database);
        var backendType = typeof(CloudSyncService).Assembly.GetType("ClipDesk.Services.SupabaseCloudSyncService")!;
        var backend = RuntimeHelpers.GetUninitializedObject(backendType);
        void Set(string field, object value) => backendType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, value);
        var handler = new QueueHandler(itemId, healthyId, userId);
        using var rest = new SupabaseRestClient(new SupabaseConfiguration(), handler);
        rest.SetSession("fixture-session");
        Set("_storage", storage);
        Set("_rest", rest);
        Set("_inaccessible", new HashSet<string>());
        Set("_permissionRetryAt", new ConcurrentDictionary<string, DateTimeOffset>());
        Set("_user", new CloudUser(userId, "fixture", "Fixture", null));
        await (Task)backendType.GetMethod("PushAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(backend, [CancellationToken.None, false])!;
        check(handler.Paths.Count == 2 && handler.Paths[0].Contains("workspaces") && handler.Paths[1].Contains("board_entities"),
            "Supabase 403 on one workspace does not block another workspace's entity");
        check(database.Pending().Count == 1 && database.Pending()[0].EntityId == deniedId
            && database.SyncFailures().Single() is { StatusCode: 403, BaseVersion: 0, Deleted: false }
            && !JsonSerializer.Serialize(database.SyncFailures()).Contains("keep this private", StringComparison.Ordinal),
            "Denied operation remains pending with content-free failure diagnostics");

        using var paged = new SupabaseRestClient(new SupabaseConfiguration(), new PageHandler());
        paged.SetSession("fixture-session");
        var all = await paged.GetAllByIdAsync<PageRow>("board_entities?select=id", row => row.Id, CancellationToken.None);
        check(all.Count == 501 && all.Select(row => row.Id).Distinct().Count() == 501,
            "Supabase initial snapshot reads beyond the default single-page limit");

        CheckReopeningOlderPresentation(check, root, backendType, userId);
        CheckRealtimePresentationBasis(check, root, backendType, userId);
        CheckBoardObjectMerge(check);
    }

    private static void CheckBoardObjectMerge(Action<bool, string> check)
    {
        var basis = new JsonObject { ["x"] = 100, ["y"] = 100,
            ["updatedAt"] = "2026-09-26T10:00:00+00:00",
            ["content"] = new JsonObject { ["title"] = "Initial", ["accent"] = "blue" } };
        var remote = basis.DeepClone().AsObject();
        remote["content"]!["title"] = "Remote title";
        remote["updatedAt"] = "2026-09-26T10:00:02+00:00";
        var desired = basis.DeepClone().AsObject();
        desired["x"] = 160;
        desired["content"]!["accent"] = "red";
        desired["updatedAt"] = "2026-09-26T10:00:01+00:00";
        var merged = CloudRules.MergeEntity("boardObject", remote, basis, desired);
        check(merged["x"]?.GetValue<int>() == 160 && merged["content"]?["title"]?.GetValue<string>() == "Remote title"
            && merged["content"]?["accent"]?.GetValue<string>() == "red"
            && merged["updatedAt"]?.GetValue<string>() == "2026-09-26T10:00:02+00:00",
            "Independent plugin position and settings merge without timestamp conflict");
        desired["content"]!["title"] = "Different title";
        var protectedEdit = false;
        try { CloudRules.MergeEntity("boardObject", remote, basis, desired); }
        catch (SyncConflictException) { protectedEdit = true; }
        check(protectedEdit, "Concurrent edits to the same plugin setting remain conflicts");
    }

    private static void CheckRealtimePresentationBasis(Action<bool, string> check, string root, Type backendType, string userId)
    {
        var storage = new StorageService();
        var board = new WorkspaceBoard { OwnerId = userId, SyncMode = WorkspaceSyncMode.Shared };
        var obj = new BoardObject { Kind = BoardObjectKind.Checklist, X = 100, Y = 100,
            Content = new() { ["title"] = "Initial", ["checked"] = "" } };
        board.Objects.Add(obj);
        var initial = CloudProjection.ProjectBoardObject(obj, board.Id.ToString("N")) with { Version = 1 };
        storage.Database.Accept(initial);
        var backend = RuntimeHelpers.GetUninitializedObject(backendType);
        void Set(string field, object value) => backendType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, value);
        Set("_storage", storage);
        Set("_user", new CloudUser(userId, "fixture", "Fixture", null));
        Set("_inaccessible", new HashSet<string>());
        Set("_presented", new Dictionary<string, CloudEntity> { [initial.Id] = initial });
        var remote = initial with { Version = 2, Data = initial.Data.DeepClone().AsObject() };
        remote.Data["content"]!["title"] = "Remote title";
        storage.Database.Accept(remote);
        backendType.GetMethod("ObserveRealtimeEntity")!.Invoke(backend, [remote]);
        obj.Content["title"] = "Remote title";
        obj.X = 160;
        backendType.GetMethod("StageBoardObject")!.Invoke(backend, [board, obj]);
        var pending = storage.Database.Pending(initial.Id).SingleOrDefault();
        check(pending is not null && pending.Data["content"]?["title"]?.GetValue<string>() == "Remote title"
            && pending.Data["x"]?.GetValue<double>() == 160 && storage.Database.ConflictCount == 0,
            "Moving a realtime-updated plugin uses its visible state as the edit basis");

        var acknowledged = remote with { Version = 3, Data = pending!.Data.DeepClone().AsObject() };
        storage.Database.Accept(acknowledged, pending);
        backendType.GetMethod("ObserveRealtimeEntity")!.Invoke(backend, [acknowledged]);
        obj.X = 200;
        var anotherMove = acknowledged with { Version = 4, Data = acknowledged.Data.DeepClone().AsObject() };
        anotherMove.Data["x"] = 180;
        storage.Database.Accept(anotherMove);
        backendType.GetMethod("StageBoardObject")!.Invoke(backend, [board, obj]);
        check(storage.Database.ConflictCount > 0,
            "Two different edits to the same plugin position still preserve a conflict");
    }

    private static void CheckReopeningOlderPresentation(Action<bool, string> check, string root, Type backendType, string userId)
    {
        var previousRoot = Environment.GetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT");
        Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT", Path.Combine(root, "stale-presentation"));
        try
        {
            var storage = new StorageService();
            var board = new WorkspaceBoard { Name = "Reopened", OwnerId = userId, SyncMode = WorkspaceSyncMode.Shared };
            var stale = new BoardObject { Kind = BoardObjectKind.Text, Content = new() { ["text"] = "older local document" } };
            board.Objects.Add(stale);
            storage.SaveWorkspaces([board]);
            var oldEntity = CloudProjection.ProjectBoardObject(stale, board.Id.ToString("N"));
            var received = oldEntity with { Version = 7, Data = oldEntity.Data.DeepClone().AsObject() };
            received.Data["content"]!["text"] = "newer received text";
            storage.Database.Accept(received);
            var newlyReceived = CloudProjection.ProjectBoardObject(new BoardObject { Kind = BoardObjectKind.StickyNote }, board.Id.ToString("N")) with { Version = 1 };
            storage.Database.Accept(newlyReceived);
            storage.Database.Accept(CloudProjection.Project([board], [], userId, false).First() with { Version = 1 });
            storage.Database.Write("supabase-sync-presentation", CloudRules.Serialize(new[] { received, newlyReceived }));
            var backend = RuntimeHelpers.GetUninitializedObject(backendType);
            void Set(string field, object value) => backendType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, value);
            Set("_storage", storage);
            Set("_user", new CloudUser(userId, "fixture", "Fixture", null));
            Set("_inaccessible", new HashSet<string>());
            var flags = BindingFlags.Instance | BindingFlags.Public;
            backendType.GetMethod("LoadPaths", flags)!.Invoke(backend, null);
            var loaded = storage.LoadWorkspaces();
            backendType.GetMethod("Stage", flags)!.Invoke(backend, [loaded, new List<ClipboardHistoryEntry>()]);
            check(storage.Database.Pending().Count == 0 && !storage.Database.Entities(newlyReceived.Id).Single().Deleted,
                "Reopening a stale workspace document neither republishes older text nor deletes newer journal entities");
            var offline = received with { Data = received.Data.DeepClone().AsObject() };
            offline.Data["x"] = 123;
            storage.Database.Stage(offline);
            backendType.GetMethod("LoadPaths", flags)!.Invoke(backend, null);
            backendType.GetMethod("Stage", flags)!.Invoke(backend, [loaded, new List<ClipboardHistoryEntry>()]);
            check(storage.Database.Pending(stale.Id).Single().Data["x"]!.GetValue<int>() == 123
                && storage.Database.Entities(stale.Id).Single().Data["content"]!["text"]!.GetValue<string>() == "newer received text",
                "Reopening preserves pending offline edits and the latest received content");
            loaded.Single().Objects.Single().Y = 456;
            backendType.GetMethod("Stage", flags)!.Invoke(backend, [loaded, new List<ClipboardHistoryEntry>()]);
            check(storage.Database.Pending(stale.Id).Single().Data["y"]!.GetValue<double>() == 456
                && storage.Database.Pending(stale.Id).Single().Data["x"]!.GetValue<int>() == 123
                && storage.Database.Entities(stale.Id).Single().Data["content"]!["text"]!.GetValue<string>() == "newer received text",
                "A real edit after reopening changes only its field without reverting received content");
        }
        finally { Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT", previousRoot); }
    }

    private sealed record PageRow(string Id);

    private sealed class QueueHandler(string itemId, string workspaceId, string userId) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("/workspaces", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                    { Content = new StringContent("{\"message\":\"RLS denied\"}") });
            var body = JsonSerializer.Serialize(new[] { new
            {
                id = itemId, workspace_id = workspaceId, owner_id = userId,
                kind = "boardObject", version = 1, data = new { text = "keep this private", x = 10 }, deleted = false
            } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(body) });
        }
    }

    private sealed class PageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var second = request.RequestUri!.Query.Contains("id=gt.", StringComparison.Ordinal);
            var rows = (second ? Enumerable.Range(500, 1) : Enumerable.Range(0, 500))
                .Select(i => new PageRow(i.ToString("D8"))).ToArray();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(rows, CloudRules.Json)) });
        }
    }
}
