using Amazon.SQS;
using Amazon.SQS.Model;
using CodeFoxtrot.CloudLocal;
using CloudQueueMessage = CodeFoxtrot.CloudLocal.Queues.QueueMessage;

namespace BlazorUI.Services.Aws;

public sealed class SqsQueueStore : CodeFoxtrot.CloudLocal.Queues.IQueueStore
{
    private const int MaxReceives = 5;
    private readonly IAmazonSQS _client;

    public SqsQueueStore(IAmazonSQS client)
    {
        _client = client;
    }

    public async Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default)
    {
        await _client.CreateQueueAsync(queue, cancellationToken);
        await _client.CreateQueueAsync(queue + "-poison", cancellationToken);
    }

    public async Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        await _client.SendMessageAsync(await UrlAsync(queue, cancellationToken), Convert.ToBase64String(body.Span), cancellationToken);
    }

    public async Task<IReadOnlyList<CloudQueueMessage>> ReceiveAsync(string queue, TimeSpan visibilityTimeout, int maxMessages = 1, CancellationToken cancellationToken = default)
    {
        ReceiveMessageResponse response = await _client.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = await UrlAsync(queue, cancellationToken),
            MaxNumberOfMessages = maxMessages,
            VisibilityTimeout = Math.Max(1, (int)visibilityTimeout.TotalSeconds),
            MessageSystemAttributeNames = ["ApproximateReceiveCount"]
        }, cancellationToken);
        List<CloudQueueMessage> messages = [];
        foreach (Message message in response.Messages)
        {
            int count = ReadCount(message);
            byte[] body = Convert.FromBase64String(message.Body);
            if (count > MaxReceives)
            {
                await _client.SendMessageAsync(await UrlAsync(queue + "-poison", cancellationToken), message.Body, cancellationToken);
                await _client.DeleteMessageAsync(await UrlAsync(queue, cancellationToken), message.ReceiptHandle, cancellationToken);
                continue;
            }

            messages.Add(new CloudQueueMessage(message.MessageId, message.ReceiptHandle, body, count));
        }

        return messages;
    }

    public async Task DeleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default)
    {
        await _client.DeleteMessageAsync(await UrlAsync(queue, cancellationToken), receipt, cancellationToken);
    }

    public async Task<IReadOnlyList<CloudQueueMessage>> ListPoisonAsync(string queue, CancellationToken cancellationToken = default)
    {
        ReceiveMessageResponse response = await _client.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = await UrlAsync(queue + "-poison", cancellationToken),
            MaxNumberOfMessages = 10,
            VisibilityTimeout = 0,
            MessageSystemAttributeNames = ["ApproximateReceiveCount"]
        }, cancellationToken);
        return response.Messages.Select(message => new CloudQueueMessage(
            message.MessageId,
            message.ReceiptHandle,
            Convert.FromBase64String(message.Body),
            ReadCount(message))).ToArray();
    }

    private async Task<string> UrlAsync(string queue, CancellationToken cancellationToken)
    {
        GetQueueUrlResponse response = await _client.GetQueueUrlAsync(queue, cancellationToken);
        return response.QueueUrl;
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

public sealed class SecretsManagerStore : CodeFoxtrot.CloudLocal.Vault.ISecretStore
{
    private readonly Amazon.SecretsManager.IAmazonSecretsManager _client;

    public SecretsManagerStore(Amazon.SecretsManager.IAmazonSecretsManager client)
    {
        _client = client;
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.CreateSecretAsync(new Amazon.SecretsManager.Model.CreateSecretRequest
            {
                Name = name,
                SecretString = value
            }, cancellationToken);
        }
        catch (Amazon.SecretsManager.Model.ResourceExistsException)
        {
            await _client.PutSecretValueAsync(new Amazon.SecretsManager.Model.PutSecretValueRequest
            {
                SecretId = name,
                SecretString = value
            }, cancellationToken);
        }
    }

    public async Task<string> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            Amazon.SecretsManager.Model.GetSecretValueResponse response = await _client.GetSecretValueAsync(new Amazon.SecretsManager.Model.GetSecretValueRequest
            {
                SecretId = name
            }, cancellationToken);
            return response.SecretString;
        }
        catch (Amazon.SecretsManager.Model.ResourceNotFoundException exception)
        {
            throw new CloudLocalNotFoundException($"Secret '{name}' was not found.", exception);
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.DeleteSecretAsync(new Amazon.SecretsManager.Model.DeleteSecretRequest
            {
                SecretId = name,
                ForceDeleteWithoutRecovery = true
            }, cancellationToken);
        }
        catch (Amazon.SecretsManager.Model.ResourceNotFoundException exception)
        {
            throw new CloudLocalNotFoundException($"Secret '{name}' was not found.", exception);
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        Amazon.SecretsManager.Model.ListSecretsResponse response = await _client.ListSecretsAsync(new Amazon.SecretsManager.Model.ListSecretsRequest(), cancellationToken);
        return response.SecretList.Select(secret => secret.Name).ToArray();
    }
}

public sealed class KmsKeyStore : CodeFoxtrot.CloudLocal.Vault.IKeyStore
{
    private readonly Amazon.KeyManagementService.IAmazonKeyManagementService _client;

