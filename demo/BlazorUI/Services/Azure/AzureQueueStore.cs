using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using CodeFoxtrot.CloudLocal;
using CloudQueueMessage = CodeFoxtrot.CloudLocal.Queues.QueueMessage;

namespace BlazorUI.Services.AzureCloud;

public sealed class AzureQueueStore : CodeFoxtrot.CloudLocal.Queues.IQueueStore
{
    private const int MaxReceives = 5;
    private readonly QueueServiceClient _client;

    public AzureQueueStore(QueueServiceClient client)
    {
        _client = client;
    }

    public async Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default)
    {
        await _client.GetQueueClient(queue).CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await Poison(queue).CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        await _client.GetQueueClient(queue).SendMessageAsync(Convert.ToBase64String(body.Span), cancellationToken);
    }

    public async Task<IReadOnlyList<CloudQueueMessage>> ReceiveAsync(string queue, TimeSpan visibilityTimeout, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        QueueMessage[] received = await _client.GetQueueClient(queue).ReceiveMessagesAsync(maxMessages, visibilityTimeout, cancellationToken);
        List<CloudQueueMessage> messages = [];
        foreach (QueueMessage message in received)
        {
            byte[] bytes = Convert.FromBase64String(message.Body.ToString());
            if (message.DequeueCount > MaxReceives)
            {
                await Poison(queue).SendMessageAsync(message.Body.ToString(), cancellationToken);
                await _client.GetQueueClient(queue).DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                continue;
            }

            messages.Add(new CloudQueueMessage(message.MessageId, message.PopReceipt, bytes, (int)message.DequeueCount));
        }

        return messages;
    }

    public async Task DeleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetQueueClient(queue).DeleteMessageAsync(messageId, receipt, cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Message '{messageId}' is not locked.", exception);
        }
    }

    public async Task<IReadOnlyList<CloudQueueMessage>> ListPoisonAsync(string queue, CancellationToken cancellationToken = default)
    {
        PeekedMessage[] peeked = await Poison(queue).PeekMessagesAsync(32, cancellationToken);
        return peeked.Select(message => new CloudQueueMessage(message.MessageId, "", Convert.FromBase64String(message.Body.ToString()), (int)message.DequeueCount)).ToArray();
    }

    private QueueClient Poison(string queue)
    {
        return _client.GetQueueClient(queue + "-poison");
    }
}
