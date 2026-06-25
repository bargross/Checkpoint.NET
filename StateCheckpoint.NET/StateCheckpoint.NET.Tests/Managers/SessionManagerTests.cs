using StateCheckpoint.NET.Manager;
using StateCheckpoint.NET.Models;
using StateCheckpoint.NET.Settings;
using StateCheckpoint.NET.Stores;
using FluentAssertions;
using Moq;

namespace StateCheckpoint.NET.Tests.Manager;

public class SessionManagerTests
{
    private readonly Mock<ISessionStore> _mockStore;
    private readonly byte[] _kvCache;
    private readonly int[] _tokenHistory;
    private readonly string _modelFingerprint;
    private readonly SamplingData _samplingConfig;
    private readonly Guid _sessionId;
    private readonly Dictionary<Guid, SessionCheckpoint> _savedSessions;

    public SessionManagerTests()
    {
        _mockStore = new Mock<ISessionStore>(MockBehavior.Strict);
        _savedSessions = new Dictionary<Guid, SessionCheckpoint>();

        // Default setup for SaveAsync
        _mockStore.Setup(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SessionCheckpoint, CancellationToken>((session, ct) =>
            {
                _savedSessions[session.SessionId] = session;
            })
            .Returns(Task.CompletedTask);

        // Default setup for LoadAsync
        _mockStore.Setup(x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) =>
            {
                _savedSessions.TryGetValue(id, out var session);
                return Task.FromResult(session);
            });

        // Default setup for DeleteAsync
        _mockStore.Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, ct) => _savedSessions.Remove(id))
            .Returns(Task.CompletedTask);

        // Default setup for ListAsync
        _mockStore.Setup(x => x.ListAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string?, string?, CancellationToken>((tagKey, tagValue, ct) =>
            {
                var ids = new List<Guid>(_savedSessions.Keys);
                return Task.FromResult(ids);
            });

        _kvCache = new byte[] { 10, 20, 30, 40 };
        _tokenHistory = new int[] { 1, 2, 3, 100, 200 };
        _modelFingerprint = "llama-2-7b-v1";
        _samplingConfig = new SamplingData { Temperature = 0.8f, TopP = 0.95f };
        _sessionId = Guid.NewGuid();
    }

    // -------------------- Constructors --------------------

    [Fact]
    public void Constructor_WithStore_ShouldSetStore()
    {
        // Act
        var manager = new SessionManager(_mockStore.Object);

        // Assert
        var id = manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint).GetAwaiter().GetResult();
        _mockStore.Verify(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        _savedSessions.Keys.Should().Contain(id);
    }

    [Fact]
    public async Task Constructor_WithBackgroundOptions_ShouldCreateBackgroundSaver()
    {
        // Arrange
        var saveCalled = new TaskCompletionSource<bool>();
        var mockStore = new Mock<ISessionStore>(MockBehavior.Loose);

        // Use a ManualResetEventSlim to block the worker thread.
        using var blockEvent = new ManualResetEventSlim(false);

        // Setup the mock's SaveAsync to block until we signal the event.
        mockStore.Setup(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SessionCheckpoint, CancellationToken>((s, ct) =>
            {
                // Block the worker thread until we release it.
                blockEvent.Wait(ct);
                saveCalled.TrySetResult(true);
            })
            .Returns(Task.CompletedTask);

        var options = new BackgroundSaveOptions { Enabled = true, QueueCapacity = 1 };
        var manager = new SessionManager(mockStore.Object, options);

        // Act – SaveAsync enqueues the save, but the worker is blocked on the event.
        var saveTask = manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint);

        // Assert – SaveAsync completed synchronously (fire-and-forget).
        saveTask.IsCompletedSuccessfully.Should().BeTrue();

        // The worker is blocked, so the save should NOT have been called yet.
        saveCalled.Task.IsCompleted.Should().BeFalse();

        // Now release the worker by signaling the event.
        blockEvent.Set();

        // Wait for the background save to complete.
        await saveCalled.Task.TimeoutAfter(2000);

        // The save should have been called.
        saveCalled.Task.IsCompleted.Should().BeTrue();

        // Clean up
        await manager.DisposeAsync();
    }

    // -------------------- SaveAsync (Synchronous) --------------------

    [Fact]
    public async Task SaveAsync_Synchronous_ShouldSaveSession()
    {
        // Arrange
        var manager = new SessionManager(_mockStore.Object);

        // Act
        var id = await manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint, _samplingConfig);

        // Assert
        _mockStore.Verify(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        var saved = _savedSessions[id];
        saved.KvCacheBytes.Should().BeEquivalentTo(_kvCache);
        saved.TokenHistory.Should().BeEquivalentTo(_tokenHistory);
        saved.ModelFingerprint.Should().Be(_modelFingerprint);
        saved.SamplingConfig.Temperature.Should().Be(_samplingConfig.Temperature);
    }

    [Fact]
    public async Task SaveAsync_Synchronous_WithTags_ShouldStoreTags()
    {
        // Arrange
        var tags = new Dictionary<string, string> { { "User", "Alice" } };
        var manager = new SessionManager(_mockStore.Object);

        // Act
        await manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint, tags: tags);

        // Assert
        _mockStore.Verify(x => x.SaveAsync(
            It.Is<SessionCheckpoint>(s => s.Tags == tags),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_Synchronous_ShouldRespectCancellation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var manager = new SessionManager(_mockStore.Object);

        // Act
        var act = async () => await manager.SaveAsync(
            _sessionId, _kvCache, _tokenHistory, _modelFingerprint,
            cancellationToken: cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        _mockStore.Verify(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_Synchronous_WhenSamplingConfigNull_UsesDefault()
    {
        // Arrange
        var manager = new SessionManager(_mockStore.Object);

        // Act
        var id = await manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint, samplingConfig: null);

        // Assert
        _mockStore.Verify(x => x.SaveAsync(
            It.Is<SessionCheckpoint>(s => s.SamplingConfig.Temperature == 0.7f && s.SamplingConfig.TopP == 0.9f),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------- SaveAsync (Background) --------------------

    [Fact]
    public async Task SaveAsync_Background_ShouldReturnImmediatelyAndSaveInBackground()
    {
        // Arrange
        var saveCompleted = new TaskCompletionSource<bool>();
        var mockStore = new Mock<ISessionStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SessionCheckpoint, CancellationToken>((s, ct) => saveCompleted.SetResult(true))
            .Returns(Task.CompletedTask);

        var options = new BackgroundSaveOptions { Enabled = true };
        var manager = new SessionManager(mockStore.Object, options);

        // Act
        var id = await manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint);
        id.Should().NotBe(Guid.Empty);

        // Wait for background
        await saveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        mockStore.Verify(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()), Times.Once);
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task SaveAsync_Background_ShouldPerformDeepCopy()
    {
        // Arrange
        SessionCheckpoint? captured = null;
        var mockStore = new Mock<ISessionStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()))
            .Callback<SessionCheckpoint, CancellationToken>((s, ct) => captured = s)
            .Returns(Task.CompletedTask);

        var options = new BackgroundSaveOptions { Enabled = true };
        var manager = new SessionManager(mockStore.Object, options);
        var mutableKv = new byte[] { 50, 60, 70, 80 };

        // Act
        await manager.SaveAsync(_sessionId, mutableKv, _tokenHistory, _modelFingerprint);
        mutableKv[0] = 99; // Mutate

        // Wait for background
        await Task.Delay(200);

        // Assert
        captured.Should().NotBeNull();
        captured!.KvCacheBytes.Should().BeEquivalentTo(new byte[] { 50, 60, 70, 80 });
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
        var mockStore = new Mock<ISessionStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.SaveAsync(It.IsAny<SessionCheckpoint>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Save failed"));

        var manager = new SessionManager(mockStore.Object, options);

        // Act
        await manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint);
        await Task.Delay(200);

        // Assert
        captured.Should().NotBeNull();
        captured.Should().BeOfType<InvalidOperationException>();
        captured!.Message.Should().Be("Save failed");
        await manager.DisposeAsync();
    }

    // -------------------- LoadAsync --------------------

    [Fact]
    public async Task LoadAsync_ShouldReturnSessionFromStore()
    {
        // Arrange
        var manager = new SessionManager(_mockStore.Object);
        var id = await manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint);
        _mockStore.Invocations.Clear();

        // Act
        var loaded = await manager.LoadAsync(id);

        // Assert
        _mockStore.Verify(x => x.LoadAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        loaded.Should().NotBeNull();
        loaded!.SessionId.Should().Be(id);
    }

    [Fact]
    public async Task LoadAsync_WhenNotFound_ShouldReturnNull()
    {
        // Arrange
        var mockStore = new Mock<ISessionStore>(MockBehavior.Loose);
        mockStore.Setup(x => x.LoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((id, ct) => Task.FromResult<SessionCheckpoint?>(null));

        var manager = new SessionManager(mockStore.Object);

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
        var manager = new SessionManager(_mockStore.Object);
        var id = await manager.SaveAsync(_sessionId, _kvCache, _tokenHistory, _modelFingerprint);
        _savedSessions.Keys.Should().Contain(id);
        _mockStore.Invocations.Clear();

        // Act
        await manager.DeleteAsync(id);

        // Assert
        _mockStore.Verify(x => x.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        _savedSessions.Keys.Should().NotContain(id);
    }

    // -------------------- ListAsync --------------------

    [Fact]
    public async Task ListAsync_ShouldReturnAllIds()
    {
        // Arrange
        var manager = new SessionManager(_mockStore.Object);
        var id1 = await manager.SaveAsync(Guid.NewGuid(), _kvCache, _tokenHistory, _modelFingerprint);
        var id2 = await manager.SaveAsync(Guid.NewGuid(), _kvCache, _tokenHistory, _modelFingerprint);
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
        var manager = new SessionManager(_mockStore.Object);

        // Act
        await manager.ListAsync("User", "Alice");

        // Assert
        _mockStore.Verify(x => x.ListAsync("User", "Alice", It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------- DisposeAsync --------------------

    [Fact]
    public async Task DisposeAsync_ShouldDisposeBackgroundSaver()
    {
        // Arrange
        var options = new BackgroundSaveOptions { Enabled = true };
        var manager = new SessionManager(_mockStore.Object, options);

        // Act
        await manager.DisposeAsync();

        // Assert - verify the store wasn't harmed, and disposing twice is safe
        await manager.DisposeAsync(); // Should not throw
    }
}