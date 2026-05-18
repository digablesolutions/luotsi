using System.Text;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Infrastructure.System;

public sealed class PhysicalFileSystem : IFileSystem
{
    private const int DefaultBufferSize = 16 * 1024;

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.EnumerateFiles(path, searchPattern, searchOption).ToArray();

    public Task WriteAllTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, text, encoding, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public Stream OpenRead(string path) =>
        new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = DefaultBufferSize,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

    public Stream OpenWrite(string path, bool overwrite = true) =>
        new FileStream(path, new FileStreamOptions
        {
            Mode = overwrite ? FileMode.Create : FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = DefaultBufferSize,
            Options = FileOptions.Asynchronous
        });

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
