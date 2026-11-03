using System.Security.Cryptography.X509Certificates;

namespace CodeFoxtrot.CloudLocal.Vault;

public interface ISecretStore
{
    Task SetAsync(string name, string value, CancellationToken cancellationToken = default);

    Task<string> GetAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IKeyStore
{
    Task CreateRsaAsync(string name, int keySize = 2048, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    Task<byte[]> SignAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(string name, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, CancellationToken cancellationToken = default);

    Task<byte[]> EncryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    Task<byte[]> DecryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}

public interface ICertificateStore
{
    Task CreateSelfSignedAsync(string name, string commonName, CancellationToken cancellationToken = default);

    Task ImportPemAsync(string name, string certificatePem, string privateKeyPem, CancellationToken cancellationToken = default);

    Task ImportPfxAsync(string name, ReadOnlyMemory<byte> pfx, string? password, CancellationToken cancellationToken = default);

    Task<X509Certificate2> LoadAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
