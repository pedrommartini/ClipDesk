using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using ClipDesk.Core;
using ClipDesk.Models;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
    passed++;
}
var prefix = new string('a', 200);
var uuid = Guid.NewGuid();
Check(CloudRules.CanonicalGuidId(uuid.ToString()) == uuid.ToString("N")
    && CloudRules.CanonicalGuidId(uuid.ToString("N")) == uuid.ToString("N"),
    "UUIDs remotos e locais apontam para a mesma entidade da mesa");
var syncDatabase = new LocalDatabase(Path.Combine(Path.GetTempPath(), "ClipDesk-Sync-Check-" + uuid.ToString("N") + ".db"));
var stableRemote = new CloudEntity(uuid.ToString("N"), "workspace", null, 1, new JsonObject { ["name"] = "Mesa" });
Check(syncDatabase.Accept(stableRemote) && !syncDatabase.Accept(stableRemote),
    "Leitura remota idêntica não dispara nova sincronização");
var noteId = Guid.NewGuid().ToString("N");
var noteOriginal = new CloudEntity(noteId, "boardObject", uuid.ToString("N"), 1, new JsonObject { ["text"] = "Original" });
syncDatabase.Accept(noteOriginal);
syncDatabase.Stage(noteOriginal with { Data = new JsonObject { ["text"] = "Primeira edição" } });
var sentNote = syncDatabase.Pending(noteId).Single();
syncDatabase.Stage(noteOriginal with { Data = new JsonObject { ["text"] = "Segunda edição" } });
syncDatabase.Accept(noteOriginal with { Version = 2, Data = new JsonObject { ["text"] = "Primeira edição" } }, sentNote);
Check(syncDatabase.Entities(noteId).Single().Data["text"]?.GetValue<string>() == "Segunda edição"
    && syncDatabase.Pending(noteId).Count == 1,
    "Resposta atrasada da nota preserva a edição mais recente");
var first = new ClipboardHistoryEntry { Type = ClipboardItemType.Text, Text = prefix + "A", Preview = prefix };
var second = new ClipboardHistoryEntry { Type = ClipboardItemType.Text, Text = prefix + "B", Preview = prefix };
Check(!HistoryContent.AreEquivalent(first, second), "Textos com o mesmo preview preservam conteúdos diferentes");
second.Text = first.Text;
Check(HistoryContent.AreEquivalent(first, second), "Cópias consecutivas idênticas são reconhecidas");
var imageA = new ClipboardHistoryEntry { Type = ClipboardItemType.Image, StoredFilePath = "hash-a.png", Preview = "Imagem salva" };
var imageB = new ClipboardHistoryEntry { Type = ClipboardItemType.Image, StoredFilePath = "hash-b.png", Preview = "Imagem salva" };
Check(!HistoryContent.AreEquivalent(imageA, imageB), "Imagens diferentes não são descartadas por descrição igual");
var filesA = new ClipboardHistoryEntry { Type = ClipboardItemType.File, FilePaths = ["a", "b", "c", "d", "e"] };
var filesB = new ClipboardHistoryEntry { Type = ClipboardItemType.File, FilePaths = ["a", "b", "c", "d", "f"] };
Check(!HistoryContent.AreEquivalent(filesA, filesB), "Compara todos os arquivos, inclusive além dos quatro exibidos");
var item = HistoryContent.ToItem(filesA);
var another = HistoryContent.ToItem(filesA);
Check(item.Id != another.Id && item.Id != filesA.Id, "Cada adição à mesa possui identidade própria");
item.FilePaths.Add("new");
Check(filesA.FilePaths.Count == 5 && another.FilePaths.Count == 5, "Editar uma cópia não altera o histórico nem a outra cópia");
foreach (var entry in new[] { first, imageA, filesA, new ClipboardHistoryEntry { Type = ClipboardItemType.Link, Url = "https://example.com", Title = "Site" } })
{
    var converted = HistoryContent.ToItem(entry);
    Check(converted.Text == entry.Text && converted.Url == entry.Url && converted.StoredFilePath == entry.StoredFilePath && converted.Type == entry.Type,
        $"Transferência do tipo {entry.Type} mantém o conteúdo");
}
var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
var legacy = """[{"Id":"existing","Type":"Text","DisplayName":"Nota","Text":"Olá","X":123,"Y":456}]""";
var loaded = JsonSerializer.Deserialize<List<ClipboardItem>>(legacy, options)!;
Check(loaded[0].Id == "existing" && loaded[0].Text == "Olá" && loaded[0].X == 123 && loaded[0].Y == 456,
    "Biblioteca compartilhada lê o formato anterior sem perder IDs e posições");
Check(loaded[0].Width == 0 && loaded[0].Height == 0,
    "Itens antigos recebem tamanho adaptativo sem quebrar a leitura");
loaded[0].Width = 420;
loaded[0].Height = 280;
loaded[0].ZIndex = 17;
var resizedJson = JsonSerializer.Serialize(loaded, options);
var resized = JsonSerializer.Deserialize<List<ClipboardItem>>(resizedJson, options)!;
Check(resized[0].Width == 420 && resized[0].Height == 280 && resized[0].ZIndex == 17,
    "Dimensões e ordem de sobreposição das prévias são persistidas");
