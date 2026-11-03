using System.Security.Cryptography;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using CodeFoxtrot.CloudLocal;

namespace BlazorUI.Services.AzureCloud;

public sealed class AzureSecretStore : CodeFoxtrot.CloudLocal.Vault.ISecretStore
{
    private readonly SecretClient _client;

    public AzureSecretStore(SecretClient client)
    {
        _client = client;
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        await _client.SetSecretAsync(name, value, cancellationToken);
    }

    public async Task<string> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            KeyVaultSecret secret = await _client.GetSecretAsync(name, cancellationToken: cancellationToken);
            return secret.Value;
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Secret '{name}' was not found.", exception);
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.StartDeleteSecretAsync(name, cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Secret '{name}' was not found.", exception);
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<string> names = [];
        await foreach (SecretProperties secret in _client.GetPropertiesOfSecretsAsync(cancellationToken))
        {
            names.Add(secret.Name);
        }

        return names;
    }
}

public sealed class AzureKeyStore : CodeFoxtrot.CloudLocal.Vault.IKeyStore
{
    private readonly KeyClient _client;

    public AzureKeyStore(KeyClient client)
    {
        _client = client;
    }

    public async Task CreateRsaAsync(string name, int keySize = 2048, CancellationToken cancellationToken = default)
    {
        await _client.CreateRsaKeyAsync(new CreateRsaKeyOptions(name) { KeySize = keySize }, cancellationToken);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.StartDeleteKeyAsync(name, cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Key '{name}' was not found.", exception);
        }
    }

    public async Task<byte[]> SignAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        CryptographyClient crypto = _client.GetCryptographyClient(name);
        SignResult result = await crypto.SignDataAsync(SignatureAlgorithm.RS256, data.ToArray(), cancellationToken);
        return result.Signature;
    }

    public async Task<bool> VerifyAsync(string name, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, CancellationToken cancellationToken = default)
    {
        CryptographyClient crypto = _client.GetCryptographyClient(name);
        VerifyResult result = await crypto.VerifyDataAsync(SignatureAlgorithm.RS256, data.ToArray(), signature.ToArray(), cancellationToken);
        return result.IsValid;
    }

    public async Task<byte[]> EncryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        CryptographyClient crypto = _client.GetCryptographyClient(name);
        EncryptResult result = await crypto.EncryptAsync(EncryptionAlgorithm.RsaOaep256, data.ToArray(), cancellationToken);
        return result.Ciphertext;
    }

    public async Task<byte[]> DecryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        CryptographyClient crypto = _client.GetCryptographyClient(name);
        DecryptResult result = await crypto.DecryptAsync(EncryptionAlgorithm.RsaOaep256, data.ToArray(), cancellationToken);
        return result.Plaintext;
    }
}

public sealed class AzureCertificateStore : CodeFoxtrot.CloudLocal.Vault.ICertificateStore
{
    private readonly CertificateClient _client;

    public AzureCertificateStore(CertificateClient client)
    {
        _client = client;
    }

    public async Task CreateSelfSignedAsync(string name, string commonName, CancellationToken cancellationToken = default)
    {
        CertificatePolicy policy = new("Self", $"CN={commonName}")
        {
            KeySize = 2048,
            KeyType = CertificateKeyType.Rsa,
            Exportable = true,
            ContentType = CertificateContentType.Pkcs12
        };
        CertificateOperation operation = await _client.StartCreateCertificateAsync(name, policy, cancellationToken: cancellationToken);
        await operation.WaitForCompletionAsync(cancellationToken);
    }

    public async Task ImportPemAsync(string name, string certificatePem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        using System.Security.Cryptography.X509Certificates.X509Certificate2 certificate = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
        byte[] pkcs12 = certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12);
        await _client.ImportCertificateAsync(new ImportCertificateOptions(name, pkcs12), cancellationToken);
    }

    public async Task ImportPfxAsync(string name, ReadOnlyMemory<byte> pfx, string? password, CancellationToken cancellationToken = default)
    {
        await _client.ImportCertificateAsync(new ImportCertificateOptions(name, pfx.ToArray()) { Password = password }, cancellationToken);
    }

    public async Task<System.Security.Cryptography.X509Certificates.X509Certificate2> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _client.DownloadCertificateAsync(name, cancellationToken: cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Certificate '{name}' was not found.", exception);
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.StartDeleteCertificateAsync(name, cancellationToken);
        }
        catch (Azure.RequestFailedException exception) when (exception.Status == 404)
        {
            throw new CloudLocalNotFoundException($"Certificate '{name}' was not found.", exception);
        }
    }
}
