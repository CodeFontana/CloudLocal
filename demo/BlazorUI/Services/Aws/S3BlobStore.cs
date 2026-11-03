using Amazon.S3;
using Amazon.S3.Model;
using CodeFoxtrot.CloudLocal;
using CodeFoxtrot.CloudLocal.Blobs;

namespace BlazorUI.Services.Aws;

public sealed class S3BlobStore : IBlobStore
{
    private readonly IAmazonS3 _client;

    public S3BlobStore(IAmazonS3 client)
    {
        _client = client;
    }

    public async Task CreateContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = container }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    public async Task DeleteContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        ListObjectsV2Response listing = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = container }, cancellationToken);
        foreach (S3Object item in listing.S3Objects)
        {
            await _client.DeleteObjectAsync(container, item.Key, cancellationToken);
        }

        await _client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = container }, cancellationToken);
    }

    public async IAsyncEnumerable<string> ListContainersAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ListBucketsResponse response = await _client.ListBucketsAsync(cancellationToken);
        foreach (S3Bucket bucket in response.Buckets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return bucket.BucketName;
        }
    }

    public async Task PutAsync(string container, string name, Stream content, BlobWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        PutObjectRequest request = new()
        {
            BucketName = container,
            Key = name,
            InputStream = content,
            ContentType = options?.ContentType
        };
        if (options?.Metadata is not null)
        {
            foreach (KeyValuePair<string, string> entry in options.Metadata)
            {
                request.Metadata.Add(entry.Key, entry.Value);
            }
        }

        await _client.PutObjectAsync(request, cancellationToken);
    }

    public async Task AppendAsync(string container, string name, Stream content, CancellationToken cancellationToken = default)
    {
        MemoryStream combined = new();
        string? contentType = null;
        try
        {
            GetObjectResponse existing = await _client.GetObjectAsync(container, name, cancellationToken);
            contentType = existing.Headers.ContentType;
            await using Stream current = existing.ResponseStream;
            await current.CopyToAsync(combined, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        await content.CopyToAsync(combined, cancellationToken);
        combined.Position = 0;
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = container,
            Key = name,
            InputStream = combined,
            ContentType = contentType
        }, cancellationToken);
    }

    public async Task<BlobRead> OpenReadAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        try
        {
            GetObjectResponse response = await _client.GetObjectAsync(container, name, cancellationToken);
            Dictionary<string, string> metadata = [];
            foreach (string key in response.Metadata.Keys)
            {
                metadata[key] = response.Metadata[key];
            }

            return new BlobRead
            {
                Content = response.ResponseStream,
                ContentType = response.Headers.ContentType,
                Metadata = metadata
            };
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new CloudLocalNotFoundException($"Blob '{container}/{name}' was not found.", exception);
        }
    }

    public async Task<bool> ExistsAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(container, name, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string container, string name, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(container, name, cancellationToken) == false)
        {
            throw new CloudLocalNotFoundException($"Blob '{container}/{name}' was not found.");
        }

        await _client.DeleteObjectAsync(container, name, cancellationToken);
    }

    public async IAsyncEnumerable<BlobItem> ListAsync(string container, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ListObjectsV2Response response = await _client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = container,
            Prefix = prefix
        }, cancellationToken);
        foreach (S3Object item in response.S3Objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new BlobItem(item.Key, item.Size ?? 0);
        }
    }
}
