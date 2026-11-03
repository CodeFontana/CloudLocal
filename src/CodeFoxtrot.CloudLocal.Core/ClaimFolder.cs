using System.Text.Json;

namespace CodeFoxtrot.CloudLocal;

public sealed record StoredMessage(string MessageId, string Receipt, byte[] Body, int DequeueCount);

public sealed class ClaimFolder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxReceives;
    private readonly string _terminalFolder;

    public ClaimFolder(string root, TimeProvider timeProvider, int maxReceives, string terminalFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReceives, 1);

        if (string.IsNullOrWhiteSpace(terminalFolder))
        {
            throw new ArgumentException("Terminal folder is required.", nameof(terminalFolder));
        }

        _root = root;
        _timeProvider = timeProvider;
        _maxReceives = maxReceives;
        _terminalFolder = terminalFolder;
    }

    public void Ensure()
    {
        Directory.CreateDirectory(Folder("ready"));
        Directory.CreateDirectory(Folder("inflight"));
        Directory.CreateDirectory(Folder(_terminalFolder));
    }

    public Task EnqueueAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        Ensure();
        string messageId = Guid.NewGuid().ToString("N");
        long ticks = _timeProvider.GetUtcNow().UtcTicks;
        string fileName = $"{ticks:D19}-{messageId}.json";

        MessageEnvelope envelope = new()
        {
            MessageId = messageId,
            Receipt = Guid.NewGuid().ToString("N"),
            Body = body.ToArray(),
            DequeueCount = 0,
            NextVisibleAt = _timeProvider.GetUtcNow()
        };

        return AtomicFile.WriteJsonAsync(Path.Combine(Folder("ready"), fileName), envelope, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredMessage>> ReceiveAsync(TimeSpan visibility, int maxMessages, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
        Ensure();
        await SweepAsync(cancellationToken);

        List<StoredMessage> received = [];

        foreach (string file in Directory.GetFiles(Folder("ready"), "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (received.Count >= maxMessages)
            {
                break;
            }

            string fileName = Path.GetFileName(file);
            string claimingPath = Path.Combine(Folder("inflight"), fileName + ".claiming");
            string inflightPath = Path.Combine(Folder("inflight"), fileName);

            if (!TryMove(file, claimingPath))
            {
                continue;
            }

            MessageEnvelope? envelope;

            try
            {
                envelope = await ReadAsync(claimingPath, cancellationToken);
            }
            catch
            {
                TryMove(claimingPath, file);
                throw;
            }

            if (envelope is null || string.IsNullOrWhiteSpace(envelope.MessageId))
            {
                TryMove(claimingPath, file);
                continue;
            }

            if (envelope.DequeueCount >= _maxReceives)
            {
                TryMove(claimingPath, Path.Combine(Folder(_terminalFolder), fileName));
                continue;
            }

            envelope.DequeueCount++;
            envelope.Receipt = Guid.NewGuid().ToString("N");
            envelope.NextVisibleAt = _timeProvider.GetUtcNow().Add(visibility);

            try
            {
                await AtomicFile.WriteJsonAsync(inflightPath, envelope, JsonOptions, cancellationToken);
            }
            catch
            {
                TryMove(claimingPath, file);
                throw;
            }

            File.Delete(claimingPath);
            received.Add(ToStored(envelope));
        }

        return received;
    }

    public async Task CompleteAsync(string messageId, string receipt, CancellationToken cancellationToken)
    {
        (string path, _) = await RequireInflightAsync(messageId, receipt, cancellationToken);
        File.Delete(path);
    }

    public async Task AbandonAsync(string messageId, string receipt, CancellationToken cancellationToken)
    {
        (string path, _) = await RequireInflightAsync(messageId, receipt, cancellationToken);
        string readyPath = Path.Combine(Folder("ready"), Path.GetFileName(path));

        if (!TryMove(path, readyPath))
        {
            throw new IOException($"Message '{messageId}' could not be returned to the ready folder.");
        }
    }

    public async Task DeadLetterAsync(string messageId, string receipt, CancellationToken cancellationToken)
    {
        (string path, _) = await RequireInflightAsync(messageId, receipt, cancellationToken);
        string terminalPath = Path.Combine(Folder(_terminalFolder), Path.GetFileName(path));

        if (!TryMove(path, terminalPath))
        {
            throw new IOException($"Message '{messageId}' could not be moved to '{_terminalFolder}'.");
        }
    }

    public Task<IReadOnlyList<StoredMessage>> ListReadyAsync(CancellationToken cancellationToken)
    {
        return ListFolderAsync(Folder("ready"), cancellationToken);
    }

    public Task<IReadOnlyList<StoredMessage>> ListTerminalAsync(CancellationToken cancellationToken)
    {
        return ListFolderAsync(Folder(_terminalFolder), cancellationToken);
    }

    private async Task<IReadOnlyList<StoredMessage>> ListFolderAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<StoredMessage> messages = [];

        foreach (string file in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            MessageEnvelope? envelope = await ReadAsync(file, cancellationToken);

            if (envelope is not null && !string.IsNullOrWhiteSpace(envelope.MessageId))
            {
                messages.Add(ToStored(envelope));
            }
        }

        return messages;
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        string inflight = Folder("inflight");

        if (!Directory.Exists(inflight))
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (string file in Directory.GetFiles(inflight, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            MessageEnvelope? envelope = await ReadAsync(file, cancellationToken);

            if (envelope is null || envelope.NextVisibleAt > now)
            {
                continue;
            }

            string fileName = Path.GetFileName(file);
            string destinationFolder = envelope.DequeueCount >= _maxReceives ? _terminalFolder : "ready";
            TryMove(file, Path.Combine(Folder(destinationFolder), fileName));
        }
    }

    private async Task<(string Path, MessageEnvelope Envelope)> RequireInflightAsync(string messageId, string receipt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receipt);
        string? path = Find(Folder("inflight"), messageId);

        if (path is null)
        {
            throw new CloudLocalNotFoundException($"Message '{messageId}' is not locked.");
        }

        MessageEnvelope? envelope = await ReadAsync(path, cancellationToken);

        if (envelope is null)
        {
            throw new CloudLocalNotFoundException($"Message '{messageId}' is not locked.");
        }

        if (!string.Equals(envelope.Receipt, receipt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The receipt does not match the current lock.");
        }

        return (path, envelope);
    }

    private string Folder(string name)
    {
        return LocalPath.Child(_root, name);
    }

    private static string? Find(string directory, string messageId)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        string[] matches = Directory.GetFiles(directory, $"*-{messageId}.json");
        return matches.Length == 0 ? null : matches[0];
    }

    private static bool TryMove(string source, string destination)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<MessageEnvelope?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<MessageEnvelope>(stream, JsonOptions, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static StoredMessage ToStored(MessageEnvelope envelope)
    {
        return new StoredMessage(envelope.MessageId, envelope.Receipt, envelope.Body ?? [], envelope.DequeueCount);
    }

    private sealed class MessageEnvelope
    {
        public string MessageId { get; set; } = "";

        public string Receipt { get; set; } = "";

        public byte[]? Body { get; set; }

        public int DequeueCount { get; set; }

        public DateTimeOffset NextVisibleAt { get; set; }
    }
}
