using System.Runtime.CompilerServices;

namespace CodeFoxtrot.CloudLocal.Blobs;

public interface IBlobStore
{
    Task CreateContainerAsync(string container, CancellationToken cancellationToken = default);

    Task DeleteContainerAsync(string container, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ListContainersAsync(CancellationToken cancellationToken = default);

    Task PutAsync(string container, string name, Stream content, BlobWriteOptions? options = null, CancellationToken cancellationToken = default);

    Task AppendAsync(string container, string name, Stream content, CancellationToken cancellationToken = default);

    Task<BlobRead> OpenReadAsync(string container, string name, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string container, string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(string container, string name, CancellationToken cancellationToken = default);

    IAsyncEnumerable<BlobItem> ListAsync(string container, string? prefix = null, CancellationToken cancellationToken = default);
}

public sealed class BlobWriteOptions
{
    public string? ContentType { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record BlobItem(string Name, long Length);

public sealed class BlobRead : IAsyncDisposable
{
    public required Stream Content { get; init; }

    public string? ContentType { get; init; }

    public required IReadOnlyDictionary<string, string> Metadata { get; init; }

    public ValueTask DisposeAsync()
    {
        return Content.DisposeAsync();
    }
}
