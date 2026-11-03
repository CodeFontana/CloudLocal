using System.Security.Cryptography.X509Certificates;
using BlazorUI.Configuration;
using CodeFoxtrot.CloudLocal.Blobs;
using CodeFoxtrot.CloudLocal.Messaging;
using CodeFoxtrot.CloudLocal.Queues;
using CodeFoxtrot.CloudLocal.Tables;
using CodeFoxtrot.CloudLocal.Vault;

namespace BlazorUI.Services;

internal static class ProviderSelection
{
    public static T Pick<T>(CloudModeState mode, T local, T? azure, T? aws, string azureSetting, string awsSetting)
        where T : class
    {
        return mode.Current switch
        {
            CloudProviderMode.Local => local,
            CloudProviderMode.Azure => azure ?? throw new InvalidOperationException($"Setting '{azureSetting}' is required to use Azure."),
            CloudProviderMode.Aws => aws ?? throw new InvalidOperationException($"Setting '{awsSetting}' is required to use AWS."),
            _ => throw new InvalidOperationException($"Provider '{mode.Current}' is not supported.")
        };
    }
}

public sealed class SwitchingBlobStore : IBlobStore
{
    private readonly CloudModeState _mode;
    private readonly IBlobStore _local;
    private readonly IBlobStore? _azure;
    private readonly IBlobStore? _aws;

    public SwitchingBlobStore(CloudModeState mode, IBlobStore local, IBlobStore? azure, IBlobStore? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private IBlobStore Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:BlobServiceUri", "CloudLocal:Aws:Region");

    public Task CreateContainerAsync(string container, CancellationToken cancellationToken = default) => Current.CreateContainerAsync(container, cancellationToken);
    public Task DeleteContainerAsync(string container, CancellationToken cancellationToken = default) => Current.DeleteContainerAsync(container, cancellationToken);
    public IAsyncEnumerable<string> ListContainersAsync(CancellationToken cancellationToken = default) => Current.ListContainersAsync(cancellationToken);
    public Task PutAsync(string container, string name, Stream content, BlobWriteOptions? options = null, CancellationToken cancellationToken = default) => Current.PutAsync(container, name, content, options, cancellationToken);
    public Task AppendAsync(string container, string name, Stream content, CancellationToken cancellationToken = default) => Current.AppendAsync(container, name, content, cancellationToken);
    public Task<BlobRead> OpenReadAsync(string container, string name, CancellationToken cancellationToken = default) => Current.OpenReadAsync(container, name, cancellationToken);
    public Task<bool> ExistsAsync(string container, string name, CancellationToken cancellationToken = default) => Current.ExistsAsync(container, name, cancellationToken);
    public Task DeleteAsync(string container, string name, CancellationToken cancellationToken = default) => Current.DeleteAsync(container, name, cancellationToken);
    public IAsyncEnumerable<BlobItem> ListAsync(string container, string? prefix = null, CancellationToken cancellationToken = default) => Current.ListAsync(container, prefix, cancellationToken);
}

public sealed class SwitchingTableStore : ITableStore
{
    private readonly CloudModeState _mode;
    private readonly ITableStore _local;
    private readonly ITableStore? _azure;
    private readonly ITableStore? _aws;

    public SwitchingTableStore(CloudModeState mode, ITableStore local, ITableStore? azure, ITableStore? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private ITableStore Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:TableServiceUri", "CloudLocal:Aws:Region");

    public Task CreateTableAsync(string table, CancellationToken cancellationToken = default) => Current.CreateTableAsync(table, cancellationToken);
    public Task DeleteTableAsync(string table, CancellationToken cancellationToken = default) => Current.DeleteTableAsync(table, cancellationToken);
    public Task UpsertAsync(string table, TableEntity entity, CancellationToken cancellationToken = default) => Current.UpsertAsync(table, entity, cancellationToken);
    public Task<TableEntity> GetAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default) => Current.GetAsync(table, partitionKey, rowKey, cancellationToken);
    public Task DeleteAsync(string table, string partitionKey, string rowKey, CancellationToken cancellationToken = default) => Current.DeleteAsync(table, partitionKey, rowKey, cancellationToken);
    public IAsyncEnumerable<TableEntity> QueryAsync(string table, string partitionKey, string? rowKey = null, CancellationToken cancellationToken = default) => Current.QueryAsync(table, partitionKey, rowKey, cancellationToken);
}

public sealed class SwitchingQueueStore : IQueueStore
{
    private readonly CloudModeState _mode;
    private readonly IQueueStore _local;
    private readonly IQueueStore? _azure;
    private readonly IQueueStore? _aws;

