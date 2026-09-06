using System.Text;

namespace EZBuddy.Core.Licensing;

public sealed class FileLicenseStore : ILicenseStore
{
    public FileLicenseStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EZBuddy",
            "Licensing",
            "EZBuddy.ezlic");
    }

    public string FilePath { get; }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(FilePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(string serializedEntitlement, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serializedEntitlement);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = FilePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, serializedEntitlement, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }

        return Task.CompletedTask;
    }
}
