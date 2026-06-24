# Checkpoint.NET

[![NuGet Version](https://img.shields.io/nuget/v/Checkpoint.NET)](https://www.nuget.org/packages/Checkpoint.NET/)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Build Status](https://img.shields.io/github/actions/workflow/status/bargross/Checkpoint.NET/dotnet.yml?branch=main)](https://github.com/bargross/Checkpoint.NET/actions)
[![Changelog](https://img.shields.io/badge/Changelog-view-blue)](./CHANGELOG.md)

## What is Checkpoint.NET?

Training large language models is expensive and time‑consuming. A crash or an I/O stall can waste hours of GPU time. Checkpoint.NET is the ultimate state persistence layer for C# machine learning. It solves these problems by:

- **Running saves in the background** – Your training loop isn't blocked by disk writes.
- **Ensuring data integrity** – Deep copying prevents in‑memory mutations from corrupting your checkpoints.
- **Being framework‑agnostic** – Use it with any C# ML library or your own custom math.
- **Managing both training and inference** – Save model weights, optimizer states, AND inference session states (KV‑cache + token history).

Persist full training checkpoints (Weights + Optimizer + Tokenizer + Hyperparameters) and inference sessions (KV‑Cache + Token History) across FileSystem, PostgreSQL, or SQL Server. Zero framework lock‑in, fully async, and managed entirely by GUIDs—built for custom training loops first.

---

## The Two Halves

| Half | Purpose | Data Stored | Typical Size |
| :--- | :--- | :--- | :--- |
| **Training Checkpoints** | Resume training after a crash, or distribute a fine-tuned model. | Weights, Optimizer (momentum/variance), Vocabulary, Hyperparameters. | 5 GB – 50 GB+ |
| **Inference Sessions** | Pause and resume a conversation without reprocessing the entire prompt history. | KV-cache, Token History, Sampling parameters (temp, top-p). | 100 MB – 2 GB |

Both halves share the same storage backends (FileSystem, PostgreSQL) and the same GUID-based management.

---

## Features

- ✅ **Dual Domain Support** – Save training states (weights + optimizer) AND inference states (KV-cache + token history) with separate, dedicated managers.
- ✅ **Three Storage Backends** – File System (local disks), PostgreSQL (Large Object support for >2GB models), and SQL Server (VARBINARY(MAX) support).
- ✅ **Background (Fire-and-Forget) Saves** – Opt-in non-blocking saves using a bounded queue. Prevents disk I/O from stalling your GPU training or chat inference loops. Supports custom error callbacks for failure handling.
- ✅ **Memory-Safe Deep Copy** – When background saves are enabled, byte arrays are automatically cloned to prevent data corruption from in-memory mutations during writes.
- ✅ **Complete Tokenizer Persistence** – Stores full vocab maps, merge rules, and special token IDs (BOS, EOS, PAD, UNK).
- ✅ **GUID-based Management** – Save, Load, List, and Delete by unique ID.
- ✅ **Tagging System** – Filter models and sessions by user-defined key/value tags.
- ✅ **Permission Handling** – Configurable startup validation and automatic fallback paths for file-system stores.
- ✅ **Async/Await** – Fully asynchronous and cancellation-token aware.
- ✅ **Framework Agnostic** – Works with your custom math, TorchSharp, ML.NET, or any C# ML library.

---

## Core Concepts

Checkpoint.NET is built around two distinct domains, each with its own manager and storage model:

### 1. Training Checkpoints (`CheckpointManager`)

Used for saving the state of a model during training. This includes:

- **Weights** – The model parameters (byte array).
- **Optimizer** – Momentum, variance, and other optimizer states (byte array).
- **Tokenizer** – Complete vocab maps, merge rules, and special token IDs.
- **Hyperparameters** – Hidden size, layers, learning rate, etc.
- **Metadata** – Epoch, loss, creation date, and user‑defined tags.

**Use case:** Resume training after a crash, or distribute a fine‑tuned model.

### 2. Inference Sessions (`SessionManager`)

Used for saving the state of an active chat or inference session. This includes:

- **KV‑Cache** – The attention key/value tensors from the inference engine (byte array).
- **Token History** – The list of token IDs already processed.
- **Sampling Configuration** – Temperature, top‑p, top‑k, etc.
- **Model Fingerprint** – A unique identifier for the model (e.g., SHA256 hash).

**Use case:** Pause and resume a conversation without reprocessing the entire prompt history.

---

## Installation

```bash
dotnet add package Checkpoint.NET
```

Or via Package Manager Console:

```powershell
Install-Package Checkpoint.NET
```

## Quick Start

### 1. Training Checkpoints (Saving Your Model)

**Save a model checkpoint (weights + optimizer + tokenizer):**

```csharp
using Checkpoint.NET.Manager;
using Checkpoint.NET.Models;
using Checkpoint.NET.Stores;

// 1. Choose a storage backend (FileSystem, PostgresModelStore, or SqlServerModelStore)
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

// 5. Save the checkpoint (synchronous – blocks until written)
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

**Fire-and-Forget Background Save (Non-Blocking):**

```csharp
// Enable background saves with a bounded queue
var options = new BackgroundSaveOptions
{
    Enabled = true,
    QueueCapacity = 5,
    OnError = ex => Console.WriteLine($"Background save failed: {ex.Message}")
};

// Wrap in 'await using' to ensure the background thread is cleaned up on exit
await using var bgManager = new CheckpointManager(options);

// This returns immediately! The actual disk write happens in the background.
Guid modelId = await bgManager.SaveAsync(
    weights: weightBytes,
    optimizer: optimizerStateBytes,
    hyperParams: hyperParams,
    tokenizer: tokenizer,
    epoch: 5,
    loss: 2.345f
);

Console.WriteLine($"✅ Model queued for save. ID: {modelId}");
// Continue training immediately – the save runs in the background.
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

---

### 2. Inference Sessions (Saving a Conversation)

**Save an active chat session (KV-cache + token history):**

```csharp
using Checkpoint.NET.Manager;
using Checkpoint.NET.Models;
using Checkpoint.NET.Stores;

// Choose a storage backend (FileSystem, PostgresSessionStore, or SqlServerSessionStore)
var sessionStore = new FileSystemSessionStore("./sessions");
var sessionManager = new SessionManager(sessionStore);

// Simulate a conversation ID (e.g., from your database)
Guid chatId = Guid.NewGuid();

// Simulate the KV-cache from your inference engine (e.g., LLamaSharp)
byte[] kvCache = new byte[50 * 1024 * 1024]; // 50 MB
new Random().NextBytes(kvCache);

// Token history (the tokens already processed)
int[] tokenHistory = new int[] { 1, 2, 3, 100, 200, 300 };

// Save the session (synchronous – blocks until written)
await sessionManager.SaveAsync(
    sessionId: chatId,
    kvCacheBytes: kvCache,
    tokenHistory: tokenHistory,
    modelFingerprint: "llama-2-7b-v1",
    samplingConfig: new SamplingData { Temperature = 0.8f, TopP = 0.95f },
    tags: new Dictionary<string, string> { { "User", "Alice" }, { "Topic", "History" } }
);

Console.WriteLine($"✅ Saved session: {chatId}");
```

**Fire-and-Forget Background Save for Sessions (Non-Blocking):**

```csharp
var options = new BackgroundSaveOptions
{
    Enabled = true,
    QueueCapacity = 5,
    OnError = ex => Console.WriteLine($"Background session save failed: {ex.Message}")
};

await using var bgSessionManager = new SessionManager(options);

// Returns immediately – the session is saved in the background.
Guid chatId = await bgSessionManager.SaveAsync(
    sessionId: chatId,
    kvCacheBytes: kvCache,
    tokenHistory: tokenHistory,
    modelFingerprint: "llama-2-7b-v1"
);

Console.WriteLine($"✅ Session queued for save. ID: {chatId}");
// Continue the conversation immediately – the save runs in the background.
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

---

**💡 Switching Storage Backends:**

Drop in any supported store:

```csharp
// PostgreSQL
var pgStore = new PostgresModelStore("Host=localhost;Database=checkpoints");
var manager = new CheckpointManager(pgStore);

// SQL Server
var sqlStore = new SqlServerModelStore("Server=localhost;Database=Checkpoints;Integrated Security=True;");
var manager = new CheckpointManager(sqlStore);
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

**⚠️ Important:** When using background saves (`Enabled = true`), always wrap your manager in an `await using` block or explicitly call `DisposeAsync()` to ensure pending saves complete before your application exits.

**Important Note:** The KV-cache size is typically between 100 MB and 2 GB, making it far lighter than full model weights. The SessionManager uses the same storage backends as the CheckpointManager—so you can store sessions alongside your models or in a separate location.

## Resuming a Checkpoint

When you save a checkpoint, `CheckpointManager.SaveAsync()` returns a `Guid` that uniquely identifies that checkpoint. To resume training later, you **must persist this ID** (e.g., in a file, a database, or an environment variable). On application startup, read the ID and pass it to `LoadAsync` to restore your model's state.

### Example Workflow

```csharp
using Checkpoint.NET.Manager;
using Checkpoint.NET.Models;
using Checkpoint.NET.Stores;

// 1. Load the model ID from persistent storage (e.g., a file)
Guid modelId = LoadModelIdFromFile("model-id.txt");

// 2. Resume training from the last checkpoint
var loaded = await manager.LoadAsync(modelId);
if (loaded != null)
{
    // Restore weights, optimizer, epoch, etc.
    // Continue training from epoch loaded.CurrentEpoch.
}

// 3. Save a checkpoint (reuse the same ID to overwrite)
Guid newId = await manager.SaveAsync(
    weights: currentWeights,
    optimizer: currentOptimizer,
    hyperParams: hyperParams,
    tokenizer: tokenizer,
    epoch: currentEpoch,
    loss: currentLoss,
    existingId: modelId  // <-- Reuse the same ID
);

// 4. Persist the ID again (in case it was newly generated)
SaveModelIdToFile("model-id.txt", newId);
```

**Important:** If you pass `existingId` to `SaveAsync`, the existing checkpoint is **overwritten**. This is useful for keeping a single "latest checkpoint" file. If you want to keep a history of checkpoints, generate a new `Guid` each time (omit `existingId`).

**⚠️ Important:** When using background saves (`Enabled = true`), always wrap your manager in an `await using` block or explicitly call `DisposeAsync()` before your application exits. This ensures that all pending saves complete and the background thread is properly cleaned up.

## Storage Providers

Checkpoint.NET provides three built‑in storage backends:

- **FileSystem** – Stores data on local or mounted drives (ideal for development, edge deployments, or single‑server setups).
- **PostgreSQL** – Stores metadata in JSONB columns and binaries as Large Objects (for Models) or BYTEA (for Sessions).
- **SQL Server** – Stores metadata in JSON strings and binaries as VARBINARY(MAX).

All backends support the same CRUD operations (Save, Load, List, Delete) and use the same GUID‑based identification system.

---

### FileSystem (`FileSystemModelStore`, `FileSystemSessionStore`)

The FileSystem provider saves each checkpoint in its own dedicated folder. This is the simplest and most reliable storage option for local development and single‑server production environments.

**Directory Structure:**

```
./data/
├── models/
│   └── {ModelId}/
│       ├── weights.bin          # Model weights (GBs)
│       ├── optimizer.bin        # Optimizer state (GBs)
│       └── manifest.json        # Metadata (HyperParams, Tokenizer, Epoch, Loss, Tags)
└── sessions/
    └── {SessionId}/
        ├── kv.bin               # KV‑cache (MBs – 2GB)
        └── meta.json            # Metadata (ModelFingerprint, TokenHistory, SamplingConfig, Tags)
```

**Usage:**

```csharp
var modelStore = new FileSystemModelStore("./checkpoints");
var sessionStore = new FileSystemSessionStore("./sessions");
```

**Configuration Options:**

You can customize the behavior of FileSystem stores using `FileSystemStoreOptions`:

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
| `EnsureDirectoryExists` | If `true`, the library creates the directory. If `false`, throws if missing. |
| `ValidatePermissionsOnStartup` | If `true`, tests write permissions in the constructor. |
| `FallbackPath` | Optional path to use if the primary directory is inaccessible. |

---

### PostgreSQL (`PostgresModelStore`, `PostgresSessionStore`)

The PostgreSQL provider stores metadata in structured JSONB columns and binaries using two different strategies optimized for the data size:

- **Training (Models):** Uses PostgreSQL **Large Objects** (`OID`) – supports individual files > 2 GB.
- **Inference (Sessions):** Uses `BYTEA` columns – efficient for KV‑caches typically < 1 GB.

**Usage:**

```csharp
var modelStore = new PostgresModelStore("Host=localhost;Database=checkpoints;Username=postgres;Password=pass");
var sessionStore = new PostgresSessionStore("Host=localhost;Database=checkpoints;Username=postgres;Password=pass");

// Ensure tables exist (call once at application startup)
await modelStore.EnsureSchemaAsync();
await sessionStore.EnsureSchemaAsync();
```

**Database Schema:**

| Training (Models) | Inference (Sessions) |
| :--- | :--- |
| `model_manifests` (JSONB metadata) | `inference_sessions` (BYTEA for KV‑cache) |
| `model_blobs` (OID references) | (Single table – no Large Objects) |
| Storage: Large Objects (LO) | Storage: BYTEA |
| Limit: >2 GB per LO | Limit: ~1 GB per BYTEA |

---

### SQL Server (`SqlServerModelStore`, `SqlServerSessionStore`)

The SQL Server provider stores metadata as JSON strings in `NVARCHAR(MAX)` columns and binaries as `VARBINARY(MAX)`.

- **Training (Models):** Uses `VARBINARY(MAX)` – supports up to 2 GB per file.
- **Inference (Sessions):** Uses `VARBINARY(MAX)` – efficient for KV‑caches.

**Usage:**

```csharp
var modelStore = new SqlServerModelStore("Server=localhost;Database=Checkpoints;Integrated Security=True;");
var sessionStore = new SqlServerSessionStore("Server=localhost;Database=Checkpoints;Integrated Security=True;");

await modelStore.EnsureSchemaAsync();
await sessionStore.EnsureSchemaAsync();
```

**Database Schema:**

| Training (Models) | Inference (Sessions) |
| :--- | :--- |
| `ModelManifests` (NVARCHAR(MAX) JSON metadata) | `InferenceSessions` (VARBINARY(MAX) for KV‑cache) |
| `ModelBlobs` (VARBINARY(MAX) for weights/optimizer) | (Single table – no blob table) |
| Storage: VARBINARY(MAX) | Storage: VARBINARY(MAX) |
| Limit: 2 GB per file | Limit: 2 GB per file |

---

### Extending with Custom Storage Backends

If you need a storage backend not provided out‑of‑the‑box (e.g., Azure Blob Storage, AWS S3, or MongoDB), implement the appropriate interface:

- **Training:** Implement `IModelStore`.
- **Inference:** Implement `ISessionStore`.

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

Once implemented, pass your custom store to the manager:

```csharp
var azureStore = new AzureBlobModelStore("connection-string", "container-name");
var manager = new CheckpointManager(azureStore);
```

## Dependencies & Roadmap

**v1.0.0 (Monolithic):** 
To keep the initial release simple and fully featured, `Checkpoint.NET` includes built-in providers for FileSystem, PostgreSQL, and SQL Server. 
If you only use FileSystem, you will still see `Npgsql.dll` and `Microsoft.Data.SqlClient.dll` in your output folder. These are harmless and unused.

**Future v2.0 (Modular):**
We plan to split the library into separate NuGet packages (`Checkpoint.NET.Core`, `Checkpoint.NET.PostgreSQL`, `Checkpoint.NET.SqlServer`) to remove these optional dependencies. The v1.0.0 API will remain fully compatible with v2.0.

## License

This project is licensed under the **Apache License, Version 2.0** – see the [LICENSE](./LICENSE) file for details.

```
Copyright 2025 Leon

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

## Acknowledgements

- [Npgsql](https://www.npgsql.org/) – PostgreSQL provider for .NET.
- [Microsoft.Data.SqlClient](https://github.com/dotnet/SqlClient) – SQL Server provider for .NET.
- [Testcontainers](https://testcontainers.com/) – Integration testing with containers.