    public SwitchingQueueStore(CloudModeState mode, IQueueStore local, IQueueStore? azure, IQueueStore? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private IQueueStore Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:QueueServiceUri", "CloudLocal:Aws:Region");

    public Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default) => Current.CreateQueueAsync(queue, cancellationToken);
    public Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) => Current.SendAsync(queue, body, cancellationToken);
    public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queue, TimeSpan visibilityTimeout, int maxMessages = 1, CancellationToken cancellationToken = default) => Current.ReceiveAsync(queue, visibilityTimeout, maxMessages, cancellationToken);
    public Task DeleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default) => Current.DeleteAsync(queue, messageId, receipt, cancellationToken);
    public Task<IReadOnlyList<QueueMessage>> ListPoisonAsync(string queue, CancellationToken cancellationToken = default) => Current.ListPoisonAsync(queue, cancellationToken);
}

public sealed class SwitchingSecretStore : ISecretStore
{
    private readonly CloudModeState _mode;
    private readonly ISecretStore _local;
    private readonly ISecretStore? _azure;
    private readonly ISecretStore? _aws;

    public SwitchingSecretStore(CloudModeState mode, ISecretStore local, ISecretStore? azure, ISecretStore? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private ISecretStore Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:VaultUri", "CloudLocal:Aws:Region");

    public Task SetAsync(string name, string value, CancellationToken cancellationToken = default) => Current.SetAsync(name, value, cancellationToken);
    public Task<string> GetAsync(string name, CancellationToken cancellationToken = default) => Current.GetAsync(name, cancellationToken);
    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Current.DeleteAsync(name, cancellationToken);
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) => Current.ListAsync(cancellationToken);
}

public sealed class SwitchingKeyStore : IKeyStore
{
    private readonly CloudModeState _mode;
    private readonly IKeyStore _local;
    private readonly IKeyStore? _azure;
    private readonly IKeyStore? _aws;

    public SwitchingKeyStore(CloudModeState mode, IKeyStore local, IKeyStore? azure, IKeyStore? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private IKeyStore Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:VaultUri", "CloudLocal:Aws:Region");

    public Task CreateRsaAsync(string name, int keySize = 2048, CancellationToken cancellationToken = default) => Current.CreateRsaAsync(name, keySize, cancellationToken);
    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Current.DeleteAsync(name, cancellationToken);
    public Task<byte[]> SignAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Current.SignAsync(name, data, cancellationToken);
    public Task<bool> VerifyAsync(string name, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, CancellationToken cancellationToken = default) => Current.VerifyAsync(name, data, signature, cancellationToken);
    public Task<byte[]> EncryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Current.EncryptAsync(name, data, cancellationToken);
    public Task<byte[]> DecryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Current.DecryptAsync(name, data, cancellationToken);
}

public sealed class SwitchingCertificateStore : ICertificateStore
{
    private readonly CloudModeState _mode;
    private readonly ICertificateStore _local;
    private readonly ICertificateStore? _azure;
    private readonly ICertificateStore? _aws;

