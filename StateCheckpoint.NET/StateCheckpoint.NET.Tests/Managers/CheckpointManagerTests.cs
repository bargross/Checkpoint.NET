using StateCheckpoint.NET.Manager;
using StateCheckpoint.NET.Models;
using StateCheckpoint.NET.Settings;
using StateCheckpoint.NET.Stores;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StateCheckpoint.NET.Tests.Manager;

public class CheckpointManagerTests
{
    private readonly Mock<IModelStore> _mockStore;
    private readonly HyperParameters _hyperParams;
    private readonly TokenizerData _tokenizer;
    private readonly byte[] _weights;
    private readonly byte[] _optimizer;
    private readonly Dictionary<Guid, ModelCheckpoint> _savedCheckpoints;

    public CheckpointManagerTests()
    {
        _mockStore = new Mock<IModelStore>(MockBehavior.Strict);
        _savedCheckpoints = new Dictionary<Guid, ModelCheckpoint>();

        // Setup default behavior for SaveAsync
        _mockStore.Setup(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<ModelCheckpoint, CancellationToken>((checkpoint, ct) =>
            {
                _savedCheckpoints[checkpoint.ModelId] = checkpoint;
            })
            .Returns(Task.CompletedTask);

        // Setup default behavior for LoadAsync
        _mockStore.Setup(x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
            {
                _savedCheckpoints.TryGetValue(id, out var checkpoint);
                return Task.FromResult(checkpoint);
            });

        // Setup default behavior for DeleteAsync
        _mockStore.Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, ct) => _savedCheckpoints.Remove(id))
            .Returns(Task.CompletedTask);

        // Setup default behavior for ListAsync
        _mockStore.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string?, string?, CancellationToken>((tagKey, tagValue, ct) =>
            {
                var ids = new List<Guid>(_savedCheckpoints.Keys);
                return Task.FromResult(ids);
            });

