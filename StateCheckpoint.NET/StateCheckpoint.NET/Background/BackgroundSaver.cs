using System.Threading.Channels;

namespace StateCheckpoint.NET.Manager;

/// <summary>
/// Internal background queue for fire-and-forget save operations.
/// Uses a bounded Channel to prevent memory exhaustion.
/// </summary>
internal sealed class BackgroundSaver<T> : IAsyncDisposable
{
    private readonly Channel<Func<CancellationToken, Task>> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _workerTask;
    private readonly Action<Exception>? _onError;
    private bool _disposed;

    /// <param name="capacity">Max number of pending save operations in the queue.</param>
    /// <param name="onError">Optional callback for background save failures.</param>
    public BackgroundSaver(int capacity = 10, Action<Exception>? onError = null)
    {
        _onError = onError;

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // Block if queue is full (backpressure)
            SingleReader = true,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<Func<CancellationToken, Task>>(options);
        _workerTask = Task.Run(RunAsync);
    }

    /// <summary>
    /// Enqueues a save operation. Returns immediately.
    /// The operation will be executed on the background thread.
    /// </summary>
    public void Enqueue(Func<CancellationToken, Task> saveOperation, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BackgroundSaver<T>));

        try
        {
            _channel.Writer.WriteAsync(saveOperation, cancellationToken).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException("Background saver is disposed or shutting down.");
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var saveTask in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    await saveTask(_cts.Token);
                }
                catch (Exception ex)
                {
                    // Invoke the user-defined error handler if provided.
                    _onError?.Invoke(ex);

                    // If no handler, we swallow the exception to avoid crashing the training loop.
                    // In a future version, we could integrate with ILogger.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Signal the worker to stop
        _cts.Cancel();

        // Stop accepting new work
        _channel.Writer.Complete();

        // Wait for the worker to finish
        try
        {
            await _workerTask;
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }

        _cts.Dispose();
    }
}