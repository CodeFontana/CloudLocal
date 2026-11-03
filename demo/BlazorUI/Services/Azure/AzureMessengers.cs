using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using CodeFoxtrot.CloudLocal;
using CodeFoxtrot.CloudLocal.Messaging;

namespace BlazorUI.Services.AzureCloud;

public sealed class AzureQueueMessenger : IQueueMessenger
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusAdministrationClient _administration;
    private readonly ConcurrentDictionary<string, LockedDelivery> _locks = new();

    public AzureQueueMessenger(ServiceBusClient client, ServiceBusAdministrationClient administration)
    {
        _client = client;
        _administration = administration;
    }

    public Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default)
    {
        return CreateAsync(() => _administration.CreateQueueAsync(queue, cancellationToken));
    }

    public async Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        ServiceBusSender sender = _client.CreateSender(queue);
        await sender.SendMessageAsync(new ServiceBusMessage(body.ToArray()), cancellationToken);
    }

    public async Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string queue, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        ServiceBusReceiver receiver = _client.CreateReceiver(queue, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(2), cancellationToken);
        return Cache(receiver, messages);
    }

    public Task CompleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return SettleAsync(receipt, (receiver, message) => receiver.CompleteMessageAsync(message, cancellationToken));
    }

    public Task AbandonAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return SettleAsync(receipt, (receiver, message) => receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken));
    }

    public Task DeadLetterAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return SettleAsync(receipt, (receiver, message) => receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken));
    }

    private IReadOnlyList<MessageDelivery> Cache(ServiceBusReceiver receiver, IReadOnlyList<ServiceBusReceivedMessage> messages)
    {
        List<MessageDelivery> deliveries = [];
        foreach (ServiceBusReceivedMessage message in messages)
        {
            string receipt = message.LockToken;
            _locks[receipt] = new LockedDelivery(receiver, message);
            deliveries.Add(new MessageDelivery(message.MessageId, receipt, message.Body.ToArray(), message.DeliveryCount));
        }

        return deliveries;
    }

    private async Task SettleAsync(string receipt, Func<ServiceBusReceiver, ServiceBusReceivedMessage, Task> settle)
    {
        if (_locks.TryRemove(receipt, out LockedDelivery? locked) == false)
        {
            throw new CloudLocalNotFoundException("Message is not locked in this process.");
        }

        await settle(locked.Receiver, locked.Message);
    }

    private static async Task CreateAsync(Func<Task> create)
    {
        try
        {
            await create();
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
        }
    }

    private sealed record LockedDelivery(ServiceBusReceiver Receiver, ServiceBusReceivedMessage Message);
}

public sealed class AzureTopicMessenger : ITopicMessenger
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusAdministrationClient _administration;
    private readonly ConcurrentDictionary<string, LockedDelivery> _locks = new();

    public AzureTopicMessenger(ServiceBusClient client, ServiceBusAdministrationClient administration)
    {
        _client = client;
        _administration = administration;
    }

    public Task CreateTopicAsync(string topic, CancellationToken cancellationToken = default)
    {
        return CreateAsync(() => _administration.CreateTopicAsync(topic, cancellationToken));
    }

    public Task CreateSubscriptionAsync(string topic, string subscription, CancellationToken cancellationToken = default)
    {
        return CreateAsync(() => _administration.CreateSubscriptionAsync(topic, subscription, cancellationToken));
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        ServiceBusSender sender = _client.CreateSender(topic);
        await sender.SendMessageAsync(new ServiceBusMessage(body.ToArray()), cancellationToken);
    }

    public async Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string topic, string subscription, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        ServiceBusReceiver receiver = _client.CreateReceiver(topic, subscription, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        IReadOnlyList<ServiceBusReceivedMessage> messages = await receiver.ReceiveMessagesAsync(maxMessages, TimeSpan.FromSeconds(2), cancellationToken);
        List<MessageDelivery> deliveries = [];
        foreach (ServiceBusReceivedMessage message in messages)
        {
            _locks[message.LockToken] = new LockedDelivery(receiver, message);
            deliveries.Add(new MessageDelivery(message.MessageId, message.LockToken, message.Body.ToArray(), message.DeliveryCount));
        }

        return deliveries;
    }

    public Task CompleteAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return SettleAsync(receipt, (receiver, message) => receiver.CompleteMessageAsync(message, cancellationToken));
    }

    public Task AbandonAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return SettleAsync(receipt, (receiver, message) => receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken));
    }

    public Task DeadLetterAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        return SettleAsync(receipt, (receiver, message) => receiver.DeadLetterMessageAsync(message, cancellationToken: cancellationToken));
    }

    private async Task SettleAsync(string receipt, Func<ServiceBusReceiver, ServiceBusReceivedMessage, Task> settle)
    {
        if (_locks.TryRemove(receipt, out LockedDelivery? locked) == false)
        {
            throw new CloudLocalNotFoundException("Message is not locked in this process.");
        }

        await settle(locked.Receiver, locked.Message);
    }

    private static async Task CreateAsync(Func<Task> create)
    {
        try
        {
            await create();
        }
        catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
        }
    }

    private sealed record LockedDelivery(ServiceBusReceiver Receiver, ServiceBusReceivedMessage Message);
}
