using Azure.Data.Tables;
using CodeFoxtrot.CloudLocal;
using CodeFoxtrot.CloudLocal.Tables;
using CloudTableEntity = CodeFoxtrot.CloudLocal.Tables.TableEntity;
using AzureTableEntity = Azure.Data.Tables.TableEntity;

namespace BlazorUI.Services.AzureCloud;

public sealed class AzureTableStore : ITableStore
{
    private readonly TableServiceClient _client;

    public AzureTableStore(TableServiceClient client)
    {
        _client = client;
    }

    public async Task CreateTableAsync(string table, CancellationToken cancellationToken = default)
    {
        await _client.CreateTableIfNotExistsAsync(table, cancellationToken);
    }

    public async Task DeleteTableAsync(string table, CancellationToken cancellationToken = default)
    {
        await _client.DeleteTableAsync(table, cancellationToken);
    }

    public async Task UpsertAsync(string table, CloudTableEntity entity, CancellationToken cancellationToken = default)
    {
        TableClient client = _client.GetTableClient(table);
        AzureTableEntity stored = new(entity.PartitionKey, entity.RowKey)
        {
            ["Payload"] = TablePayload.Serialize(entity.Properties)
        };
        await client.UpsertEntityAsync(stored, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<CloudTableEntity> GetAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        TableClient client = _client.GetTableClient(table);
        try
        {
            AzureTableEntity stored = await client.GetEntityAsync<AzureTableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken);
            return ToEntity(stored);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Entity '{partitionKey}/{rowKey}' was not found in table '{table}'.", exception);
        }
    }

    public async Task DeleteAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        TableClient client = _client.GetTableClient(table);
        try
        {
            await client.DeleteEntityAsync(partitionKey, rowKey, cancellationToken: cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Entity '{partitionKey}/{rowKey}' was not found in table '{table}'.", exception);
        }
    }

    public async IAsyncEnumerable<CloudTableEntity> QueryAsync(
        string table,
        string partitionKey,
        string? rowKey = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string filter = $"PartitionKey eq '{TablePayload.Quote(partitionKey)}'";
        if (rowKey is not null)
        {
            filter += $" and RowKey eq '{TablePayload.Quote(rowKey)}'";
        }

        await foreach (AzureTableEntity stored in _client.GetTableClient(table).QueryAsync<AzureTableEntity>(filter, cancellationToken: cancellationToken))
        {
            yield return ToEntity(stored);
        }
    }

    private static CloudTableEntity ToEntity(AzureTableEntity stored)
    {
        stored.TryGetValue("Payload", out object? payload);
        return new CloudTableEntity
        {
            PartitionKey = stored.PartitionKey,
            RowKey = stored.RowKey,
            Properties = TablePayload.Deserialize(payload as string)
        };
    }
}
