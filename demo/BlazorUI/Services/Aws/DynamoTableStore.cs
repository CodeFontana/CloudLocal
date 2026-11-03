using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CodeFoxtrot.CloudLocal;
using CodeFoxtrot.CloudLocal.Tables;

namespace BlazorUI.Services.Aws;

public sealed class DynamoTableStore : ITableStore
{
    private readonly IAmazonDynamoDB _client;

    public DynamoTableStore(IAmazonDynamoDB client)
    {
        _client = client;
    }

    public async Task CreateTableAsync(string table, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.CreateTableAsync(new CreateTableRequest
            {
                TableName = table,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                AttributeDefinitions =
                [
                    new AttributeDefinition("PartitionKey", ScalarAttributeType.S),
                    new AttributeDefinition("RowKey", ScalarAttributeType.S)
                ],
                KeySchema =
                [
                    new KeySchemaElement("PartitionKey", KeyType.HASH),
                    new KeySchemaElement("RowKey", KeyType.RANGE)
                ]
            }, cancellationToken);
        }
        catch (ResourceInUseException)
        {
        }

        for (int attempt = 0; attempt < 30; attempt++)
        {
            DescribeTableResponse description = await _client.DescribeTableAsync(table, cancellationToken);
            if (description.Table.TableStatus == TableStatus.ACTIVE)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    public async Task DeleteTableAsync(string table, CancellationToken cancellationToken = default)
    {
        await _client.DeleteTableAsync(table, cancellationToken);
    }

    public async Task UpsertAsync(string table, CodeFoxtrot.CloudLocal.Tables.TableEntity entity, CancellationToken cancellationToken = default)
    {
        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PartitionKey"] = new AttributeValue { S = entity.PartitionKey },
                ["RowKey"] = new AttributeValue { S = entity.RowKey },
                ["Payload"] = new AttributeValue { S = TablePayload.Serialize(entity.Properties) }
            }
        }, cancellationToken);
    }

    public async Task<CodeFoxtrot.CloudLocal.Tables.TableEntity> GetAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        GetItemResponse response = await _client.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = Key(partitionKey, rowKey)
        }, cancellationToken);
        if (response.Item is null || response.Item.Count == 0)
        {
            throw new CloudLocalNotFoundException($"Entity '{partitionKey}/{rowKey}' was not found in table '{table}'.");
        }

        return ToEntity(response.Item);
    }

    public async Task DeleteAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        await GetAsync(table, partitionKey, rowKey, cancellationToken);
        await _client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = table,
            Key = Key(partitionKey, rowKey)
        }, cancellationToken);
    }

    public async IAsyncEnumerable<CodeFoxtrot.CloudLocal.Tables.TableEntity> QueryAsync(
        string table,
        string partitionKey,
        string? rowKey = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Dictionary<string, AttributeValue> values = new()
        {
            [":pk"] = new AttributeValue { S = partitionKey }
        };
        string expression = "PartitionKey = :pk";
        if (rowKey is not null)
        {
            values[":rk"] = new AttributeValue { S = rowKey };
            expression += " AND RowKey = :rk";
        }

        QueryResponse response = await _client.QueryAsync(new QueryRequest
        {
            TableName = table,
            KeyConditionExpression = expression,
            ExpressionAttributeValues = values
        }, cancellationToken);
        foreach (Dictionary<string, AttributeValue> item in response.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ToEntity(item);
        }
    }

    private static Dictionary<string, AttributeValue> Key(string partitionKey, string rowKey)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["PartitionKey"] = new AttributeValue { S = partitionKey },
            ["RowKey"] = new AttributeValue { S = rowKey }
        };
    }

    private static CodeFoxtrot.CloudLocal.Tables.TableEntity ToEntity(Dictionary<string, AttributeValue> item)
    {
        string payload = item.TryGetValue("Payload", out AttributeValue? value) ? value.S : "";
        return new CodeFoxtrot.CloudLocal.Tables.TableEntity
        {
            PartitionKey = item["PartitionKey"].S,
            RowKey = item["RowKey"].S,
            Properties = TablePayload.Deserialize(payload)
        };
    }
}
