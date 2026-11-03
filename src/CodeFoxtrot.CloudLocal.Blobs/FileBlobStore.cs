using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CodeFoxtrot.CloudLocal.Blobs;

public sealed class FileBlobStore : IBlobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _root;

    public FileBlobStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    public Task CreateContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ContainerData(container));
        Directory.CreateDirectory(ContainerMeta(container));
        return Task.CompletedTask;
    }

    public Task DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string data = ContainerData(container);
        if (!Directory.Exists(data))
        {
            throw new CloudLocalNotFoundException($"Container '{container}' was not found.");
        }

        Directory.Delete(data, recursive: true);
        string meta = ContainerMeta(container);
        if (Directory.Exists(meta))
        {
            Directory.Delete(meta, recursive: true);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> ListContainersAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        string dataRoot = LocalPath.Child(_root, "blobs", "data");
        if (!Directory.Exists(dataRoot))
        {
            yield break;
        }

        foreach (string directory in Directory.GetDirectories(dataRoot).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Path.GetFileName(directory);
        }
    }

    public async Task PutAsync(string container, string name, Stream content, BlobWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string dataPath = DataPath(container, name);
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
        string tempPath = Path.Combine(Path.GetDirectoryName(dataPath)!, $".{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream file = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, dataPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        BlobMetadataDocument document = new()
        {
            ContentType = options?.ContentType,
            Metadata = options?.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(options.Metadata, StringComparer.Ordinal)
        };

        await AtomicFile.WriteJsonAsync(MetaPath(container, name), document, JsonOptions, cancellationToken);
    }

    public async Task AppendAsync(string container, string name, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string dataPath = DataPath(container, name);
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
        await using FileStream file = new(dataPath, FileMode.Append, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await content.CopyToAsync(file, cancellationToken);
    }

    public async Task<BlobRead> OpenReadAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        string dataPath = DataPath(container, name);

        if (!File.Exists(dataPath))
        {
            throw new CloudLocalNotFoundException($"Blob '{container}/{name}' was not found.");
        }

        BlobMetadataDocument metadata = await ReadMetadataAsync(container, name, cancellationToken);
        FileStream stream = new(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);

        return new BlobRead
        {
            Content = stream,
            ContentType = metadata.ContentType,
            Metadata = metadata.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };
    }

    public Task<bool> ExistsAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(DataPath(container, name)));
    }

    public Task DeleteAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string dataPath = DataPath(container, name);

        if (!File.Exists(dataPath))
        {
            throw new CloudLocalNotFoundException($"Blob '{container}/{name}' was not found.");
        }

        File.Delete(dataPath);
        string metaPath = MetaPath(container, name);

        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<BlobItem> ListAsync(string container, string? prefix = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        string data = ContainerData(container);

        if (!Directory.Exists(data))
        {
            throw new CloudLocalNotFoundException($"Container '{container}' was not found.");
        }

        foreach ((string name, string path) in Enumerate(data, ""))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (prefix is not null && !name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            yield return new BlobItem(name, new FileInfo(path).Length);
        }
    }

    private async Task<BlobMetadataDocument> ReadMetadataAsync(string container, string name, CancellationToken cancellationToken)
    {
        string metaPath = MetaPath(container, name);

        if (!File.Exists(metaPath))
        {
            return new BlobMetadataDocument();
        }

        await using FileStream stream = new(metaPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        BlobMetadataDocument? document = await JsonSerializer.DeserializeAsync<BlobMetadataDocument>(stream, JsonOptions, cancellationToken);
        document ??= new BlobMetadataDocument();
        document.Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        return document;
    }

    private string ContainerData(string container)
    {
        return LocalPath.Child(_root, "blobs", "data", RequireContainer(container));
    }

    private string ContainerMeta(string container)
    {
        return LocalPath.Child(_root, "blobs", "meta", RequireContainer(container));
    }

    private string DataPath(string container, string name)
    {
        string[] parts = LocalPath.SplitRelative(name, nameof(name));
        string[] segments = new string[parts.Length + 3];
        segments[0] = "blobs";
        segments[1] = "data";
        segments[2] = RequireContainer(container);
        parts.CopyTo(segments, 3);
        return LocalPath.Child(_root, segments);
    }

    private string MetaPath(string container, string name)
    {
        string[] parts = LocalPath.SplitRelative(name, nameof(name));
        parts[^1] += ".json";
        string[] segments = new string[parts.Length + 3];
        segments[0] = "blobs";
        segments[1] = "meta";
        segments[2] = RequireContainer(container);
        parts.CopyTo(segments, 3);
        return LocalPath.Child(_root, segments);
    }

    private static string RequireContainer(string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        if (LocalPath.SplitRelative(container, nameof(container)).Length != 1)
        {
            throw new ArgumentException("Container name is invalid.", nameof(container));
        }

        return container;
    }

    private static IEnumerable<(string Name, string Path)> Enumerate(string directory, string relative)
    {
        foreach (string file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(file);

            if (fileName.StartsWith('.'))
            {
                continue;
            }

            string name = relative.Length == 0 ? fileName : relative + "/" + fileName;
            yield return (name, file);
        }

        foreach (string subdirectory in Directory.GetDirectories(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string folder = Path.GetFileName(subdirectory);
            string next = relative.Length == 0 ? folder : relative + "/" + folder;

            foreach ((string name, string path) in Enumerate(subdirectory, next))
            {
                yield return (name, path);
            }
        }
    }

    private sealed class BlobMetadataDocument
    {
        public string? ContentType { get; set; }

        public Dictionary<string, string>? Metadata { get; set; }
    }
}
