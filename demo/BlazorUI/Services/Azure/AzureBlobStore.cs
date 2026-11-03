using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CodeFoxtrot.CloudLocal;
using CodeFoxtrot.CloudLocal.Blobs;
using CloudBlobItem = CodeFoxtrot.CloudLocal.Blobs.BlobItem;

namespace BlazorUI.Services.AzureCloud;

public sealed class AzureBlobStore : IBlobStore
{
    private readonly BlobServiceClient _client;

    public AzureBlobStore(BlobServiceClient client)
    {
        _client = client;
    }

    public async Task CreateContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        await _client.GetBlobContainerClient(container).CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        Response<bool> deleted = await _client.GetBlobContainerClient(container).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        if (deleted.Value == false)
        {
            throw new CloudLocalNotFoundException($"Container '{container}' was not found.");
        }
    }

    public async IAsyncEnumerable<string> ListContainersAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (BlobContainerItem item in _client.GetBlobContainersAsync(cancellationToken: cancellationToken))
        {
            yield return item.Name;
        }
    }

    public async Task PutAsync(string container, string name, Stream content, BlobWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        BlobClient blob = _client.GetBlobContainerClient(container).GetBlobClient(name);
        BlobUploadOptions upload = new()
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = options?.ContentType },
            Metadata = options?.Metadata is null ? null : new Dictionary<string, string>(options.Metadata)
        };
        await blob.UploadAsync(content, upload, cancellationToken);
    }

    public async Task AppendAsync(string container, string name, Stream content, CancellationToken cancellationToken = default)
    {
        BlobClient blob = _client.GetBlobContainerClient(container).GetBlobClient(name);
        MemoryStream combined = new();
        string? contentType = null;
        IDictionary<string, string>? metadata = null;
        if (await blob.ExistsAsync(cancellationToken))
        {
            Response<BlobProperties> properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            contentType = properties.Value.ContentType;
            metadata = properties.Value.Metadata;
            Response<BlobDownloadStreamingResult> download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            await using Stream current = download.Value.Content;
            await current.CopyToAsync(combined, cancellationToken);
        }

        await content.CopyToAsync(combined, cancellationToken);
        combined.Position = 0;
        await blob.UploadAsync(combined, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = metadata
        }, cancellationToken);
    }

    public async Task<BlobRead> OpenReadAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        BlobClient blob = _client.GetBlobContainerClient(container).GetBlobClient(name);
        if (await blob.ExistsAsync(cancellationToken) == false)
        {
            throw new CloudLocalNotFoundException($"Blob '{container}/{name}' was not found.");
        }

        Response<BlobProperties> properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
        Response<BlobDownloadStreamingResult> download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return new BlobRead
        {
            Content = download.Value.Content,
            ContentType = properties.Value.ContentType,
            Metadata = new Dictionary<string, string>(properties.Value.Metadata)
        };
    }

    public async Task<bool> ExistsAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        return await _client.GetBlobContainerClient(container).GetBlobClient(name).ExistsAsync(cancellationToken);
    }

    public async Task DeleteAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        Response<bool> deleted = await _client.GetBlobContainerClient(container).GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        if (deleted.Value == false)
        {
            throw new CloudLocalNotFoundException($"Blob '{container}/{name}' was not found.");
        }
    }

    public async IAsyncEnumerable<CloudBlobItem> ListAsync(string container, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        BlobContainerClient containerClient = _client.GetBlobContainerClient(container);
        if (await containerClient.ExistsAsync(cancellationToken) == false)
        {
            throw new CloudLocalNotFoundException($"Container '{container}' was not found.");
        }

        await foreach (Azure.Storage.Blobs.Models.BlobItem item in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            yield return new CloudBlobItem(item.Name, item.Properties.ContentLength ?? 0);
        }
    }
}