    public SwitchingCertificateStore(CloudModeState mode, ICertificateStore local, ICertificateStore? azure, ICertificateStore? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private ICertificateStore Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:VaultUri", "CloudLocal:Aws:Region");

    public Task CreateSelfSignedAsync(string name, string commonName, CancellationToken cancellationToken = default) => Current.CreateSelfSignedAsync(name, commonName, cancellationToken);
    public Task ImportPemAsync(string name, string certificatePem, string privateKeyPem, CancellationToken cancellationToken = default) => Current.ImportPemAsync(name, certificatePem, privateKeyPem, cancellationToken);
    public Task ImportPfxAsync(string name, ReadOnlyMemory<byte> pfx, string? password, CancellationToken cancellationToken = default) => Current.ImportPfxAsync(name, pfx, password, cancellationToken);
    public Task<X509Certificate2> LoadAsync(string name, CancellationToken cancellationToken = default) => Current.LoadAsync(name, cancellationToken);
    public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Current.DeleteAsync(name, cancellationToken);
}

public sealed class SwitchingQueueMessenger : IQueueMessenger
{
    private readonly CloudModeState _mode;
    private readonly IQueueMessenger _local;
    private readonly IQueueMessenger? _azure;
    private readonly IQueueMessenger? _aws;

    public SwitchingQueueMessenger(CloudModeState mode, IQueueMessenger local, IQueueMessenger? azure, IQueueMessenger? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private IQueueMessenger Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:ServiceBusNamespace", "CloudLocal:Aws:Region");

    public Task CreateQueueAsync(string queue, CancellationToken cancellationToken = default) => Current.CreateQueueAsync(queue, cancellationToken);
    public Task SendAsync(string queue, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) => Current.SendAsync(queue, body, cancellationToken);
    public Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string queue, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default) => Current.ReceiveAsync(queue, lockDuration, maxMessages, cancellationToken);
    public Task CompleteAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default) => Current.CompleteAsync(queue, messageId, receipt, cancellationToken);
    public Task AbandonAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default) => Current.AbandonAsync(queue, messageId, receipt, cancellationToken);
    public Task DeadLetterAsync(string queue, string messageId, string receipt, CancellationToken cancellationToken = default) => Current.DeadLetterAsync(queue, messageId, receipt, cancellationToken);
}

public sealed class SwitchingTopicMessenger : ITopicMessenger
{
    private readonly CloudModeState _mode;
    private readonly ITopicMessenger _local;
    private readonly ITopicMessenger? _azure;
    private readonly ITopicMessenger? _aws;

    public SwitchingTopicMessenger(CloudModeState mode, ITopicMessenger local, ITopicMessenger? azure, ITopicMessenger? aws)
    {
        _mode = mode;
        _local = local;
        _azure = azure;
        _aws = aws;
    }

    private ITopicMessenger Current => ProviderSelection.Pick(_mode, _local, _azure, _aws, "CloudLocal:Azure:ServiceBusNamespace", "CloudLocal:Aws:Region");

    public Task CreateTopicAsync(string topic, CancellationToken cancellationToken = default) => Current.CreateTopicAsync(topic, cancellationToken);
    public Task CreateSubscriptionAsync(string topic, string subscription, CancellationToken cancellationToken = default) => Current.CreateSubscriptionAsync(topic, subscription, cancellationToken);
    public Task PublishAsync(string topic, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default) => Current.PublishAsync(topic, body, cancellationToken);
    public Task<IReadOnlyList<MessageDelivery>> ReceiveAsync(string topic, string subscription, TimeSpan lockDuration, int maxMessages = 1, CancellationToken cancellationToken = default) => Current.ReceiveAsync(topic, subscription, lockDuration, maxMessages, cancellationToken);
    public Task CompleteAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default) => Current.CompleteAsync(topic, subscription, messageId, receipt, cancellationToken);
    public Task AbandonAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default) => Current.AbandonAsync(topic, subscription, messageId, receipt, cancellationToken);
    public Task DeadLetterAsync(string topic, string subscription, string messageId, string receipt, CancellationToken cancellationToken = default) => Current.DeadLetterAsync(topic, subscription, messageId, receipt, cancellationToken);
}
