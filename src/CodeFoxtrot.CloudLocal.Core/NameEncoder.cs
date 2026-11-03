using System.Text;

namespace CodeFoxtrot.CloudLocal;

public static class NameEncoder
{
    public static string Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return "_";
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Decode(string encoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encoded);
        if (encoded == "_")
        {
            return string.Empty;
        }

        string padded = encoded.Replace('-', '+').Replace('_', '/');
        int remainder = padded.Length % 4;
        if (remainder == 2)
        {
            padded += "==";
        }
        else if (remainder == 3)
        {
            padded += "=";
        }
        else if (remainder != 0)
        {
            throw new ArgumentException("Encoded name is invalid.", nameof(encoded));
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
