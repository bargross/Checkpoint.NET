using System.Text.Json;
using StateCheckpoint.NET.Settings;

namespace StateCheckpoint.NET.Stores;

internal static class FileSystemHelper
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static async Task SaveAsync<TMetadata>(
        string rootPath,
        Guid id,
        byte[] binaryData,
        TMetadata metadata,
        FileSystemStoreOptions options,
        string binaryFileName = "data.bin",
        string metaFileName = "meta.json",
        CancellationToken cancellationToken = default) where TMetadata : class
    {
        // Fail fast if cancellation is already requested
        cancellationToken.ThrowIfCancellationRequested();

        var dir = Path.Combine(rootPath, id.ToString());

        // Check cancellation before synchronous directory operations
        cancellationToken.ThrowIfCancellationRequested();

        if (options.EnsureDirectoryExists)
        {
            Directory.CreateDirectory(dir);
        }
        else if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                $"The target directory '{dir}' does not exist and " +
                $"{nameof(options.EnsureDirectoryExists)} is set to false. " +
                "Please create the directory manually or enable EnsureDirectoryExists.");
        }

        // Check cancellation before synchronous write access test
        cancellationToken.ThrowIfCancellationRequested();

        // Validate write access (optional runtime check)
        if (!TryValidateWriteAccess(dir, out var error))
        {
            if (!string.IsNullOrEmpty(options.FallbackPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fallbackDir = Path.Combine(options.FallbackPath, id.ToString());
                Directory.CreateDirectory(fallbackDir);

                // Recursively call with the fallback path
                await SaveAsync(
                    options.FallbackPath,
                    id,
                    binaryData,
                    metadata,
                    options,
                    binaryFileName,
                    metaFileName,
                    cancellationToken);

                return;
            }

            throw error!;
        }

        // Check cancellation before async file writes
        cancellationToken.ThrowIfCancellationRequested();

        await File.WriteAllBytesAsync(Path.Combine(dir, binaryFileName), binaryData, cancellationToken);

        var json = JsonSerializer.Serialize(metadata, _jsonOpts);

        await File.WriteAllTextAsync(Path.Combine(dir, metaFileName), json, cancellationToken);
    }

    public static async Task SaveMultipleAsync<TMetadata>(
        string rootPath,
        Guid id,
        Dictionary<string, byte[]> binaryData,
        TMetadata metadata,
        FileSystemStoreOptions options,
        string metaFileName = "manifest.json",
        CancellationToken cancellationToken = default) where TMetadata : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dir = Path.Combine(rootPath, id.ToString());

        if (options.EnsureDirectoryExists)
        {
            Directory.CreateDirectory(dir);
        }
        else if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                $"The target directory '{dir}' does not exist and " +
                $"{nameof(options.EnsureDirectoryExists)} is set to false.");
        }

        // Validate write access
        if (!TryValidateWriteAccess(dir, out var error))
        {
            if (!string.IsNullOrWhiteSpace(options.FallbackPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fallbackDir = Path.Combine(options.FallbackPath, id.ToString());

                Directory.CreateDirectory(fallbackDir);

                await SaveMultipleAsync(
                    options.FallbackPath,
                    id,
                    binaryData,
                    metadata,
                    options,
                    metaFileName,
                    cancellationToken);

                return;
            }

            throw error!;
        }

        // Write each binary file
        foreach (var (fileName, data) in binaryData)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await File.WriteAllBytesAsync(Path.Combine(dir, fileName), data, cancellationToken);
        }

        // Write manifest
        var json = JsonSerializer.Serialize(metadata, _jsonOpts);
        await File.WriteAllTextAsync(Path.Combine(dir, metaFileName), json, cancellationToken);
    }

    public static async Task<(Dictionary<string, byte[]> Binaries, TMetadata Metadata)> LoadMultipleAsync<TMetadata>(
        string rootPath,
        Guid id,
        string[] binaryFileNames,
        string metaFileName = "manifest.json",
        CancellationToken cancellationToken = default) where TMetadata : class, new()
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dir = Path.Combine(rootPath, id.ToString());
        var metaPath = Path.Combine(dir, metaFileName);

        if (!File.Exists(metaPath))
            throw new FileNotFoundException($"Checkpoint {id} not found in {rootPath}");

        var json = await File.ReadAllTextAsync(metaPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<TMetadata>(json)!;

        var binaries = new Dictionary<string, byte[]>();
        foreach (var fileName in binaryFileNames)
        {
            var filePath = Path.Combine(dir, fileName);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Binary file {fileName} missing for checkpoint {id}");
            binaries[fileName] = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }

        return (binaries, metadata);
    }

    public static async Task<(byte[] Binary, TMetadata Metadata)> LoadAsync<TMetadata>(
        string rootPath,
        Guid id,
        string binaryFileName = "data.bin",
        string metaFileName = "meta.json",
        CancellationToken cancellationToken = default) where TMetadata : class, new()
    {
        // Fail fast if cancellation is already requested
        cancellationToken.ThrowIfCancellationRequested();

        var dir = Path.Combine(rootPath, id.ToString());
        var binaryPath = Path.Combine(dir, binaryFileName);
        var metaPath = Path.Combine(dir, metaFileName);

        if (!File.Exists(metaPath) || !File.Exists(binaryPath))
            throw new FileNotFoundException($"Checkpoint {id} not found in {rootPath}");

        // Check cancellation before async reads
        cancellationToken.ThrowIfCancellationRequested();

        var binary = await File.ReadAllBytesAsync(binaryPath, cancellationToken);
        var json = await File.ReadAllTextAsync(metaPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<TMetadata>(json)!;

        return (binary, metadata);
    }

    public static Task DeleteAsync(string rootPath, Guid id, CancellationToken cancellationToken = default)
    {
        // Fail fast if cancellation is already requested
        cancellationToken.ThrowIfCancellationRequested();

        var dir = Path.Combine(rootPath, id.ToString());

        // Check cancellation before synchronous directory deletion
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(dir))
            Directory.Delete(dir, true);

        return Task.CompletedTask;
    }

    public static Task<List<Guid>> ListAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        // Fail fast if cancellation is already requested
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(rootPath))
            return Task.FromResult(new List<Guid>());

        var dirs = Directory.GetDirectories(rootPath);
        var guids = new List<Guid>();

        foreach (var dir in dirs)
        {
            // Check cancellation during the loop (for long-running enumerations)
            cancellationToken.ThrowIfCancellationRequested();

            if (Guid.TryParse(Path.GetFileName(dir), out var id))
                guids.Add(id);
        }

        return Task.FromResult(guids);
    }

    public static bool TryValidateWriteAccess(string path, out Exception? error)
    {
        error = null;

        try
        {
            // Ensure the directory exists (if we are allowed to create it)
            Directory.CreateDirectory(path);

            // Test write access by creating and deleting a temporary file
            var testFile = Path.Combine(path, $".checkpoint_net_test_{Guid.NewGuid()}.tmp");

            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = new UnauthorizedAccessException(
                $"The process does not have write permission to '{path}'. " +
                $"Please run the application with elevated privileges or choose a different path.",
                ex);
            return false;
        }
        catch (IOException ex) when (ex.Message.Contains("disk") || ex.Message.Contains("space"))
        {
            error = new IOException(
                $"The storage location '{path}' is unavailable or out of space. Please check your disk.",
                ex);
            return false;
        }
        catch (Exception ex)
        {
            error = new IOException(
                $"The directory '{path}' could not be accessed. {ex.Message}",
                ex);
            return false;
        }
    }
}