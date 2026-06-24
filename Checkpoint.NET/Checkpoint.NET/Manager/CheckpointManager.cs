using Checkpoint.NET.Models;
using Checkpoint.NET.Settings;
using Checkpoint.NET.Stores;
using Checkpoint.NET.Stores.FileSystem;

namespace Checkpoint.NET.Manager;

public class CheckpointManager : IAsyncDisposable
{
    private readonly IModelStore _store;
    private readonly BackgroundSaver<ModelCheckpoint>? _backgroundSaver;
    private static string _defaultCheckpointPath = "./checkpoints";

    /// <summary>
    /// Initializes the manager with a custom storage provider.
    /// </summary>
    /// <param name="store">Any implementation of IModelStore (FileSystem, PostgreSQL, etc.)</param>
    public CheckpointManager(IModelStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Initializes the manager with the default FileSystem store.
    /// Models are saved to ./checkpoints by default.
    /// </summary>
    public CheckpointManager() : this(new FileSystemModelStore(_defaultCheckpointPath))
    {
    }

    /// <summary>
    /// Initializes the manager with the default FileSystem store at a custom root path.
    /// </summary>
    /// <param name="rootPath">Root directory where checkpoints will be stored.</param>
    public CheckpointManager(string rootPath) : this(new FileSystemModelStore(rootPath))
    {
    }

    /// <summary>
    /// Initializes the manager with the default FileSystem store and enables background saves.
    /// </summary>
    /// <param name="options">Background save configuration (Enabled, QueueCapacity, OnError).</param>
    public CheckpointManager(BackgroundSaveOptions options)
        : this(new FileSystemModelStore(_defaultCheckpointPath), options)
    {
    }

    /// <summary>
    /// Initializes the manager with a custom storage provider and enables background saves.
    /// </summary>
    /// <param name="store">Any implementation of IModelStore (FileSystem, PostgreSQL, etc.).</param>
    /// <param name="options">Background save configuration (Enabled, QueueCapacity, OnError).</param>
    public CheckpointManager(IModelStore store, BackgroundSaveOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        if (options != null && options.Enabled)
        {
            _backgroundSaver = new BackgroundSaver<ModelCheckpoint>(
                capacity: options.QueueCapacity,
                onError: options.OnError
            );
        }
    }

    /// <summary>
    /// Save a checkpoint. If existingId is null, generates a new GUID.
    /// </summary>
    /// <param name="weights">Raw byte array of model weights.</param>
    /// <param name="optimizer">Raw byte array of optimizer state (momentum, variance, etc.).</param>
    /// <param name="hyperParams">Hyperparameters used for this model.</param>
    /// <param name="tokenizer">Complete tokenizer state (vocab, merge rules, special tokens).</param>
    /// <param name="epoch">Current training epoch.</param>
    /// <param name="loss">Current training loss.</param>
    /// <param name="existingId">Optional existing GUID to overwrite. If null, a new one is generated.</param>
    /// <param name="tags">Optional user-defined tags for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Returns the ModelId (GUID) of the saved checkpoint.</returns>
    public async Task<Guid> SaveAsync(
        byte[] weights,
        byte[] optimizer,
        HyperParameters hyperParams,
        TokenizerData tokenizer,
        int epoch,
        float loss,
        Guid? existingId = null,
        Dictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var checkpoint = new ModelCheckpoint
        {
            ModelId = existingId ?? Guid.NewGuid(),
            WeightsBytes = weights,
            OptimizerBytes = optimizer,
            HyperParams = hyperParams,
            Tokenizer = tokenizer,
            CurrentEpoch = epoch,
            LastTrainingLoss = loss,
            CreatedAt = DateTime.UtcNow,
            Tags = tags ?? new Dictionary<string, string>()
        };

        // --- BACKGROUND MODE ---
        if (_backgroundSaver != null)
        {
            // DEEP COPY the byte arrays to prevent mutation during background write
            var capturedCheckpoint = new ModelCheckpoint
            {
                ModelId = checkpoint.ModelId,
                WeightsBytes = checkpoint.WeightsBytes.ToArray(),
                OptimizerBytes = checkpoint.OptimizerBytes.ToArray(),
                HyperParams = checkpoint.HyperParams,
                Tokenizer = checkpoint.Tokenizer,
                CurrentEpoch = checkpoint.CurrentEpoch,
                LastTrainingLoss = checkpoint.LastTrainingLoss,
                CreatedAt = checkpoint.CreatedAt,
                Tags = checkpoint.Tags
            };

            _backgroundSaver.Enqueue(async (ct) =>
            {
                await _store.SaveAsync(capturedCheckpoint, ct);
            });

            return checkpoint.ModelId; // Returns immediately!
        }

        // --- SYNCHRONOUS MODE (Default) ---
        await _store.SaveAsync(checkpoint, cancellationToken);
        return checkpoint.ModelId;
    }

    /// <summary>
    /// Load a checkpoint.
    /// </summary>
    /// <param name="modelId">The GUID of the checkpoint to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Returns the full ModelCheckpoint, including raw weight/optimizer bytes.</returns>
    public async Task<ModelCheckpoint?> LoadAsync(Guid modelId, CancellationToken cancellationToken = default)
        => await _store.LoadAsync(modelId, cancellationToken);

    /// <summary>
    /// Delete a checkpoint.
    /// </summary>
    /// <param name="modelId">The GUID of the checkpoint to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteAsync(Guid modelId, CancellationToken cancellationToken = default)
        => await _store.DeleteAsync(modelId, cancellationToken);

    /// <summary>
    /// List all saved model IDs, optionally filtered by a tag key/value pair.
    /// </summary>
    /// <param name="tagKey">Optional tag key to filter by.</param>
    /// <param name="tagValue">Optional tag value to filter by (requires tagKey).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of GUIDs matching the filter (or all if no filter provided).</returns>
    public async Task<List<Guid>> ListAsync(string? tagKey = null, string? tagValue = null, CancellationToken cancellationToken = default)
        => await _store.ListAsync(tagKey, tagValue, cancellationToken);

    /// <summary>
    /// Disposes the manager and ensures the background saver finishes all pending operations.
    /// Must be called if background saves are enabled.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_backgroundSaver != null)
        {
            await _backgroundSaver.DisposeAsync();
        }
    }
}