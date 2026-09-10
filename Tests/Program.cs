using System.Text.Json;
using System.Text.Json.Serialization;
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
Console.WriteLine($"{passed} verificações concluídas.");