var todayHistory = new ClipboardHistoryEntry { CapturedAt = DateTime.Today.AddHours(9) };
var yesterdayHistory = new ClipboardHistoryEntry { CapturedAt = DateTime.Today.AddDays(-1).AddHours(9) };
Check(todayHistory.CapturedDayLabel == "Hoje" && yesterdayHistory.CapturedDayLabel == "Ontem",
    "Histórico identifica claramente hoje e ontem");
var historicEntry = new ClipboardHistoryEntry { CapturedAt = new DateTime(DateTime.Today.Year - 1, 3, 8) };
Check(historicEntry.CapturedDayLabel.Contains((DateTime.Today.Year - 1).ToString()),
    "Histórico preserva o ano em datas antigas");
var viewport = new BoardViewport(12000, 8000, 1200, 800, new BoardViewportState { Zoom = 1, PanX = -300, PanY = -200 });
var anchor = new BoardPoint(200, 200);
var beforeZoom = viewport.ScreenToWorld(anchor);
viewport.SetZoomAt(.5, anchor);
var afterZoom = viewport.ScreenToWorld(anchor);
Check(Math.Abs(beforeZoom.X - afterZoom.X) < .001 && Math.Abs(beforeZoom.Y - afterZoom.Y) < .001,
    "Zoom da mesa preserva o ponto sob o cursor");
viewport.Fit();
Check(Math.Abs(viewport.Zoom - .1) < .001 && viewport.WorldToScreen(new BoardPoint(0, 0)).X >= 0,
    "Enquadramento usa a mesa inteira sem escala individual dos cards");
var legacyBoard = new WorkspaceBoard { WorldWidth = 0, WorldHeight = double.NaN, SchemaVersion = 0, Items = [new ClipboardItem { X = 20000, Y = -4, Width = 340, Height = 220 }] };
Check(BoardMigration.Normalize(legacyBoard) && legacyBoard.WorldWidth == BoardSpace.DefaultWidth && legacyBoard.Items[0].X <= legacyBoard.WorldWidth - 340 && legacyBoard.Items[0].Y == 0,
    "Mesa antiga migra para coordenadas limitadas compartilhadas");
var legacyPlugin = new BoardObject { Kind = BoardObjectKind.Checklist };
var legacyPluginBoard = new WorkspaceBoard { Objects = [legacyPlugin] };
Check(BoardMigration.Normalize(legacyPluginBoard) && legacyPlugin.PluginId == "clipdesk.checklist"
    && legacyPlugin.PluginName == "Checklist" && legacyPlugin.PluginVersion == "1.0.0",
    "Plugin antigo recebe identificador e versão estáveis sem perder o tipo original");
Check(!BoardMigration.Normalize(legacyPluginBoard), "Migração de identidade do plugin é estável ao reabrir");
var duplicateBoard = new WorkspaceBoard
{
    Id = legacyBoard.Id, Name = "Cópia", SyncMode = WorkspaceSyncMode.Shared, OwnerId = "old-owner",
    Items = [new ClipboardItem { Id = "copied-card" }],
    Objects = [new BoardObject { Id = "copied-object" }, new BoardObject { Kind = BoardObjectKind.Connector, Content = new() { ["nodeIds"] = "card:copied-card;object:copied-object" } }]
};
var duplicateBoards = new List<WorkspaceBoard> { legacyBoard, duplicateBoard };
Check(BoardIdentityMigration.EnsureUniqueBoardIds(duplicateBoards) && duplicateBoards[0].Id != duplicateBoards[1].Id
    && duplicateBoards[1].Name == "Cópia" && duplicateBoards[1].SyncMode == WorkspaceSyncMode.Local && duplicateBoards[1].OwnerId is null,
    "Mesas copiadas com o mesmo ID são preservadas com identidades distintas");
Check(!BoardIdentityMigration.EnsureUniqueBoardIds(duplicateBoards), "Migração de IDs repetidos é estável ao reabrir");
Check(duplicateBoard.Items[0].Id != "copied-card" && duplicateBoard.Objects[0].Id != "copied-object"
    && duplicateBoard.Objects[1].Content["nodeIds"] == $"card:{duplicateBoard.Items[0].Id};object:{duplicateBoard.Objects[0].Id}"
    && duplicateBoard.Objects.All(obj => obj.WorkspaceId == duplicateBoard.Id.ToString("N")),
    "Cópia da mesa mantém referências de conexões e IDs de elementos independentes");
var futureObject = new BoardObject { Kind = BoardObjectKind.Stroke, Style = new() { ["color"] = "#22D3EE" }, Content = new() { ["points"] = "0,0;10,10" } };
Check(futureObject.Kind == BoardObjectKind.Stroke && futureObject.Style["color"] == "#22D3EE" && futureObject.Content.ContainsKey("points"),
    "Contrato de objeto suporta ferramentas criativas sem anexos");
