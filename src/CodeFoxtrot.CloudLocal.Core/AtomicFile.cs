using System.Text;
using System.Text.Json;

namespace CodeFoxtrot.CloudLocal;

public static class AtomicFile
{
    public static async Task WriteAsync(string destinationPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        string? directory = Path.GetDirectoryName(destinationPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Destination path has no directory.", nameof(destinationPath));
        }

        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(destinationPath))
            {
                File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, destinationPath);
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    public static Task WriteTextAsync(string destinationPath, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return WriteAsync(destinationPath, Encoding.UTF8.GetBytes(content), cancellationToken);
    }

    public static Task WriteJsonAsync<T>(string destinationPath, T value, JsonSerializerOptions options, CancellationToken cancellationToken = default)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        return WriteAsync(destinationPath, bytes, cancellationToken);
    }
}
