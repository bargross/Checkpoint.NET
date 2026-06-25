using StateCheckpoint.NET.Models;
using StateCheckpoint.NET.Settings;

namespace StateCheckpoint.NET.Stores.FileSystem;

public class FileSystemModelStore : IModelStore
{
    private readonly string _rootPath;
    private readonly FileSystemStoreOptions _options;

    public FileSystemModelStore(string rootPath, FileSystemStoreOptions? options = null)
    {
        _options = options ?? new FileSystemStoreOptions();
        _rootPath = Path.Combine(rootPath, "models");

        if (_options.ValidatePermissionsOnStartup)
        {
            if (!FileSystemHelper.TryValidateWriteAccess(_rootPath, out var error))
            {
                // If fallback is provided, update the root path
                if (!string.IsNullOrEmpty(_options.FallbackPath))
                {
                    _rootPath = Path.Combine(_options.FallbackPath, "models");

                    Directory.CreateDirectory(_rootPath);
                }
                else
                {
                    throw error!;
                }
            }
        }
        else
        {
            // Still ensure the directory exists if required
            if (_options.EnsureDirectoryExists)
            {
                Directory.CreateDirectory(_rootPath);
            }
        }
    }

    /// <summary>
    /// saves model checkpoint for model training session
    /// </summary>
    /// <param name="checkpoint"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task SaveAsync(ModelCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        var binaryData = new Dictionary<string, byte[]>
        {
            ["weights.bin"] = checkpoint.WeightsBytes,
            ["optimizer.bin"] = checkpoint.OptimizerBytes
        };

        var manifest = new ModelManifest
        {
            HyperParams = checkpoint.HyperParams,
            Tokenizer = checkpoint.Tokenizer,
            CurrentEpoch = checkpoint.CurrentEpoch,
            LastTrainingLoss = checkpoint.LastTrainingLoss,
            CreatedAt = checkpoint.CreatedAt,
            Tags = checkpoint.Tags
        };

        await FileSystemHelper.SaveMultipleAsync(
            _rootPath,
            checkpoint.ModelId,
            binaryData,
            manifest,
            _options,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// loads a previously saved model
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<ModelCheckpoint?> LoadAsync(Guid modelId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (binaries, manifest) = await FileSystemHelper.LoadMultipleAsync<ModelManifest>(
                _rootPath,
                modelId,
                new[] { "weights.bin", "optimizer.bin" }, // ✅ Hardcoded constants
                "manifest.json",
                cancellationToken);

            return new ModelCheckpoint
            {
                ModelId = modelId,
                WeightsBytes = binaries["weights.bin"],
                OptimizerBytes = binaries["optimizer.bin"],
                HyperParams = manifest.HyperParams,
                Tokenizer = manifest.Tokenizer,
                CurrentEpoch = manifest.CurrentEpoch,
                LastTrainingLoss = manifest.LastTrainingLoss,
                CreatedAt = manifest.CreatedAt,
                Tags = manifest.Tags
            };
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public Task DeleteAsync(Guid modelId, CancellationToken cancellationToken = default)
        => FileSystemHelper.DeleteAsync(_rootPath, modelId, cancellationToken);

    public Task<List<Guid>> ListAsync(string? tagKey = null, string? tagValue = null, CancellationToken cancellationToken = default)
    {
        // Tag filtering is ignored for Phase 1 FileSystem.
        // The manager can filter in-memory if needed.
        return FileSystemHelper.ListAsync(_rootPath, cancellationToken);
    }
}