var calculator = BoardCalculator.Press("12+3", "=");
Check(calculator.Expression == "15" && calculator.Display == "15", "Calculadora da mesa resolve operações básicas");
calculator = BoardCalculator.Press(calculator.Expression, "×");
calculator = BoardCalculator.Press(calculator.Expression, "4");
calculator = BoardCalculator.Press(calculator.Expression, "=");
Check(calculator.Display == "60", "Calculadora mantém a expressão para operações encadeadas");
calculator = BoardCalculator.Press("10", "%");
Check(calculator.Display == "0.1", "Calculadora aplica porcentagem sem depender da interface");
var creativeBoard = new WorkspaceBoard { SyncMode = WorkspaceSyncMode.PersonalCloud, OwnerId = "owner" };
creativeBoard.Objects.Add(new BoardObject
{
    Id = Guid.NewGuid().ToString("N"), WorkspaceId = creativeBoard.Id.ToString("N"), Kind = BoardObjectKind.StickyNote,
    X = 210, Y = 320, Width = 240, Height = 180, Style = new() { ["fill"] = "#F6D365" }, Content = new() { ["text"] = "Nota sincronizada" }
});
creativeBoard.Objects.Add(new BoardObject
{
    Id = Guid.NewGuid().ToString("N"), WorkspaceId = creativeBoard.Id.ToString("N"), Kind = BoardObjectKind.Connector,
    X = 100, Y = 120, Width = 500, Height = 260,
    Style = new() { ["stroke"] = "#8393AD", ["thickness"] = "1.35" },
    Content = new() { ["nodeIds"] = "card:card-a;object:text-a;card:card-c", ["points"] = "4,4;250,90;496,256" }
});
creativeBoard.Objects.Add(new BoardObject
{
    Id = Guid.NewGuid().ToString("N"), WorkspaceId = creativeBoard.Id.ToString("N"), Kind = BoardObjectKind.Calculator,
    PluginId = "clipdesk.calculator", PluginName = "Calculadora", PluginVersion = "1.0.0", X = 60, Y = 80, Width = 300, Height = 390,
    Content = new() { ["expression"] = "6*7", ["display"] = "42" }
});
var creativeProjection = CloudProjection.Project([creativeBoard], [], "owner", false).ToList();
var creativeRoundTrip = CloudProjection.Materialize(creativeProjection, new Dictionary<string, string>()).Single();
Check(creativeProjection.Any(entity => entity.Kind == "boardObject") && creativeRoundTrip.Objects.Single(o => o.Kind == BoardObjectKind.StickyNote).Content["text"] == "Nota sincronizada",
    "Objetos criativos são preservados na sincronização da mesa");
Check(creativeRoundTrip.Objects.Single(o => o.Kind == BoardObjectKind.Connector).Content["nodeIds"] == "card:card-a;object:text-a;card:card-c",
    "Conexões mistas preservam cards e objetos criativos na sincronização");
Check(creativeRoundTrip.Objects.Single(o => o.Kind == BoardObjectKind.Calculator).PluginId == "clipdesk.calculator"
    && creativeRoundTrip.Objects.Single(o => o.Kind == BoardObjectKind.Calculator).PluginName == "Calculadora"
    && creativeRoundTrip.Objects.Single(o => o.Kind == BoardObjectKind.Calculator).PluginVersion == "1.0.0",
    "Sincronização preserva a identidade e a versão independente do plugin");
foreach (var mode in new[] { WorkspaceSyncMode.Local, WorkspaceSyncMode.PersonalCloud, WorkspaceSyncMode.Shared })
{
    var board = new WorkspaceBoard { SyncMode = mode, OwnerId = mode == WorkspaceSyncMode.Shared ? "owner" : null };
    board.Objects.Add(new BoardObject
    {
        WorkspaceId = board.Id.ToString("N"), Kind = BoardObjectKind.Plugin,
        PluginId = "clipdesk.example", PluginName = "Plugin de exemplo", PluginVersion = "1.1.0",
        Content = new() { ["value"] = "estado editado", ["completed"] = "true" }
    });
    var locallyRestored = JsonSerializer.Deserialize<WorkspaceBoard>(JsonSerializer.Serialize(board))!;
    Check(locallyRestored.Objects.Single().PluginId == "clipdesk.example"
        && locallyRestored.Objects.Single().PluginName == "Plugin de exemplo"
        && locallyRestored.Objects.Single().Content["value"] == "estado editado",
        $"Plugin preserva seu estado na mesa {mode} ao salvar localmente");
    var projection = CloudProjection.Project([board], [], "owner", false).ToList();
    if (mode == WorkspaceSyncMode.Local)
        Check(projection.Count == 0, "Mesa somente local não envia plugin à nuvem");
    else
    {
        var received = CloudProjection.Materialize(projection, new Dictionary<string, string>()).Single();
        var plugin = received.Objects.Single();
        Check(received.SyncMode == mode && plugin.PluginId == "clipdesk.example"
            && plugin.PluginName == "Plugin de exemplo"
            && plugin.PluginVersion == "1.1.0" && plugin.Content["value"] == "estado editado"
            && plugin.Content["completed"] == "true",
            $"Plugin preserva identidade, versão e estado na mesa {mode} após sincronização");
    }
}
Console.WriteLine($"{passed} verificações concluídas.");
