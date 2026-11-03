using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using CodeFoxtrot.CloudLocal;
using CodeFoxtrot.CloudLocal.Messaging;

namespace BlazorUI.Services.Aws;

public sealed class SqsQueueMessenger : IQueueMessenger
{
    private readonly IAmazonSQS _client;
    private readonly ConcurrentDictionary<string, string> _bodies = new();

    public SqsQueueMessenger(IAmazonSQS client)
    {
        _client = client;
    }

    public async Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default)
    {
        await _client.CreateQueueAsync(queue, cancellationToken);
    }

    public async Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        await _client.SendMessageAsync(await UrlAsync(queue, cancellationToken), Convert.ToBase64String(body.Span), cancellationToken);
    }

    public async Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string queue, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        ReceiveMessageResponse response = await _client.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = await UrlAsync(queue, cancellationToken),
            MaxNumberOfMessages = maxMessages,
            VisibilityTimeout = Math.Max(1, (int)lockDuration.TotalSeconds),
            WaitTimeSeconds = 1,
            MessageSystemAttributeNames = ["ApproximateReceiveCount"]
        }, cancellationToken);
        return response.Messages.Select(message =>
        {
            _bodies[message.ReceiptHandle] = message.Body;
            return new MessageDelivery(
                message.MessageId,
                message.ReceiptHandle,
                Convert.FromBase64String(message.Body),
                ReadCount(message));
        }).ToArray();
    }

    public async Task CompleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        await _client.DeleteMessageAsync(await UrlAsync(queue, cancellationToken), receipt, cancellationToken);
    }

    public async Task AbandonAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        await _client.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
        {
            QueueUrl = await UrlAsync(queue, cancellationToken),
            ReceiptHandle = receipt,
            VisibilityTimeout = 0
        }, cancellationToken);
    }

    public async Task DeadLetterAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        string deadLetter = queue + "-deadletter";
        await _client.CreateQueueAsync(deadLetter, cancellationToken);
        string body = _bodies.TryGetValue(receipt, out string? cached) ? cached : Convert.ToBase64String(Encoding.UTF8.GetBytes(messageId));
        await _client.SendMessageAsync(await UrlAsync(deadLetter, cancellationToken), body, cancellationToken);
        await CompleteAsync(queue, messageId, receipt, cancellationToken);
    }

    private async Task<string> UrlAsync(string queue, CancellationToken cancellationToken)
    {
        return (await _client.GetQueueUrlAsync(queue, cancellationToken)).QueueUrl;
    }

    private static int ReadCount(Message message)
    {
        if (message.Attributes is not null && message.Attributes.TryGetValue("ApproximateReceiveCount", out string? count) && int.TryParse(count, out int value))
        {
            return value;
        }

        return 1;
    }
}

public sealed class SnsTopicMessenger : ITopicMessenger
{
    private readonly IAmazonSimpleNotificationService _topics;
    private readonly IAmazonSQS _queues;
    private readonly ConcurrentDictionary<string, string> _bodies = new();

    public SnsTopicMessenger(IAmazonSimpleNotificationService topics, IAmazonSQS queues)
    {
        _topics = topics;
        _queues = queues;
    }

    public async Task CreateTopicAsync(string topic, CancellationToken cancellationToken = default)
    {
        await _topics.CreateTopicAsync(topic, cancellationToken);
    }

    public async Task CreateSubscriptionAsync(string topic, string subscription, CancellationToken cancellationToken = default)
    {
        string queueName = QueueName(topic, subscription);
        CreateQueueResponse created = await _queues.CreateQueueAsync(queueName, cancellationToken);
        string queueArn = await QueueArnAsync(created.QueueUrl, cancellationToken);
        string topicArn = await TopicArnAsync(topic, cancellationToken);
        await _queues.SetQueueAttributesAsync(new SetQueueAttributesRequest
        {
            QueueUrl = created.QueueUrl,
            Attributes = new Dictionary<string, string>
            {
                ["Policy"] = AllowSns(queueArn, topicArn)
            }
        }, cancellationToken);
        await _topics.SubscribeAsync(new SubscribeRequest
        {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queueArn,
            Attributes = new Dictionary<string, string> { ["RawMessageDelivery"] = "true" }
        }, cancellationToken);
    }

