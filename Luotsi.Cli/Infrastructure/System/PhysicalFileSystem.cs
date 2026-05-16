using System.Text;

namespace Luotsi.Cli.Infrastructure;

public sealed class PhysicalFileSystem : IFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Task WriteAllTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, text, encoding, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public Stream OpenWrite(string path, bool overwrite = true) =>
        new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public string GetTempPath() => Path.GetTempPath();
}