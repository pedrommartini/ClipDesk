using System.IO;

namespace ClipDesk.Installer;

/// <summary>Only the current edition's private data is owned by the installer.</summary>
public sealed class InstallationData : IDisposable
{
    private readonly string _root;
    private string? _backup;
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), InstallerBrand.DataDirectoryName);

    public InstallationData(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(_root, Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!_root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(_root).StartsWith("ClipDesk-Data-Test-", StringComparison.Ordinal))
                throw new InvalidOperationException("A limpeza deve usar apenas a pasta privada desta edição do ClipDesk.");
        }
        for (var dir = new DirectoryInfo(_root); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A pasta de dados usa um redirecionamento. A limpeza foi interrompida para preservar os arquivos.");
    }

    public void Stage()
    {
        if (_backup is not null || !Directory.Exists(_root)) return;
        var backup = _root + ".reset-" + Guid.NewGuid().ToString("N");
        Directory.Move(_root, backup);
        _backup = backup;
    }

    public void Commit()
    {
        if (_backup is null) return;
        var cleanup = _backup;
        // Deletion is irreversible: never restore a partially deleted database on failure.
        _backup = null;
        try { Directory.Delete(cleanup, recursive: true); }
        catch (Exception ex) { throw new IOException($"A instalação foi atualizada, mas não foi possível concluir a limpeza dos dados antigos em {cleanup}. Feche os programas que usam essa pasta e tente removê-la novamente.", ex); }
    }

    public void Dispose()
    {
        if (_backup is not null && Directory.Exists(_backup) && !Directory.Exists(_root)) Directory.Move(_backup, _root);
    }
}
