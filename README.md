# CodeFoxtrot.CloudLocal

Local filesystem implementations of blob storage, table storage, storage queues, a secret/key/certificate vault, and queue/topic messaging. The libraries target `net8.0`, `net9.0`, and `net10.0` and run on Windows, macOS, and Linux.

Application code calls a small port such as `IBlobStore`. The file implementation in these packages is one of the types that can sit behind that port. Azure and AWS SDK clients stay in the application, which chooses those package versions itself.

## Packages

- `CodeFoxtrot.CloudLocal.Core` — path confinement, atomic writes, and the claim folder used by queues and messaging
- `CodeFoxtrot.CloudLocal.Blobs` — containers and blobs, including append
- `CodeFoxtrot.CloudLocal.Tables` — entities addressed by partition key and row key
- `CodeFoxtrot.CloudLocal.Queues` — storage queues with visibility timeout and a poison folder
- `CodeFoxtrot.CloudLocal.Vault` — secrets, RSA keys, and certificates
- `CodeFoxtrot.CloudLocal.Messaging` — queues and topics with subscriptions, peek-lock, abandon, and dead-letter

Pass one root directory into each store. Data is written under `blobs`, `tables`, `queues`, `vault`, and `messaging` inside that root. The root holds secret values and private keys. Keep it out of source control.

`demo/BlazorUI` is a server-side Blazor app that exercises each port. `CloudLocal:ProviderMode` selects `Local`, `Azure`, or `Aws`.
