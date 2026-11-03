using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeFoxtrot.CloudLocal.Tables;

public interface ITableStore
{
    Task CreateTableAsync(string table, CancellationToken cancellationToken = default);

    Task DeleteTableAsync(string table, CancellationToken cancellationToken = default);

    Task UpsertAsync(string table, TableEntity entity, CancellationToken cancellationToken = default);

    Task<TableEntity> GetAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default);

    IAsyncEnumerable<TableEntity> QueryAsync(string table, string partitionKey, string? rowKey = null, CancellationToken cancellationToken = default);
}

public sealed class TableEntity
{
    public required string PartitionKey { get; init; }

    public required string RowKey { get; init; }

    public Dictionary<string, TableValue> Properties { get; init; } = new(StringComparer.Ordinal);
}

public enum TableValueKind
{
    String,
    Boolean,
    Integer,
    Double,
    Timestamp,
    Binary
}

[JsonConverter(typeof(TableValueJsonConverter))]
public readonly record struct TableValue
{
    public TableValueKind Kind { get; init; }

    public string? String { get; init; }

    public bool Boolean { get; init; }

    public long Integer { get; init; }

    public double Double { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public byte[]? Binary { get; init; }

    public static TableValue FromString(string value) => new() { Kind = TableValueKind.String, String = value };

    public static TableValue FromBoolean(bool value) => new() { Kind = TableValueKind.Boolean, Boolean = value };

    public static TableValue FromInteger(long value) => new() { Kind = TableValueKind.Integer, Integer = value };

    public static TableValue FromDouble(double value) => new() { Kind = TableValueKind.Double, Double = value };

    public static TableValue FromTimestamp(DateTimeOffset value) => new() { Kind = TableValueKind.Timestamp, Timestamp = value };

    public static TableValue FromBinary(byte[] value) => new() { Kind = TableValueKind.Binary, Binary = value };
}

public sealed class TableValueJsonConverter : JsonConverter<TableValue>
{
    public override TableValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        string kind = root.GetProperty("kind").GetString() ?? "";
        return kind switch
        {
            "string" => TableValue.FromString(root.GetProperty("string").GetString() ?? ""),
            "boolean" => TableValue.FromBoolean(root.GetProperty("boolean").GetBoolean()),
            "integer" => TableValue.FromInteger(root.GetProperty("integer").GetInt64()),
            "double" => TableValue.FromDouble(root.GetProperty("double").GetDouble()),
            "timestamp" => TableValue.FromTimestamp(root.GetProperty("timestamp").GetDateTimeOffset()),
            "binary" => TableValue.FromBinary(root.GetProperty("binary").GetBytesFromBase64()),
            _ => throw new JsonException($"Unknown table value kind '{kind}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, TableValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value.Kind)
        {
            case TableValueKind.String:
                writer.WriteString("kind", "string");
                writer.WriteString("string", value.String);
                break;
            case TableValueKind.Boolean:
                writer.WriteString("kind", "boolean");
                writer.WriteBoolean("boolean", value.Boolean);
                break;
            case TableValueKind.Integer:
                writer.WriteString("kind", "integer");
                writer.WriteNumber("integer", value.Integer);
                break;
            case TableValueKind.Double:
                writer.WriteString("kind", "double");
                writer.WriteNumber("double", value.Double);
                break;
            case TableValueKind.Timestamp:
                writer.WriteString("kind", "timestamp");
                writer.WriteString("timestamp", value.Timestamp);
                break;
            case TableValueKind.Binary:
                writer.WriteString("kind", "binary");
                writer.WriteBase64String("binary", value.Binary ?? []);
                break;
            default:
                throw new JsonException($"Unknown table value kind '{value.Kind}'.");
        }

        writer.WriteEndObject();
    }
}
