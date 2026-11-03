using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CodeFoxtrot.CloudLocal.Tables;

public sealed class FileTableStore : ITableStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _root;

    public FileTableStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    public Task CreateTableAsync(string table, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(TablePath(table));
        return Task.CompletedTask;
    }

    public Task DeleteTableAsync(string table, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = TablePath(table);

        if (!Directory.Exists(path))
        {
            throw new CloudLocalNotFoundException($"Table '{table}' was not found.");
        }

        Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    public Task UpsertAsync(string table, TableEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(entity.PartitionKey);
        ArgumentNullException.ThrowIfNull(entity.RowKey);

        string path = EntityPath(table, entity.PartitionKey, entity.RowKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return AtomicFile.WriteJsonAsync(path, entity, JsonOptions, cancellationToken);
    }

    public async Task<TableEntity> GetAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        string path = EntityPath(table, partitionKey, rowKey);

        if (!File.Exists(path))
        {
            throw new CloudLocalNotFoundException($"Entity '{partitionKey}/{rowKey}' was not found in table '{table}'.");
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        TableEntity? entity = await JsonSerializer.DeserializeAsync<TableEntity>(stream, JsonOptions, cancellationToken);

        if (entity is null)
        {
            throw new CloudLocalNotFoundException($"Entity '{partitionKey}/{rowKey}' was not found in table '{table}'.");
        }

        return entity;
    }

    public Task DeleteAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = EntityPath(table, partitionKey, rowKey);

        if (!File.Exists(path))
        {
            throw new CloudLocalNotFoundException($"Entity '{partitionKey}/{rowKey}' was not found in table '{table}'.");
        }

        File.Delete(path);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TableEntity> QueryAsync(string table,
                                                          string partitionKey,
                                                          string? rowKey = null,
                                                          [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (rowKey is not null)
        {
            TableEntity? entity = await TryGetAsync(table, partitionKey, rowKey, cancellationToken);

            if (entity is not null)
            {
                yield return entity;
            }

            yield break;
        }

        string directory = PartitionPath(table, partitionKey);

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (string file in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            TableEntity? entity = await JsonSerializer.DeserializeAsync<TableEntity>(stream, JsonOptions, cancellationToken);

            if (entity is not null)
            {
                yield return entity;
            }
        }
    }

    private async Task<TableEntity?> TryGetAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        string path = EntityPath(table, partitionKey, rowKey);

        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<TableEntity>(stream, JsonOptions, cancellationToken);
    }

    private string TablePath(string table)
    {
        return LocalPath.Child(_root, "tables", Single(table, nameof(table)));
    }

    private string PartitionPath(string table, string partitionKey)
    {
        ArgumentNullException.ThrowIfNull(partitionKey);
        return LocalPath.Child(TablePath(table), NameEncoder.Encode(partitionKey));
    }

    private string EntityPath(string table, string partitionKey, string rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        return LocalPath.Child(PartitionPath(table, partitionKey), NameEncoder.Encode(rowKey) + ".json");
    }

    private static string Single(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (LocalPath.SplitRelative(value, parameterName).Length != 1)
        {
            throw new ArgumentException("Name is invalid.", parameterName);
        }

        return value;
    }
}
