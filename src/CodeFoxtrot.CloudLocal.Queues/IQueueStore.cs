namespace CodeFoxtrot.CloudLocal.Queues;

public interface IQueueStore
{
    Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default);

    Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queue, TimeSpan visibilityTimeout, int maxMessages = 1, CancellationToken cancellationToken = default);

    Task DeleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueMessage>> ListPoisonAsync(string queue, CancellationToken cancellationToken = default);
}

public sealed record QueueMessage(string MessageId, string Receipt, byte[] Body, int DequeueCount);
