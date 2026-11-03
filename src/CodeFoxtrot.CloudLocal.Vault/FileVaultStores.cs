using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CodeFoxtrot.CloudLocal.Vault;

public sealed class FileSecretStore : ISecretStore
{
    private readonly string _root;

    public FileSecretStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    public Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AtomicFile.WriteTextAsync(PathFor(name), value, cancellationToken);
    }

    public async Task<string> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        string path = PathFor(name);

        if (!File.Exists(path))
        {
            throw new CloudLocalNotFoundException($"Secret '{name}' was not found.");
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathFor(name);

        if (!File.Exists(path))
        {
            throw new CloudLocalNotFoundException($"Secret '{name}' was not found.");
        }

        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = LocalPath.Child(_root, "vault", "secrets");

        if (!Directory.Exists(directory))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        List<string> names = [];

        foreach (string file in Directory.GetFiles(directory, "*.txt").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string encoded = Path.GetFileNameWithoutExtension(file);
            names.Add(NameEncoder.Decode(encoded));
        }

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private string PathFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return LocalPath.Child(_root, "vault", "secrets", NameEncoder.Encode(name) + ".txt");
    }
}

public sealed class FileKeyStore : IKeyStore
{
    private readonly string _root;

    public FileKeyStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    public async Task CreateRsaAsync(string name, int keySize = 2048, CancellationToken cancellationToken = default)
    {
        if (keySize is not (2048 or 3072 or 4096))
        {
            throw new ArgumentOutOfRangeException(nameof(keySize), "RSA key size must be 2048, 3072, or 4096.");
        }

        string path = PathFor(name);

        if (File.Exists(path))
        {
            throw new InvalidOperationException($"Key '{name}' already exists.");
        }

        using RSA rsa = RSA.Create(keySize);
        await AtomicFile.WriteTextAsync(path, rsa.ExportPkcs8PrivateKeyPem(), cancellationToken);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = PathFor(name);

        if (!File.Exists(path))
        {
            throw new CloudLocalNotFoundException($"Key '{name}' was not found.");
        }

        File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<byte[]> SignAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        using RSA rsa = await LoadAsync(name, cancellationToken);
        return rsa.SignData(data.Span, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public async Task<bool> VerifyAsync(string name, ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, CancellationToken cancellationToken = default)
    {
        using RSA rsa = await LoadAsync(name, cancellationToken);
        return rsa.VerifyData(data.Span, signature.Span, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public async Task<byte[]> EncryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        using RSA rsa = await LoadAsync(name, cancellationToken);
        return rsa.Encrypt(data.Span, RSAEncryptionPadding.OaepSHA256);
    }

    public async Task<byte[]> DecryptAsync(string name, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        using RSA rsa = await LoadAsync(name, cancellationToken);
        return rsa.Decrypt(data.Span, RSAEncryptionPadding.OaepSHA256);
    }

    private async Task<RSA> LoadAsync(string name, CancellationToken cancellationToken)
    {
        string path = PathFor(name);

        if (!File.Exists(path))
        {
            throw new CloudLocalNotFoundException($"Key '{name}' was not found.");
        }

        string pem = await File.ReadAllTextAsync(path, cancellationToken);
        RSA rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private string PathFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return LocalPath.Child(_root, "vault", "keys", NameEncoder.Encode(name) + ".pem");
    }
}

public sealed class FileCertificateStore : ICertificateStore
{
    private readonly string _root;

    public FileCertificateStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    public async Task CreateSelfSignedAsync(string name, string commonName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commonName);
        (string certificatePath, string keyPath) = PathsFor(name);

        if (File.Exists(certificatePath) || File.Exists(keyPath))
        {
            throw new InvalidOperationException($"Certificate '{name}' already exists.");
        }

        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(new X500DistinguishedName($"CN={commonName}"), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        await WritePairAsync(certificatePath, keyPath, certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), cancellationToken);
    }

    public async Task ImportPemAsync(string name, string certificatePem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePem);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        using X509Certificate2 parsed = CertificateMaterial.LoadPem(certificatePem, privateKeyPem);
        (string certificatePath, string keyPath) = PathsFor(name);
        await WritePairAsync(certificatePath, keyPath, certificatePem, privateKeyPem, cancellationToken);
    }

    public async Task ImportPfxAsync(string name, ReadOnlyMemory<byte> pfx, string? password, CancellationToken cancellationToken = default)
    {
        using X509Certificate2 certificate = CertificateMaterial.LoadPfx(pfx, password);
        using RSA? key = certificate.GetRSAPrivateKey();

        if (key is null)
        {
            throw new InvalidOperationException($"Certificate '{name}' does not contain an RSA private key.");
        }

        (string certificatePath, string keyPath) = PathsFor(name);
        await WritePairAsync(certificatePath, keyPath, certificate.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem(), cancellationToken);
    }

    public Task<X509Certificate2> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (string certificatePath, string keyPath) = PathsFor(name);

        if (!File.Exists(certificatePath) || !File.Exists(keyPath))
        {
            throw new CloudLocalNotFoundException($"Certificate '{name}' was not found.");
        }

        string certificatePem = File.ReadAllText(certificatePath);
        string keyPem = File.ReadAllText(keyPath);
        return Task.FromResult(CertificateMaterial.LoadPem(certificatePem, keyPem));
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (string certificatePath, string keyPath) = PathsFor(name);

        if (!File.Exists(certificatePath) && !File.Exists(keyPath))
        {
            throw new CloudLocalNotFoundException($"Certificate '{name}' was not found.");
        }

        if (File.Exists(certificatePath))
        {
            File.Delete(certificatePath);
        }

        if (File.Exists(keyPath))
        {
            File.Delete(keyPath);
        }

        return Task.CompletedTask;
    }

    private static async Task WritePairAsync(string certificatePath, string keyPath, string certificatePem, string keyPem, CancellationToken cancellationToken)
    {
        await AtomicFile.WriteTextAsync(certificatePath, certificatePem, cancellationToken);
        await AtomicFile.WriteTextAsync(keyPath, keyPem, cancellationToken);
    }

    private (string CertificatePath, string KeyPath) PathsFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string encoded = NameEncoder.Encode(name);
        return (
            LocalPath.Child(_root, "vault", "certificates", encoded + ".crt"),
            LocalPath.Child(_root, "vault", "certificates", encoded + ".key"));
    }
}

internal static class CertificateMaterial
{
    public static X509Certificate2 LoadPem(string certificatePem, string privateKeyPem)
    {
        using X509Certificate2 ephemeral = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
        byte[] pkcs12 = ephemeral.Export(X509ContentType.Pkcs12);
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pkcs12, password: null, X509KeyStorageFlags.Exportable);
#else
        return new X509Certificate2(pkcs12, (string?)null, X509KeyStorageFlags.Exportable);
#endif
    }

    public static X509Certificate2 LoadPfx(ReadOnlyMemory<byte> pfx, string? password)
    {
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfx.Span, password, X509KeyStorageFlags.Exportable);
#else
        return new X509Certificate2(pfx.Span, password, X509KeyStorageFlags.Exportable);
#endif
    }
}
