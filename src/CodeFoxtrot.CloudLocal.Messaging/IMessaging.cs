namespace CodeFoxtrot.CloudLocal.Messaging;

public interface IQueueMessenger
{
    Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default);

    Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string queue, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default);

    Task CompleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default);

    Task AbandonAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default);

    Task DeadLetterAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default);
}

public interface ITopicMessenger
{
    Task CreateTopicAsync(string topic, CancellationToken cancellationToken = default);

    Task CreateSubscriptionAsync(string topic, string subscription, CancellationToken cancellationToken = default);

    Task PublishAsync(string topic, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string topic, string subscription, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default);

    Task CompleteAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default);

    Task AbandonAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default);

    Task DeadLetterAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default);
}

public sealed record MessageDelivery(string MessageId, string Receipt, byte[] Body, int DequeueCount);
