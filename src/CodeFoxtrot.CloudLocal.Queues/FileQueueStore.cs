using CodeFoxtrot.CloudLocal;

namespace CodeFoxtrot.CloudLocal.Queues;

public sealed class FileQueueStore : IQueueStore
{
    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxReceives;

    public FileQueueStore(string root, TimeProvider? timeProvider = null, int maxReceives = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReceives, 1);
        _root = root;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxReceives = maxReceives;
    }

    public Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Mailbox(queue).Ensure();
        return Task.CompletedTask;
    }

    public Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        return Mailbox(queue).EnqueueAsync(body, cancellationToken);
    }

    public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queue, TimeSpan visibilityTimeout, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredMessage> messages = await Mailbox(queue).ReceiveAsync(visibilityTimeout, maxMessages, cancellationToken);
        return messages.Select(ToQueueMessage).ToArray();
    }

    public Task DeleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return Mailbox(queue).CompleteAsync(messageId, receipt, cancellationToken);
    }

    public async Task<IReadOnlyList<QueueMessage>> ListPoisonAsync(string queue, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredMessage> messages = await Mailbox(queue).ListTerminalAsync(cancellationToken);
        return messages.Select(ToQueueMessage).ToArray();
    }

    private ClaimFolder Mailbox(string queue)
    {
        return new ClaimFolder(LocalPath.Child(_root, "queues", queue), _timeProvider, _maxReceives, "poison");
    }

    private static QueueMessage ToQueueMessage(StoredMessage message)
    {
        return new QueueMessage(message.MessageId, message.Receipt, message.Body, message.DequeueCount);
    }
}
