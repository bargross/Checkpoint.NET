using StateCheckpoint.NET.Manager;
using FluentAssertions;

namespace StateCheckpoint.NET.Tests.Manager;

public class BackgroundSaverTests
{
    [Fact]
    public async Task Constructor_ShouldStartWorkerTask()
    {
        // Arrange & Act
        await using var saver = new BackgroundSaver<object>(capacity: 1);

        // Assert
        var tcs = new TaskCompletionSource<bool>();
        saver.Enqueue(async (ct) =>
        {
            await Task.Delay(10, ct);
            tcs.SetResult(true);
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        completed.Should().Be(tcs.Task);
        tcs.Task.Result.Should().BeTrue();
    }

    [Fact]
    public async Task Enqueue_ShouldAddOperationToQueue()
    {
        // Arrange
        await using var saver = new BackgroundSaver<object>(capacity: 5);
        var invoked = false;

        // Act
        saver.Enqueue(async (ct) =>
        {
            await Task.CompletedTask;
            invoked = true;
        });

        // Let the background thread process it
        await Task.Delay(50);

        // Assert
        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task Enqueue_WhenDisposed_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var saver = new BackgroundSaver<object>(capacity: 1);
        await saver.DisposeAsync();

        // Act
        Action act = () => saver.Enqueue(async (ct) => await Task.CompletedTask);

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Enqueue_WhenCapacityReached_ShouldBlockUntilSpaceFrees()
    {
        // Capacity = 1 so the channel fills with a single waiting item.
        await using var saver = new BackgroundSaver<object>(capacity: 1);
        using var holdEvent = new ManualResetEventSlim(false);

        // 1st enqueue – worker picks it up and blocks synchronously.
        saver.Enqueue(async (ct) =>
        {
            holdEvent.Wait(ct);  // Blocks the worker thread until we signal.
            await Task.CompletedTask;
        });

        // Give the worker time to start and block on the event.
        await Task.Delay(100);

        // 2nd enqueue – worker is blocked, so this fills the channel (capacity = 1).
        saver.Enqueue(async (ct) => await Task.CompletedTask);

        // 3rd enqueue – channel is full, so this MUST block.
        var enqueueTask = Task.Run(() => saver.Enqueue(async (ct) => await Task.CompletedTask));

        // Verify the third enqueue is blocked.
        await Task.Delay(100);
        enqueueTask.IsCompleted.Should().BeFalse("Enqueue should be blocked waiting for capacity");

        // Release the worker – it completes the first task, then reads the second.
        holdEvent.Set();

        // Now the third enqueue should complete.
        await enqueueTask.TimeoutAfter(2000);
        enqueueTask.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ErrorHandling_ShouldInvokeOnErrorCallback()
    {
        // Arrange
        Exception? capturedException = null;
        var onError = new Action<Exception>(ex => capturedException = ex);
        await using var saver = new BackgroundSaver<object>(capacity: 1, onError);

        // Act
        var expectedException = new InvalidOperationException("Test error");
        saver.Enqueue(async (ct) =>
        {
            await Task.CompletedTask;
            throw expectedException;
        });

        // Allow the background task to process
        await Task.Delay(100);

        // Assert
        capturedException.Should().NotBeNull();
        capturedException.Should().Be(expectedException);
    }

    [Fact]
    public async Task ErrorHandling_WhenNoCallbackProvided_ShouldSwallowException()
    {
        // Arrange
        await using var saver = new BackgroundSaver<object>(capacity: 1);

        // Act & Assert - should not throw
        var act = async () =>
        {
            saver.Enqueue(async (ct) =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("Test error");
            });

            await Task.Delay(100);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_ShouldCancelWorkerAndWaitForCompletion()
    {
        // Arrange
        var saver = new BackgroundSaver<object>(capacity: 2);
        var processed = false;

        saver.Enqueue(async (ct) =>
        {
            await Task.Delay(200, ct);
            processed = true;
        });

        // Act
        await saver.DisposeAsync();

        // Assert
        processed.Should().BeFalse("The long-running task should have been cancelled and not completed.");
    }

    [Fact]
    public async Task DisposeAsync_ShouldCompleteAllPendingTasks()
    {
        // Arrange
        var saver = new BackgroundSaver<object>(capacity: 2);
        var tcs = new TaskCompletionSource<bool>();

        saver.Enqueue(async (ct) =>
        {
            await Task.Delay(50, ct);
            tcs.SetResult(true);
        });

        // Wait for the task to complete
        await Task.Delay(100);
        await saver.DisposeAsync();

        // Assert
        var result = await tcs.Task.TimeoutAfter(100);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ShouldCancelAfterEnqueue()
    {
        // Arrange
        var saver = new BackgroundSaver<object>(capacity: 1);

        // Act
        saver.Enqueue(async (ct) =>
        {
            await Task.Delay(1000, ct);
        });

        await saver.DisposeAsync();

        // Assert
        Action act = () => saver.Enqueue(async (ct) => await Task.CompletedTask);

        act.Should().Throw<ObjectDisposedException>();
    }
}

// Extension method for timeout on tasks
public static class TaskExtensions
{
    public static async Task<T> TimeoutAfter<T>(this Task<T> task, int millisecondsTimeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(millisecondsTimeout));
        if (completed != task)
            throw new TimeoutException($"Task did not complete within {millisecondsTimeout}ms");
        return await task;
    }

    public static async Task TimeoutAfter(this Task task, int millisecondsTimeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(millisecondsTimeout));
        if (completed != task)
            throw new TimeoutException($"Task did not complete within {millisecondsTimeout}ms");
    }
}