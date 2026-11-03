using System.Text;
using Xunit;
using CodeFoxtrot.CloudLocal;
using CodeFoxtrot.CloudLocal.Blobs;
using CodeFoxtrot.CloudLocal.Messaging;
using CodeFoxtrot.CloudLocal.Queues;
using CodeFoxtrot.CloudLocal.Tables;
using CodeFoxtrot.CloudLocal.Vault;

namespace CloudLocal.Tests;

public sealed class LocalStoreTests
{
    [Fact]
    public void Path_rejects_parent_segment()
    {
        using TempRoot temp = new();
        ArgumentException exception = Assert.Throws<ArgumentException>(() => LocalPath.Child(temp.Root, "..", "outside"));
        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Name_encoder_roundtrips_reserved_characters()
    {
        Assert.Equal("a/b key", NameEncoder.Decode(NameEncoder.Encode("a/b key")));
        Assert.Equal(string.Empty, NameEncoder.Decode(NameEncoder.Encode(string.Empty)));
    }

    [Fact]
    public async Task Blob_put_append_and_read_roundtrip()
    {
        using TempRoot temp = new();
        FileBlobStore store = new(temp.Root);
        await store.CreateContainerAsync("demo");
        await store.PutAsync("demo", "hello.txt", Text("hello"), new BlobWriteOptions
        {
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string> { ["owner"] = "demo" }
        });
        await store.AppendAsync("demo", "hello.txt", Text(" world"));
        await store.AppendAsync("demo", "notes/new.txt", Text("created"));

        await using BlobRead hello = await store.OpenReadAsync("demo", "hello.txt");
        using StreamReader reader = new(hello.Content, Encoding.UTF8);
        Assert.Equal("hello world", await reader.ReadToEndAsync());
        Assert.Equal("text/plain", hello.ContentType);
        Assert.Equal("demo", hello.Metadata["owner"]);

        await using BlobRead created = await store.OpenReadAsync("demo", "notes/new.txt");
        using StreamReader createdReader = new(created.Content, Encoding.UTF8);
        Assert.Equal("created", await createdReader.ReadToEndAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync("demo", "../escape.txt", Text("no")));
    }

    [Fact]
    public async Task Table_query_returns_one_partition()
    {
        using TempRoot temp = new();
        FileTableStore store = new(temp.Root);
        await store.CreateTableAsync("records");
        await store.UpsertAsync("records", Entity("a", "1", "one"));
        await store.UpsertAsync("records", Entity("a", "2", "two"));
        await store.UpsertAsync("records", Entity("b", "1", "other"));

        List<TableEntity> partition = [];
        await foreach (TableEntity entity in store.QueryAsync("records", "a"))
        {
            partition.Add(entity);
        }

        Assert.Equal(2, partition.Count);
        Assert.Contains(partition, entity => entity.RowKey == "1" && entity.Properties["label"].String == "one");

        List<TableEntity> exact = [];
        await foreach (TableEntity entity in store.QueryAsync("records", "a", "2"))
        {
            exact.Add(entity);
        }

        Assert.Single(exact);
        Assert.Equal(7L, exact[0].Properties["count"].Integer);
    }

    [Fact]
    public async Task Queue_claims_are_exclusive_and_poison_after_limit()
    {
        using TempRoot temp = new();
        ManualTimeProvider time = new();
        FileQueueStore first = new(temp.Root, time, maxReceives: 5);
        FileQueueStore second = new(temp.Root, time, maxReceives: 5);
        await first.CreateQueueAsync("jobs");
        await first.SendAsync("jobs", TextBytes("one"));
        await first.SendAsync("jobs", TextBytes("two"));

        Task<IReadOnlyList<QueueMessage>> leftTask = first.ReceiveAsync("jobs", TimeSpan.FromMinutes(1), 1);
        Task<IReadOnlyList<QueueMessage>> rightTask = second.ReceiveAsync("jobs", TimeSpan.FromMinutes(1), 1);
        IReadOnlyList<QueueMessage>[] claimed = await Task.WhenAll(leftTask, rightTask);
        IReadOnlyList<QueueMessage> left = claimed[0];
        IReadOnlyList<QueueMessage> right = claimed[1];
        Assert.Single(left);
        Assert.Single(right);
        Assert.NotEqual(left[0].MessageId, right[0].MessageId);

        await first.DeleteAsync("jobs", left[0].MessageId, left[0].Receipt);
        await second.DeleteAsync("jobs", right[0].MessageId, right[0].Receipt);

        await first.SendAsync("jobs", TextBytes("again"));
        IReadOnlyList<QueueMessage> held = await first.ReceiveAsync("jobs", TimeSpan.FromSeconds(10), 1);
        Assert.Empty(await first.ReceiveAsync("jobs", TimeSpan.FromSeconds(10), 1));
        time.UtcNow = time.UtcNow.AddSeconds(11);
        IReadOnlyList<QueueMessage> again = await first.ReceiveAsync("jobs", TimeSpan.FromSeconds(10), 1);
        Assert.Equal(2, again[0].DequeueCount);
        Assert.Equal(held[0].MessageId, again[0].MessageId);

        time.UtcNow = time.UtcNow.AddSeconds(11);
        for (int count = 0; count < 3; count++)
        {
            IReadOnlyList<QueueMessage> batch = await first.ReceiveAsync("jobs", TimeSpan.FromSeconds(10), 1);
            Assert.Single(batch);
            time.UtcNow = time.UtcNow.AddSeconds(11);
        }

        Assert.Empty(await first.ReceiveAsync("jobs", TimeSpan.FromSeconds(10), 1));
        IReadOnlyList<QueueMessage> poison = await first.ListPoisonAsync("jobs");
        Assert.Single(poison);
    }

    [Fact]
    public async Task Topic_publish_waits_for_subscriptions_without_duplicating()
    {
        using TempRoot temp = new();
        FileTopicMessenger topics = new(temp.Root);
        await topics.CreateTopicAsync("events");
        await topics.PublishAsync("events", TextBytes("held"));
        await topics.CreateSubscriptionAsync("events", "a");
        await topics.CreateSubscriptionAsync("events", "a");
        await topics.CreateSubscriptionAsync("events", "b");

        IReadOnlyList<MessageDelivery> heldOnA = await topics.ReceiveAsync("events", "a", TimeSpan.FromSeconds(30), 2);
        IReadOnlyList<MessageDelivery> heldOnB = await topics.ReceiveAsync("events", "b", TimeSpan.FromSeconds(30), 2);
        Assert.Single(heldOnA);
        Assert.Single(heldOnB);
        Assert.Equal("held", Encoding.UTF8.GetString(heldOnA[0].Body));
        Assert.Equal("held", Encoding.UTF8.GetString(heldOnB[0].Body));
        await topics.CompleteAsync("events", "a", heldOnA[0].MessageId, heldOnA[0].Receipt);
        await topics.AbandonAsync("events", "b", heldOnB[0].MessageId, heldOnB[0].Receipt);
        IReadOnlyList<MessageDelivery> abandoned = await topics.ReceiveAsync("events", "b", TimeSpan.FromSeconds(30), 1);
        Assert.Equal(heldOnB[0].MessageId, abandoned[0].MessageId);
        await topics.DeadLetterAsync("events", "b", abandoned[0].MessageId, abandoned[0].Receipt);
        Assert.Empty(await topics.ReceiveAsync("events", "b", TimeSpan.FromSeconds(30), 1));

        await topics.PublishAsync("events", TextBytes("hello"));
        IReadOnlyList<MessageDelivery> helloOnA = await topics.ReceiveAsync("events", "a", TimeSpan.FromSeconds(30), 2);
        IReadOnlyList<MessageDelivery> helloOnB = await topics.ReceiveAsync("events", "b", TimeSpan.FromSeconds(30), 2);
        Assert.Single(helloOnA);
        Assert.Single(helloOnB);
        Assert.Equal("hello", Encoding.UTF8.GetString(helloOnA[0].Body));
        Assert.Equal("hello", Encoding.UTF8.GetString(helloOnB[0].Body));
    }

    [Fact]
    public async Task Vault_signs_and_loads_a_certificate()
    {
        using TempRoot temp = new();
        FileSecretStore secrets = new(temp.Root);
        await secrets.SetAsync("demo-secret", "s3cret");
        Assert.Equal("s3cret", await secrets.GetAsync("demo-secret"));

        FileKeyStore keys = new(temp.Root);
        await keys.CreateRsaAsync("demo-key");
        byte[] data = TextBytes("payload");
        byte[] signature = await keys.SignAsync("demo-key", data);
        Assert.True(await keys.VerifyAsync("demo-key", data, signature));
        Assert.False(await keys.VerifyAsync("demo-key", TextBytes("other"), signature));
        byte[] cipher = await keys.EncryptAsync("demo-key", data);
        Assert.Equal(data, await keys.DecryptAsync("demo-key", cipher));

        FileCertificateStore certificates = new(temp.Root);
        await certificates.CreateSelfSignedAsync("demo-cert", "demo-cert");
        using System.Security.Cryptography.X509Certificates.X509Certificate2 certificate = await certificates.LoadAsync("demo-cert");
        Assert.Contains("demo-cert", certificate.Subject, StringComparison.Ordinal);
        Assert.True(certificate.HasPrivateKey);
    }

    private static TableEntity Entity(string partitionKey, string rowKey, string label)
    {
        return new TableEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            Properties = new Dictionary<string, TableValue>
            {
                ["label"] = TableValue.FromString(label),
                ["count"] = TableValue.FromInteger(7)
            }
        };
    }

    private static MemoryStream Text(string value)
    {
        return new MemoryStream(TextBytes(value));
    }

    private static byte[] TextBytes(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }
}
