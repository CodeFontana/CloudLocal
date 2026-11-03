namespace BlazorUI.Configuration;

public enum CloudProviderMode
{
    Local,
    Azure,
    Aws
}

public sealed class CloudLocalOptions
{
    public const string SectionName = "CloudLocal";

    public CloudProviderMode ProviderMode { get; set; } = CloudProviderMode.Local;

    public string RootPath { get; set; } = ".cloudlocal";

    public AzureCloudOptions Azure { get; set; } = new();

    public AwsCloudOptions Aws { get; set; } = new();
}

public sealed class AzureCloudOptions
{
    public string BlobServiceUri { get; set; } = "";

    public string TableServiceUri { get; set; } = "";

    public string QueueServiceUri { get; set; } = "";

    public string VaultUri { get; set; } = "";

    public string ServiceBusNamespace { get; set; } = "";
}

public sealed class AwsCloudOptions
{
    public string Region { get; set; } = "";

    public string S3ServiceUrl { get; set; } = "";

    public string DynamoServiceUrl { get; set; } = "";

    public string SqsServiceUrl { get; set; } = "";

    public string SnsServiceUrl { get; set; } = "";

    public string SecretsManagerServiceUrl { get; set; } = "";

    public string KmsServiceUrl { get; set; } = "";
}
