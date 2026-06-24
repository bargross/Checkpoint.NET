namespace Checkpoint.NET.Settings;

public class FileSystemStoreOptions
{
    /// <summary>
    /// If true, the library will create the root directory if it does not exist.
    /// If false, the library will throw an exception if the directory is missing.
    /// Default: true.
    /// </summary>
    public bool EnsureDirectoryExists { get; set; } = true;

    /// <summary>
    /// If true, the library will attempt to validate write permissions on the constructor.
    /// If false, permissions are only checked when SaveAsync/LoadAsync is called.
    /// Default: true.
    /// </summary>
    public bool ValidatePermissionsOnStartup { get; set; } = true;

    /// <summary>
    /// Optional fallback path if the primary directory is inaccessible.
    /// If null, the library will throw the original exception.
    /// </summary>
    public string? FallbackPath { get; set; }
}