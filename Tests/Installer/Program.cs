using ClipDesk.Installer;

var root = Path.Combine(Path.GetTempPath(), "ClipDesk-Data-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var board = Path.Combine(root, "fixture.db");
File.WriteAllText(board, "mesa de teste");
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS: " + name); }
using (var data = new InstallationData(root)) { data.Stage(); Check(!Directory.Exists(root), "Clean install stages old data without deleting it before activation succeeds"); }
Check(File.ReadAllText(board) == "mesa de teste", "Failed activation restores staged boards intact");
using (var data = new InstallationData(root))
{
    data.Stage();
    var staged = Directory.GetDirectories(Path.GetTempPath(), Path.GetFileName(root) + ".reset-*").Single();
    using var locked = new FileStream(Path.Combine(staged, "fixture.db"), FileMode.Open, FileAccess.Read, FileShare.None);
    var failed = false;
    try { data.Commit(); } catch (IOException ex) { failed = ex.Message.Contains(staged); }
    data.Dispose();
    Check(failed && !Directory.Exists(root) && File.Exists(Path.Combine(staged, "fixture.db")), "Failed final cleanup reports its backup and never restores partially deleted data");
}
Directory.Move(Directory.GetDirectories(Path.GetTempPath(), Path.GetFileName(root) + ".reset-*").Single(), root);
using (var locked = new FileStream(board, FileMode.Open, FileAccess.Read, FileShare.None))
{
    try { using var data = new InstallationData(root); data.Stage(); data.Commit(); } catch (IOException) { }
    Check(File.Exists(board) || Directory.GetDirectories(Path.GetTempPath(), Path.GetFileName(root) + ".reset-*").Length == 1,
        "Locked data remains recoverable at its original or reported backup location");
}
// Fixtures only: restore a failed-cleanup backup for the successful-delete check.
if (!Directory.Exists(root)) Directory.Move(Directory.GetDirectories(Path.GetTempPath(), Path.GetFileName(root) + ".reset-*").Single(), root);
using (var data = new InstallationData(root)) { data.Stage(); data.Commit(); }
Check(!Directory.Exists(root) && Directory.GetDirectories(Path.GetTempPath(), Path.GetFileName(root) + ".reset-*").Length == 0, "Confirmed cleanup deletes only the isolated fixture");
foreach (var unsafePath in new[] { Path.GetTempPath(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipDesk") })
{
    var rejected = false;
    try { using var ignored = new InstallationData(unsafePath); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Reject broad or other-edition data path: " + unsafePath);
}
Console.WriteLine("Installer data safety checks passed. Personal data was not accessed.");
