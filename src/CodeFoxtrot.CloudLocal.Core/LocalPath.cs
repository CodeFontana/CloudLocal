namespace CodeFoxtrot.CloudLocal;

public static class LocalPath
{
    public static string Child(string root, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (segments.Length == 0)
        {
            throw new ArgumentException("At least one path segment is required.", nameof(segments));
        }

        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string combined = fullRoot;

        foreach (string segment in segments)
        {
            if (IsInvalidSegment(segment))
            {
                throw new ArgumentException($"Path segment '{segment}' is invalid.", nameof(segments));
            }

            combined = Path.Combine(combined, segment);
        }

        string fullPath = Path.GetFullPath(combined);

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string prefix = fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, comparison))
        {
            throw new ArgumentException("Path escapes the root.", nameof(segments));
        }

        return fullPath;
    }

    public static string[] SplitRelative(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string[] parts = name.Split(['/', '\\'], StringSplitOptions.None);

        if (parts.Length == 0 || Array.Exists(parts, IsInvalidSegment))
        {
            throw new ArgumentException("Name is invalid.", parameterName);
        }

        return parts;
    }

    private static bool IsInvalidSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
        {
            return true;
        }

        if (segment.Contains('/') || segment.Contains('\\'))
        {
            return true;
        }

        return segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }
}
