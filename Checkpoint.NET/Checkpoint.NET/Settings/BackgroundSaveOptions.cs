namespace Checkpoint.NET.Settings;

public class BackgroundSaveOptions
{
    /// <summary>
    /// Enables background (fire-and-forget) saves.
    /// Default is false for simplicity, but you can default to true if you prefer.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Maximum number of pending save operations in the queue.
    /// If exceeded, the training loop will block until space frees up.
    /// Default: 10.
    /// </summary>
    public int QueueCapacity { get; set; } = 10;

    /// <summary>
    /// Optional callback invoked when a background save fails.
    /// If not provided, exceptions are silently swallowed (to prevent training crashes).
    /// </summary>
    public Action<Exception>? OnError { get; set; }
}