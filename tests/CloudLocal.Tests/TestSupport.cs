namespace CloudLocal.Tests;

internal sealed class TempRoot : IDisposable
{
    public TempRoot()
    {
        Root = Path.Combine(Path.GetTempPath(), "cloudlocal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }
}