    public async Task PublishAsync(string topic, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        await _topics.PublishAsync(new PublishRequest
        {
            TopicArn = await TopicArnAsync(topic, cancellationToken),
            Message = Convert.ToBase64String(body.Span)
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string topic, string subscription, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        string queueUrl = (await _queues.GetQueueUrlAsync(QueueName(topic, subscription), cancellationToken)).QueueUrl;
        ReceiveMessageResponse response = await _queues.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = maxMessages,
            VisibilityTimeout = Math.Max(1, (int)lockDuration.TotalSeconds),
            WaitTimeSeconds = 1,
            MessageSystemAttributeNames = ["ApproximateReceiveCount"]
        }, cancellationToken);
        return response.Messages.Select(message =>
        {
            _bodies[message.ReceiptHandle] = message.Body;
            return new MessageDelivery(
                message.MessageId,
                message.ReceiptHandle,
                Convert.FromBase64String(message.Body),
                ReadCount(message));
        }).ToArray();
    }

    public async Task CompleteAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        string queueUrl = (await _queues.GetQueueUrlAsync(QueueName(topic, subscription), cancellationToken)).QueueUrl;
        await _queues.DeleteMessageAsync(queueUrl, receipt, cancellationToken);
    }

    public async Task AbandonAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        string queueUrl = (await _queues.GetQueueUrlAsync(QueueName(topic, subscription), cancellationToken)).QueueUrl;
        await _queues.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
        {
            QueueUrl = queueUrl,
            ReceiptHandle = receipt,
            VisibilityTimeout = 0
        }, cancellationToken);
    }

    public async Task DeadLetterAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        string deadLetter = QueueName(topic, subscription) + "-deadletter";
        await _queues.CreateQueueAsync(deadLetter, cancellationToken);
        string deadLetterUrl = (await _queues.GetQueueUrlAsync(deadLetter, cancellationToken)).QueueUrl;
        string queueUrl = (await _queues.GetQueueUrlAsync(QueueName(topic, subscription), cancellationToken)).QueueUrl;
        string body = _bodies.TryGetValue(receipt, out string? cached) ? cached : Convert.ToBase64String(Encoding.UTF8.GetBytes(messageId));
        await _queues.SendMessageAsync(deadLetterUrl, body, cancellationToken);
        await _queues.DeleteMessageAsync(queueUrl, receipt, cancellationToken);
    }

    private async Task<string> TopicArnAsync(string topic, CancellationToken cancellationToken)
    {
        CreateTopicResponse response = await _topics.CreateTopicAsync(topic, cancellationToken);
        return response.TopicArn;
    }

    private async Task<string> QueueArnAsync(string queueUrl, CancellationToken cancellationToken)
    {
        GetQueueAttributesResponse attributes = await _queues.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = queueUrl,
            AttributeNames = ["QueueArn"]
        }, cancellationToken);
        return attributes.QueueARN;
    }

    private static string QueueName(string topic, string subscription)
    {
        return $"{topic}-{subscription}";
    }

    private static string AllowSns(string queueArn, string topicArn)
    {
        return JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new { Service = "sns.amazonaws.com" },
                    Action = "sqs:SendMessage",
                    Resource = queueArn,
                    Condition = new { ArnEquals = new Dictionary<string, string> { ["aws:SourceArn"] = topicArn } }
                }
            }
        });
    }

    private static int ReadCount(Message message)
    {
        if (message.Attributes is not null && message.Attributes.TryGetValue("ApproximateReceiveCount", out string? count) && int.TryParse(count, out int value))
        {
            return value;
        }

        return 1;
    }
}
