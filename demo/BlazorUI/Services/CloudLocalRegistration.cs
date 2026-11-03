using Amazon;
using Amazon.DynamoDBv2;
using Amazon.KeyManagementService;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using BlazorUI.Configuration;
using BlazorUI.Services.Aws;
using BlazorUI.Services.AzureCloud;
using CodeFoxtrot.CloudLocal.Blobs;
using CodeFoxtrot.CloudLocal.Messaging;
using CodeFoxtrot.CloudLocal.Queues;
using CodeFoxtrot.CloudLocal.Tables;
using CodeFoxtrot.CloudLocal.Vault;

namespace BlazorUI.Services;

public static class CloudLocalRegistration
{
    public static void AddCloudLocal(this IServiceCollection services, CloudLocalOptions options, string contentRoot)
    {
        services.AddSingleton(options);

        string root = Path.IsPathRooted(options.RootPath)
            ? options.RootPath
            : Path.Combine(contentRoot, options.RootPath);

        CloudModeState mode = new(options.ProviderMode);
        services.AddSingleton(mode);

        DefaultAzureCredential? credential = HasAzureSettings(options) ? new DefaultAzureCredential() : null;
        IBlobStore? azureBlobs = CreateAzureBlobs(options, credential);
        ITableStore? azureTables = CreateAzureTables(options, credential);
        IQueueStore? azureQueues = CreateAzureQueues(options, credential);
        (ISecretStore? azureSecrets, IKeyStore? azureKeys, ICertificateStore? azureCertificates) = CreateAzureVault(options, credential);
        (IQueueMessenger? azureQueueMessenger, ITopicMessenger? azureTopicMessenger) = CreateAzureMessaging(options, credential);

        IBlobStore? awsBlobs = null;
        ITableStore? awsTables = null;
        IQueueStore? awsQueues = null;
        ISecretStore? awsSecrets = null;
        IKeyStore? awsKeys = null;
        ICertificateStore? awsCertificates = null;
        IQueueMessenger? awsQueueMessenger = null;
        ITopicMessenger? awsTopicMessenger = null;

        if (string.IsNullOrWhiteSpace(options.Aws.Region) == false)
        {
            IAmazonS3 s3 = new AmazonS3Client(S3Config(options.Aws.Region, options.Aws.S3ServiceUrl));
            IAmazonDynamoDB dynamo = new AmazonDynamoDBClient(DynamoConfig(options.Aws.Region, options.Aws.DynamoServiceUrl));
            IAmazonSQS sqs = new AmazonSQSClient(SqsConfig(options.Aws.Region, options.Aws.SqsServiceUrl));
            IAmazonSimpleNotificationService sns = new AmazonSimpleNotificationServiceClient(SnsConfig(options.Aws.Region, options.Aws.SnsServiceUrl));
            awsBlobs = new S3BlobStore(s3);
            awsTables = new DynamoTableStore(dynamo);
            awsQueues = new SqsQueueStore(sqs);
            awsSecrets = new SecretsManagerStore(new AmazonSecretsManagerClient(SecretsConfig(options.Aws.Region, options.Aws.SecretsManagerServiceUrl)));
            awsKeys = new KmsKeyStore(new AmazonKeyManagementServiceClient(KmsConfig(options.Aws.Region, options.Aws.KmsServiceUrl)));
            awsCertificates = new UnsupportedCertificateStore();
            awsQueueMessenger = new SqsQueueMessenger(sqs);
            awsTopicMessenger = new SnsTopicMessenger(sns, sqs);
        }

        services.AddSingleton<IBlobStore>(new SwitchingBlobStore(mode, new FileBlobStore(root), azureBlobs, awsBlobs));
        services.AddSingleton<ITableStore>(new SwitchingTableStore(mode, new FileTableStore(root), azureTables, awsTables));
        services.AddSingleton<IQueueStore>(new SwitchingQueueStore(mode, new FileQueueStore(root), azureQueues, awsQueues));
        services.AddSingleton<ISecretStore>(new SwitchingSecretStore(mode, new FileSecretStore(root), azureSecrets, awsSecrets));
        services.AddSingleton<IKeyStore>(new SwitchingKeyStore(mode, new FileKeyStore(root), azureKeys, awsKeys));
        services.AddSingleton<ICertificateStore>(new SwitchingCertificateStore(mode, new FileCertificateStore(root), azureCertificates, awsCertificates));
        services.AddSingleton<IQueueMessenger>(new SwitchingQueueMessenger(mode, new FileQueueMessenger(root), azureQueueMessenger, awsQueueMessenger));
        services.AddSingleton<ITopicMessenger>(new SwitchingTopicMessenger(mode, new FileTopicMessenger(root), azureTopicMessenger, awsTopicMessenger));
    }