        _hyperParams = new HyperParameters { HiddenSize = 768 };
        _tokenizer = new TokenizerData { Type = "BPE" };
        _weights = new byte[] { 1, 2, 3 };
        _optimizer = new byte[] { 4, 5, 6 };
    }

    // -------------------- Constructors --------------------

    [Fact]
    public void Constructor_WithStore_ShouldSetStore()
    {
        // Act
        var manager = new CheckpointManager(_mockStore.Object);

        // Assert
        var id = manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f).GetAwaiter().GetResult();
        _mockStore.Verify(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _savedCheckpoints.Keys.Should().Contain(id);
    }

    [Fact]
    public async Task Constructor_WithBackgroundOptions_ShouldCreateBackgroundSaver()
    {
        // Arrange
        var saveCalled = new TaskCompletionSource<bool>();
        var mockStore = new Mock<IModelStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback(() => saveCalled.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var options = new BackgroundSaveOptions { Enabled = true, QueueCapacity = 3 };
        var manager = new CheckpointManager(mockStore.Object, options);

        // Act – SaveAsync returns immediately (fire‑and‑forget)
        var saveTask = manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f);

        // Assert – SaveAsync completed synchronously
        saveTask.IsCompletedSuccessfully.Should().BeTrue();

        // Store should NOT have been called yet (background hasn't run)
        saveCalled.Task.IsCompleted.Should().BeFalse();

        // Flush the background queue
        await ((IAsyncDisposable)manager).DisposeAsync();

        // Now the store should have been called
        saveCalled.Task.IsCompleted.Should().BeTrue();
    }

    // -------------------- SaveAsync (Synchronous Mode) --------------------

    [Fact]
    public async Task SaveAsync_Synchronous_ShouldSaveCheckpoint()
    {
        // Arrange
        var manager = new CheckpointManager(_mockStore.Object);

        // Act
        var id = await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 5, 2.345f);

        // Assert
        _mockStore.Verify(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        var saved = _savedCheckpoints[id];
        saved.WeightsBytes.Should().BeEquivalentTo(_weights);
        saved.OptimizerBytes.Should().BeEquivalentTo(_optimizer);
        saved.HyperParams.Should().BeEquivalentTo(_hyperParams);
        saved.Tokenizer.Should().BeEquivalentTo(_tokenizer);
        saved.CurrentEpoch.Should().Be(5);
        saved.LastTrainingLoss.Should().Be(2.345f);
    }

    [Fact]
    public async Task SaveAsync_Synchronous_WithExistingId_ShouldUseProvidedId()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var manager = new CheckpointManager(_mockStore.Object);

        // Act
        var id = await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f, existingId);

        // Assert
        id.Should().Be(existingId);
        _mockStore.Verify(x => x.SaveAsync(It.Is<ModelCheckpoint>(c => c.ModelId == existingId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_Synchronous_WithTags_ShouldStoreTags()
    {
        // Arrange
        var tags = new Dictionary<string, string> { { "key", "value" } };
        var manager = new CheckpointManager(_mockStore.Object);

        // Act
        await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f, tags: tags);

        // Assert
        _mockStore.Verify(x => x.SaveAsync(It.Is<ModelCheckpoint>(c => c.Tags == tags), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_Synchronous_ShouldRespectCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var manager = new CheckpointManager(_mockStore.Object);

        // Act
        var act = async () => await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f, cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockStore.Verify(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------- SaveAsync (Background Mode) --------------------

    [Fact]
    public async Task SaveAsync_Background_ShouldReturnImmediatelyAndSaveInBackground()
    {
        // Arrange - Use a ManualResetEvent or TaskCompletionSource to block the save
        var saveCompleted = new TaskCompletionSource<bool>();
        var mockStore = new Mock<IModelStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<ModelCheckpoint, CancellationToken>((c, ct) =>
            {
                // Simulate work
                saveCompleted.SetResult(true);
            })
            .Returns(Task.CompletedTask);

        var options = new BackgroundSaveOptions { Enabled = true };
        var manager = new CheckpointManager(mockStore.Object, options);

        // Act
        var id = await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f);
        id.Should().NotBe(Guid.Empty);

        // Wait for background save to complete
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - store was called
        mockStore.Verify(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task SaveAsync_Background_ShouldPerformDeepCopy()
    {
        // Arrange
        var capturedCheckpoint = (ModelCheckpoint?)null;
        var mockStore = new Mock<IModelStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<ModelCheckpoint, CancellationToken>((c, ct) => capturedCheckpoint = c)
            .Returns(Task.CompletedTask);

        var options = new BackgroundSaveOptions { Enabled = true };
        var manager = new CheckpointManager(mockStore.Object, options);

        var mutableWeights = new byte[] { 10, 20, 30 };
        var mutableOptimizer = new byte[] { 40, 50, 60 };

        // Act
        await manager.SaveAsync(mutableWeights, mutableOptimizer, _hyperParams, _tokenizer, 1, 0.5f);
        mutableWeights[0] = 99; // Mutate after save call
        mutableOptimizer[0] = 99;

        // Wait for background to process
        await Task.Delay(200);

        // Assert - saved bytes should be the original, not mutated
        capturedCheckpoint.Should().NotBeNull();
        capturedCheckpoint!.WeightsBytes.Should().BeEquivalentTo(new byte[] { 10, 20, 30 });
        capturedCheckpoint.OptimizerBytes.Should().BeEquivalentTo(new byte[] { 40, 50, 60 });

        await manager.DisposeAsync();
    }

    [Fact]
    public async Task SaveAsync_Background_WithErrorCallback_ShouldInvokeOnError()
    {
        // Arrange
        Exception? captured = null;
        var options = new BackgroundSaveOptions
        {
            Enabled = true,
            OnError = ex => captured = ex
        };

        var mockStore = new Mock<IModelStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.SaveAsync(It.IsAny<ModelCheckpoint>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Save failed"));

        var manager = new CheckpointManager(mockStore.Object, options);

        // Act
        await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f);

        // Wait for background to process
        await Task.Delay(200);

        // Assert
        captured.Should().NotBeNull();
        captured.Should().BeOfType<InvalidOperationException>();
        captured!.Message.Should().Be("Save failed");

        await manager.DisposeAsync();
    }

    // -------------------- LoadAsync --------------------

    [Fact]
    public async Task LoadAsync_ShouldReturnCheckpointFromStore()
    {
        // Arrange
        var manager = new CheckpointManager(_mockStore.Object);
        var id = await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f);

        // Reset the mock call counter for LoadAsync
        _mockStore.Invocations.Clear();

        // Act
        var loaded = await manager.LoadAsync(id);

        // Assert
        _mockStore.Verify(x => x.LoadAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        loaded.Should().NotBeNull();
        loaded!.ModelId.Should().Be(id);
    }

    [Fact]
    public async Task LoadAsync_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        var mockStore = new Mock<IModelStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) => Task.FromResult<ModelCheckpoint?>(null));

        var manager = new CheckpointManager(mockStore.Object);

        // Act
        var loaded = await manager.LoadAsync(Guid.NewGuid());

        // Assert
        loaded.Should().BeNull();
    }

    // -------------------- DeleteAsync --------------------

    [Fact]
    public async Task DeleteAsync_ShouldCallStoreDelete()
    {
        // Arrange
        var manager = new CheckpointManager(_mockStore.Object);
        var id = await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f);
        _savedCheckpoints.Keys.Should().Contain(id);

        _mockStore.Invocations.Clear();

        // Act
        await manager.DeleteAsync(id);

        // Assert
        _mockStore.Verify(x => x.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        _savedCheckpoints.Keys.Should().NotContain(id);
    }

    // -------------------- ListAsync --------------------

    [Fact]
    public async Task ListAsync_ShouldReturnAllIds()
    {
        // Arrange
        var manager = new CheckpointManager(_mockStore.Object);
        var id1 = await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 1, 0.5f);
        var id2 = await manager.SaveAsync(_weights, _optimizer, _hyperParams, _tokenizer, 2, 0.6f);

        _mockStore.Invocations.Clear();

        // Act
        var ids = await manager.ListAsync();

        // Assert
        _mockStore.Verify(x => x.ListAsync(null, null, It.IsAny<CancellationToken>()), Times.Once);
        ids.Should().BeEquivalentTo(new[] { id1, id2 });
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_ShouldPassTagsToStore()
    {
        // Arrange
        var manager = new CheckpointManager(_mockStore.Object);

        // Act
        await manager.ListAsync("key", "value");

        // Assert
        _mockStore.Verify(x => x.ListAsync("key", "value", It.IsAny<CancellationToken>()), Times.Once);
    }
}