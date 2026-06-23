# Checkpoint.NET

[![NuGet Version](https://img.shields.io/nuget/v/Checkpoint.NET)](https://www.nuget.org/packages/Checkpoint.NET/)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Build Status](https://img.shields.io/github/actions/workflow/status/yourusername/Checkpoint.NET/ci.yml?branch=main)](https://github.com/yourusername/Checkpoint.NET/actions)

**Universal checkpointing for C# machine learning.** Persist full training states (Weights + Optimizer + Hyperparameters) *and* inference session states (KV-cache + Token History) to File System, PostgreSQL, or Cloud Blobs. Manage thousands of models by GUID with zero framework lock-in—built for custom training loops first.

---

## 📦 The Two Halves

| Half | Purpose | Data Stored | Typical Size |
| :--- | :--- | :--- | :--- |
| **Training Checkpoints** | Resume training after a crash, or distribute a fine-tuned model. | Weights, Optimizer (momentum/variance), Vocabulary, Hyperparameters. | 5 GB – 50 GB+ |
| **Inference Sessions** | Pause and resume a conversation without reprocessing the entire prompt history. | KV-cache, Token History, Sampling parameters (temp, top-p). | 100 MB – 2 GB |

Both halves share the same storage backends (FileSystem, PostgreSQL) and the same GUID-based management.

---

## ✨ Features

- ✅ **Framework Agnostic** – Works with your custom math, TorchSharp, ML.NET, or any C# ML library.
- ✅ **Two Storage Backends** – File System (local disks) or PostgreSQL (with Large Object support for >2GB models).
- ✅ **Complete Tokenizer Persistence** – Stores full vocab maps, merge rules, and special token IDs (BOS, EOS, PAD, UNK).
- ✅ **GUID-based Management** – Save, Load, List, and Delete by unique ID.
- ✅ **Tagging System** – Filter models and sessions by user-defined key/value tags.
- ✅ **Permission Handling** – Configurable startup validation and automatic fallback paths for file-system stores.
- ✅ **Async/Await** – Fully asynchronous and cancellation-token aware.

---

## 📥 Installation

```bash
dotnet add package Checkpoint.NET
```

Or via Package Manager Console:

```powershell
Install-Package Checkpoint.NET
```

## 🚀 Quick Start

### 1. Training Checkpoints (Saving Your Model)

**Save a model checkpoint (weights + optimizer + tokenizer):**

```csharp
using Checkpoint.NET.Manager;
using Checkpoint.NET.Models;
using Checkpoint.NET.Stores;

// 1. Choose a storage backend
var store = new FileSystemModelStore("./checkpoints");
var manager = new CheckpointManager(store);

// 2. Build your tokenizer (complete vocab + merge rules)
var tokenizer = new TokenizerData
{
    Type = "ByteLevelBPE",
    TokenToId = new Dictionary<string, int> { { "hello", 100 }, { "world", 200 } },
    IdToToken = new Dictionary<int, string> { { 100, "hello" }, { 200, "world" } },
    MergeRules = new List<MergeRule> { new MergeRule("h", "e"), new MergeRule("he", "llo") },
    SpecialTokens = new Dictionary<string, int> { { "bos", 0 }, { "eos", 1 } }
};

// 3. Define your hyperparameters
var hyperParams = new HyperParameters
{
    HiddenSize = 1024,
    NumLayers = 24,
    NumHeads = 16,
    LearningRate = 1e-4f
};

// 4. Convert your custom tensors to byte arrays
float[] weights = new float[1_000_000]; // Your model's parameters
float[] optimizerState = new float[2_000_000]; // Adam states
byte[] weightBytes = new byte[weights.Length * sizeof(float)];
Buffer.BlockCopy(weights, 0, weightBytes, 0, weightBytes.Length);

byte[] optimizerStateBytes = new byte[optimizerState.Length * sizeof(float)];
Buffer.BlockCopy(optimizerState, 0, optimizerStateBytes, 0, optimizerStateBytes.Length);

// 5. Save the checkpoint
Guid modelId = await manager.SaveAsync(
    weights: weightBytes,
    optimizer: optimizerStateBytes,
    hyperParams: hyperParams,
    tokenizer: tokenizer,
    epoch: 5,
    loss: 2.345f,
    tags: new Dictionary<string, string> { { "Architecture", "GPT-2-Style" }, { "Dataset", "Wikipedia" } }
);

Console.WriteLine($"✅ Saved model with ID: {modelId}");
```
**Load a saved checkpoint (resume training or serve):**

```csharp
// 1. Load the model by its GUID
var loaded = await manager.LoadAsync(modelId);

// 2. Convert bytes back to your custom tensors
float[] restoredWeights = new float[loaded.WeightsBytes.Length / sizeof(float)];
Buffer.BlockCopy(loaded.WeightsBytes, 0, restoredWeights, 0, loaded.WeightsBytes.Length);

// 3. Access all metadata
Console.WriteLine($"Epoch: {loaded.CurrentEpoch}, Loss: {loaded.LastTrainingLoss}");
Console.WriteLine($"Vocab Size: {loaded.Tokenizer.TokenToId.Count}");
Console.WriteLine($"BOS token ID: {loaded.Tokenizer.GetSpecialTokenId("bos")}");
```

### 2. Inference Sessions (Saving a Conversation)

**Save an active chat session (KV-cache + token history):**

```csharp
using Checkpoint.NET.Manager;
using Checkpoint.NET.Models;
using Checkpoint.NET.Stores;

var sessionStore = new FileSystemSessionStore("./sessions");
var sessionManager = new SessionManager(sessionStore);

// Simulate a conversation ID (e.g., from your database)
Guid chatId = Guid.NewGuid();

// Simulate the KV-cache from your inference engine (e.g., LLamaSharp)
byte[] kvCache = new byte[50 * 1024 * 1024]; // 50 MB
new Random().NextBytes(kvCache);

// Token history (the tokens already processed)
int[] tokenHistory = new int[] { 1, 2, 3, 100, 200, 300 };

// Save the session
await sessionManager.SaveAsync(
    sessionId: chatId,
    kvCacheBytes: kvCache,
    tokenHistory: tokenHistory,
    modelFingerprint: "llama-2-7b-v1",
    samplingConfig: new SamplingConfig { Temperature = 0.8f, TopP = 0.95f },
    tags: new Dictionary<string, string> { { "User", "Alice" }, { "Topic", "History" } }
);

Console.WriteLine($"✅ Saved session: {chatId}");
```

**Load a saved session (restore the conversation):**

```csharp
// 1. Load the session by its GUID
var loadedSession = await sessionManager.LoadAsync(chatId);

if (loadedSession != null)
{
    // 2. Inject the KV-cache back into your inference engine
    // (LLamaSharp example)
    // context.LoadState(new MemoryStream(loadedSession.KvCacheBytes));

    // 3. Continue generating tokens from where you left off
    Console.WriteLine($"✅ Session restored.");
    Console.WriteLine($"   Tokens in history: {loadedSession.TokenHistory.Length}");
    Console.WriteLine($"   Temperature: {loadedSession.SamplingConfig.Temperature}");
    Console.WriteLine($"   Model Fingerprint: {loadedSession.ModelFingerprint}");
    Console.WriteLine($"   Tags: {string.Join(", ", loadedSession.Tags.Select(kv => $"{kv.Key}={kv.Value}"))}");
}
```

**List all active sessions:**

```csharp
var allSessionIds = await sessionManager.ListAsync();
foreach (var id in allSessionIds)
{
    Console.WriteLine($" - {id}");
}

// Filter by tag
var aliceSessions = await sessionManager.ListAsync(tagKey: "User", tagValue: "Alice");
```

**Delete a session:**

```csharp
await sessionManager.DeleteAsync(chatId);
```

**Important Note:** The KV-cache size is typically between 100 MB and 2 GB, making it far lighter than full model weights. The SessionManager uses the same storage backends as the CheckpointManager—so you can store sessions alongside your models or in a separate location.

## 🗄️ Storage Providers

Checkpoint.NET provides two built-in storage backends for both Training Checkpoints and Inference Sessions:

- **FileSystem** – Stores data on local or mounted drives (ideal for development, edge deployments, or single-server setups).
- **PostgreSQL** – Stores metadata in relational tables and binaries either as Large Objects (Models) or BYTEA (Sessions).

Both backends support the same CRUD operations (Save, Load, List, Delete) and use the same GUID-based identification system.

---

### FileSystem (`FileSystemModelStore`, `FileSystemSessionStore`)

The FileSystem provider saves each checkpoint in its own dedicated folder. This is the simplest and most reliable storage option for local development and single-server production environments.

**Usage:**

Instantiate FileSystemModelStore with path "./checkpoints" and pass to CheckpointManager

```csharp
var modelStore = new FileSystemModelStore("./checkpoints");
var checkpointManager = new CheckpointManager(modelStore);
```

Instantiate FileSystemSessionStore with path "./sessions" and pass to SessionManager

```csharp
var sessionStore = new FileSystemSessionStore("./sessions");
var sessionManager = new SessionManager(sessionStore);
```

**Directory Structure:**

```plaintext
/data/
├── models/
│   └── {ModelId}/
│       ├── weights.bin      # Model weights (GBs)
│       ├── optimizer.bin    # Optimizer state (GBs)
│       └── manifest.json    # Metadata (HyperParams, Tokenizer, Epoch, Loss, Tags)
└── sessions/
    └── {SessionId}/
        ├── kv.bin           # KV-cache (MBs - 2GB)
        └── meta.json        # Metadata (ModelFingerprint, TokenHistory, SamplingConfig, Tags)
```


**Configuration Options:**

You can customize the behavior of FileSystem stores using `FileSystemStoreOptions`.

Instantiate FileSystemStoreOptions with EnsureDirectoryExists, ValidatePermissionsOnStartup, and FallbackPath, then pass to FileSystemModelStore constructor

```csharp
var options = new FileSystemStoreOptions
{
    EnsureDirectoryExists = true,
    ValidatePermissionsOnStartup = true,
    FallbackPath = "/temp/backup_models"
};

var store = new FileSystemModelStore("./protected_models", options);
```

| Property | Description |
| :--- | :--- |
| `EnsureDirectoryExists` | If `true`, the library creates the directory. If `false`, throws a `DirectoryNotFoundException` if the path is missing. |
| `ValidatePermissionsOnStartup` | If `true`, the library tests write permissions in the constructor by creating and deleting a temporary file. If `false`, permissions are only checked when `SaveAsync` or `LoadAsync` is called. |
| `FallbackPath` | An optional absolute or relative path to use if the primary directory is inaccessible due to permission errors. Useful for CI/CD environments or containerized deployments. |

---

### PostgreSQL (`PostgresModelStore`, `PostgresSessionStore`)

The PostgreSQL provider stores metadata in structured JSONB columns and binaries using two different strategies optimized for the data size:

- **Training (Models):** Uses PostgreSQL **Large Objects** (`OID`) – supports individual files > 2 GB (weights and optimizer).
- **Inference (Sessions):** Uses `BYTEA` columns – efficient for KV-caches typically < 1 GB and faster for frequent save/load operations.

**Usage:**

Instantiate PostgresModelStore with connection string and pass to CheckpointManager

```csharp
// Option A: Connection String (Library manages the connection pool)
var modelStore = new PostgresModelStore("Host=localhost;Database=checkpoints;Username=postgres;Password=pass");
var checkpointManager = new CheckpointManager(modelStore);

// Option B: Existing NpgsqlDataSource (Recommended for DI)
// var modelStore = new PostgresModelStore(myDataSource);
```

Instantiate PostgresSessionStore with connection string and pass to SessionManager
```csharp
// Option A: Connection String
var sessionStore = new PostgresSessionStore("Host=localhost;Database=checkpoints;Username=postgres;Password=pass");
var sessionManager = new SessionManager(sessionStore);

// Option B: Existing NpgsqlDataSource
// var sessionStore = new PostgresSessionStore(myDataSource);
```

Call EnsureSchemaAsync on both stores at application startup

```csharp
await modelStore.EnsureSchemaAsync();
await sessionStore.EnsureSchemaAsync();
```


**Database Schema:**

| Training (Models) | Inference (Sessions) |
| :--- | :--- |
| **`model_manifests`** <br> `model_id` (UUID, PK) <br> `hyper_params` (JSONB) <br> `tokenizer` (JSONB) <br> `epoch` (INT) <br> `loss` (FLOAT) <br> `created_at` (TIMESTAMPTZ) <br> `tags` (JSONB) | **`inference_sessions`** <br> `session_id` (UUID, PK) <br> `model_fingerprint` (TEXT) <br> `token_history` (INTEGER[]) <br> `sampling_config` (JSONB) <br> `kv_cache_bytes` (BYTEA) <br> `last_updated` (TIMESTAMPTZ) <br> `tags` (JSONB) |
| **`model_blobs`** <br> `model_id` (UUID, PK, FK) <br> `weights_oid` (OID) <br> `optimizer_oid` (OID) | *(Single table – no Large Objects required)* |
| **Storage Strategy:** Large Objects (LO) | **Storage Strategy:** BYTEA |
| **Size Limit:** Up to 2 GB per LO (weights + optimizer) | **Practical Limit:** ~1 GB per BYTEA (KV-cache) |

**Why Different Strategies?**

| Aspect | Models (Training) | Sessions (Inference) |
| :--- | :--- | :--- |
| **Binary Size** | 5 GB – 50 GB+ | 100 MB – 2 GB |
| **PostgreSQL Feature** | Large Objects (`OID`) | `BYTEA` column |
| **Performance** | Optimized for streaming large data in/out. | Optimized for small, frequent read/write operations. |
| **Why Not BYTEA?** | BYTEA has a 1 GB hard limit per column in PostgreSQL. | BYTEA is simpler, faster for small data, and does not require managing OID references. |

---

### Extending with Custom Storage Backends

If you need a storage backend not provided out-of-the-box (e.g., Azure Blob Storage, AWS S3, SQL Server, Redis), you can implement the appropriate interface:

- **Training:** Implement `IModelStore`.
- **Inference:** Implement `ISessionStore`.

Implement AzureBlobModelStore class with SaveAsync, LoadAsync, DeleteAsync, ListAsync methods

```csharp
public class AzureBlobModelStore : IModelStore
{
    public async Task SaveAsync(ModelCheckpoint checkpoint, CancellationToken ct = default)
    {
        // Upload checkpoint.WeightsBytes + checkpoint.OptimizerBytes to Azure Blob
        // Upload manifest (HyperParams, Tokenizer, etc.) as JSON
    }

    public async Task<ModelCheckpoint?> LoadAsync(Guid modelId, CancellationToken ct = default)
    {
        // Download the binary blobs and manifest JSON
        // Return a ModelCheckpoint object
    }

    public async Task DeleteAsync(Guid modelId, CancellationToken ct = default)
    {
        // Delete the blobs and manifest from Azure
    }

    public async Task<List<Guid>> ListAsync(string? tagKey = null, string? tagValue = null, CancellationToken ct = default)
    {
        // List all model IDs, optionally filtered by tags
    }
}
```


Once implemented, pass your custom store to the `CheckpointManager` or `SessionManager` just like the built-in providers:

Instantiate AzureBlobModelStore and pass to CheckpointManager

```csharp
var azureStore = new AzureBlobModelStore("connection-string", "container-name");
var manager = new CheckpointManager(azureStore);
```