    private static bool HasAzureSettings(CloudLocalOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Azure.BlobServiceUri) == false
            || string.IsNullOrWhiteSpace(options.Azure.TableServiceUri) == false
            || string.IsNullOrWhiteSpace(options.Azure.QueueServiceUri) == false
            || string.IsNullOrWhiteSpace(options.Azure.VaultUri) == false
            || string.IsNullOrWhiteSpace(options.Azure.ServiceBusNamespace) == false;
    }

    private static IBlobStore? CreateAzureBlobs(CloudLocalOptions options, DefaultAzureCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(options.Azure.BlobServiceUri) || credential is null)
        {
            return null;
        }

        return new AzureBlobStore(new BlobServiceClient(new Uri(options.Azure.BlobServiceUri), credential));
    }

    private static ITableStore? CreateAzureTables(CloudLocalOptions options, DefaultAzureCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(options.Azure.TableServiceUri) || credential is null)
        {
            return null;
        }

        return new AzureTableStore(new TableServiceClient(new Uri(options.Azure.TableServiceUri), credential));
    }

    private static IQueueStore? CreateAzureQueues(CloudLocalOptions options, DefaultAzureCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(options.Azure.QueueServiceUri) || credential is null)
        {
            return null;
        }

        return new AzureQueueStore(new QueueServiceClient(new Uri(options.Azure.QueueServiceUri), credential));
    }

    private static (ISecretStore?, IKeyStore?, ICertificateStore?) CreateAzureVault(CloudLocalOptions options, DefaultAzureCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(options.Azure.VaultUri) || credential is null)
        {
            return (null, null, null);
        }

        Uri vault = new(options.Azure.VaultUri);

        return (
            new AzureSecretStore(new SecretClient(vault, credential)),
            new AzureKeyStore(new KeyClient(vault, credential)),
            new AzureCertificateStore(new CertificateClient(vault, credential)));
    }

    private static (IQueueMessenger?, ITopicMessenger?) CreateAzureMessaging(CloudLocalOptions options, DefaultAzureCredential? credential)
    {
        if (string.IsNullOrWhiteSpace(options.Azure.ServiceBusNamespace) || credential is null)
        {
            return (null, null);
        }

        ServiceBusClient queueClient = new(options.Azure.ServiceBusNamespace, credential);
        ServiceBusAdministrationClient queueAdmin = new(options.Azure.ServiceBusNamespace, credential);
        ServiceBusClient topicClient = new(options.Azure.ServiceBusNamespace, credential);
        ServiceBusAdministrationClient topicAdmin = new(options.Azure.ServiceBusNamespace, credential);
        return (new AzureQueueMessenger(queueClient, queueAdmin), new AzureTopicMessenger(topicClient, topicAdmin));
    }

    private static AmazonS3Config S3Config(string region, string? serviceUrl)
    {
        AmazonS3Config config = new() { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };

        if (string.IsNullOrWhiteSpace(serviceUrl) == false)
        {
            config.ServiceURL = serviceUrl;
            config.ForcePathStyle = true;
        }

        return config;
    }

    private static AmazonDynamoDBConfig DynamoConfig(string region, string? serviceUrl)
    {
        AmazonDynamoDBConfig config = new() { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        ApplyServiceUrl(config, serviceUrl);
        return config;
    }

    private static AmazonSQSConfig SqsConfig(string region, string? serviceUrl)
    {
        AmazonSQSConfig config = new() { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        ApplyServiceUrl(config, serviceUrl);
        return config;
    }

    private static AmazonSimpleNotificationServiceConfig SnsConfig(string region, string? serviceUrl)
    {
        AmazonSimpleNotificationServiceConfig config = new() { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        ApplyServiceUrl(config, serviceUrl);
        return config;
    }

    private static AmazonSecretsManagerConfig SecretsConfig(string region, string? serviceUrl)
    {
        AmazonSecretsManagerConfig config = new() { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        ApplyServiceUrl(config, serviceUrl);
        return config;
    }

    private static AmazonKeyManagementServiceConfig KmsConfig(string region, string? serviceUrl)
    {
        AmazonKeyManagementServiceConfig config = new() { RegionEndpoint = RegionEndpoint.GetBySystemName(region) };
        ApplyServiceUrl(config, serviceUrl);
        return config;
    }

    private static void ApplyServiceUrl(Amazon.Runtime.ClientConfig config, string? serviceUrl)
    {
        if (string.IsNullOrWhiteSpace(serviceUrl) == false)
        {
            config.ServiceURL = serviceUrl;
        }
    }
}
