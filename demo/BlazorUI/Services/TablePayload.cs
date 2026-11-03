using System.Text.Json;
using CodeFoxtrot.CloudLocal.Tables;

namespace BlazorUI.Services;

internal static class TablePayload
{
    public static string Serialize(IReadOnlyDictionary<string, TableValue> properties)
    {
        return JsonSerializer.Serialize(properties);
    }

    public static Dictionary<string, TableValue> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, TableValue>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<Dictionary<string, TableValue>>(json)
            ?? new Dictionary<string, TableValue>(StringComparer.Ordinal);
    }

    public static string Quote(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
