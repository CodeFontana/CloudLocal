namespace CodeFoxtrot.CloudLocal.Messaging;

public sealed class FileQueueMessenger : IQueueMessenger
{
    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxReceives;

    public FileQueueMessenger(string root, TimeProvider? timeProvider = null, int maxReceives = 5)
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

    public async Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string queue, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredMessage> messages = await Mailbox(queue).ReceiveAsync(lockDuration, maxMessages, cancellationToken);
        return messages.Select(ToDelivery).ToArray();
    }

    public Task CompleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return Mailbox(queue).CompleteAsync(messageId, receipt, cancellationToken);
    }

    public Task AbandonAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return Mailbox(queue).AbandonAsync(messageId, receipt, cancellationToken);
    }

    public Task DeadLetterAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return Mailbox(queue).DeadLetterAsync(messageId, receipt, cancellationToken);
    }

    private ClaimFolder Mailbox(string queue)
    {
        return new ClaimFolder(LocalPath.Child(_root, "messaging", "queues", queue), _timeProvider, _maxReceives, "deadletter");
    }

    private static MessageDelivery ToDelivery(StoredMessage message)
    {
        return new MessageDelivery(message.MessageId, message.Receipt, message.Body, message.DequeueCount);
    }
}

public sealed class FileTopicMessenger : ITopicMessenger
{
    private readonly string _root;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxReceives;

    public FileTopicMessenger(string root, TimeProvider? timeProvider = null, int maxReceives = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReceives, 1);
        _root = root;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxReceives = maxReceives;
    }

    public Task CreateTopicAsync(string topic, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(SubscriptionsRoot(topic));
        return Task.CompletedTask;
    }

    public async Task CreateSubscriptionAsync(string topic, string subscription, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = LocalPath.Child(SubscriptionsRoot(topic), subscription);
        bool created = !Directory.Exists(path);
        ClaimFolder mailbox = new(path, _timeProvider, _maxReceives, "deadletter");
        mailbox.Ensure();

        if (!created)
        {
            return;
        }

        foreach (StoredMessage message in await Outbox(topic).ListReadyAsync(cancellationToken))
        {
            await mailbox.EnqueueAsync(message.Body, cancellationToken);
        }
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        await Outbox(topic).EnqueueAsync(body, cancellationToken);
        string root = SubscriptionsRoot(topic);

        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (string subscriptionPath in Directory.GetDirectories(root))
        {
            ClaimFolder mailbox = new(subscriptionPath, _timeProvider, _maxReceives, "deadletter");
            await mailbox.EnqueueAsync(body, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string topic, string subscription, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredMessage> messages = await Mailbox(topic, subscription).ReceiveAsync(lockDuration, maxMessages, cancellationToken);
        return messages.Select(ToDelivery).ToArray();
    }

    public Task CompleteAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return Mailbox(topic, subscription).CompleteAsync(messageId, receipt, cancellationToken);
    }

    public Task AbandonAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return Mailbox(topic, subscription).AbandonAsync(messageId, receipt, cancellationToken);
    }

    public Task DeadLetterAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return Mailbox(topic, subscription).DeadLetterAsync(messageId, receipt, cancellationToken);
    }

    private string SubscriptionsRoot(string topic)
    {
        return LocalPath.Child(_root, "messaging", "topics", topic, "subscriptions");
    }

    private ClaimFolder Mailbox(string topic, string subscription)
    {
        return new ClaimFolder(LocalPath.Child(SubscriptionsRoot(topic), subscription), _timeProvider, _maxReceives, "deadletter");
    }

    private ClaimFolder Outbox(string topic)
    {
        return new ClaimFolder(LocalPath.Child(_root, "messaging", "topics", topic, "outbox"), _timeProvider, _maxReceives, "deadletter");
    }

    private static MessageDelivery ToDelivery(StoredMessage message)
    {
        return new MessageDelivery(message.MessageId, message.Receipt, message.Body, message.DequeueCount);
    }
}