    public KmsKeyStore(Amazon.KeyManagementService.IAmazonKeyManagementService client)
    {
        _client = client;
    }

    public async Task CreateRsaAsync(string name, int keySize = 2048, CancellationToken cancellationToken = default)
    {
        await CreateAsync(name, "sign", Amazon.KeyManagementService.KeyUsageType.SIGN_VERIFY, cancellationToken);
        await CreateAsync(name, "crypt", Amazon.KeyManagementService.KeyUsageType.ENCRYPT_DECRYPT, cancellationToken);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        await ScheduleAsync(Alias(name, "sign"), cancellationToken);
        await ScheduleAsync(Alias(name, "crypt"), cancellationToken);
    }

    public async Task<byte[]> SignAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Amazon.KeyManagementService.Model.SignResponse response = await _client.SignAsync(new Amazon.KeyManagementService.Model.SignRequest
        {
            KeyId = Alias(name, "sign"),
            Message = new MemoryStream(data.ToArray()),
            MessageType = Amazon.KeyManagementService.MessageType.RAW,
            SigningAlgorithm = Amazon.KeyManagementService.SigningAlgorithmSpec.RSASSA_PKCS1_V1_5_SHA_256
        }, cancellationToken);
        return response.Signature.ToArray();
    }

    public async Task<bool> VerifyAsync(string name, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, CancellationToken cancellationToken = default)
    {
        Amazon.KeyManagementService.Model.VerifyResponse response = await _client.VerifyAsync(new Amazon.KeyManagementService.Model.VerifyRequest
        {
            KeyId = Alias(name, "sign"),
            Message = new MemoryStream(data.ToArray()),
            MessageType = Amazon.KeyManagementService.MessageType.RAW,
            Signature = new MemoryStream(signature.ToArray()),
            SigningAlgorithm = Amazon.KeyManagementService.SigningAlgorithmSpec.RSASSA_PKCS1_V1_5_SHA_256
        }, cancellationToken);
        return response.SignatureValid ?? false;
    }

    public async Task<byte[]> EncryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Amazon.KeyManagementService.Model.EncryptResponse response = await _client.EncryptAsync(new Amazon.KeyManagementService.Model.EncryptRequest
        {
            KeyId = Alias(name, "crypt"),
            Plaintext = new MemoryStream(data.ToArray()),
            EncryptionAlgorithm = Amazon.KeyManagementService.EncryptionAlgorithmSpec.RSAES_OAEP_SHA_256
        }, cancellationToken);
        return response.CiphertextBlob.ToArray();
    }

    public async Task<byte[]> DecryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Amazon.KeyManagementService.Model.DecryptResponse response = await _client.DecryptAsync(new Amazon.KeyManagementService.Model.DecryptRequest
        {
            KeyId = Alias(name, "crypt"),
            CiphertextBlob = new MemoryStream(data.ToArray()),
            EncryptionAlgorithm = Amazon.KeyManagementService.EncryptionAlgorithmSpec.RSAES_OAEP_SHA_256
        }, cancellationToken);
        return response.Plaintext.ToArray();
    }

    private async Task CreateAsync(string name, string purpose, Amazon.KeyManagementService.KeyUsageType usage, CancellationToken cancellationToken)
    {
        Amazon.KeyManagementService.Model.CreateKeyResponse created = await _client.CreateKeyAsync(new Amazon.KeyManagementService.Model.CreateKeyRequest
        {
            Description = name,
            KeyUsage = usage,
            KeySpec = Amazon.KeyManagementService.KeySpec.RSA_2048
        }, cancellationToken);
        await _client.CreateAliasAsync(new Amazon.KeyManagementService.Model.CreateAliasRequest
        {
            AliasName = Alias(name, purpose),
            TargetKeyId = created.KeyMetadata.KeyId
        }, cancellationToken);
    }

    private async Task ScheduleAsync(string alias, CancellationToken cancellationToken)
    {
        try
        {
            await _client.ScheduleKeyDeletionAsync(new Amazon.KeyManagementService.Model.ScheduleKeyDeletionRequest
            {
                KeyId = alias,
                PendingWindowInDays = 7
            }, cancellationToken);
        }
        catch (Amazon.KeyManagementService.Model.NotFoundException exception)
        {
            throw new CloudLocalNotFoundException($"Key '{alias}' was not found.", exception);
        }
    }

    private static string Alias(string name, string purpose)
    {
        return $"alias/cloudlocal-{name}-{purpose}";
    }
}

public sealed class UnsupportedCertificateStore : CodeFoxtrot.CloudLocal.Vault.ICertificateStore
{
    private const string Reason = "This demo has no AWS private-certificate backend.";

    public Task CreateSelfSignedAsync(string name, string commonName, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(Reason);
    }

    public Task ImportPemAsync(string name, string certificatePem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(Reason);
    }

    public Task ImportPfxAsync(string name, ReadOnlyMemory<byte> pfx, string? password, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(Reason);
    }

    public Task<System.Security.Cryptography.X509Certificates.X509Certificate2> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(Reason);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(Reason);
    }
}
