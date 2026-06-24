using Checkpoint.NET.Manager;
using Checkpoint.NET.Models;
using FluentAssertions;

namespace Checkpoint.NET.Tests.Integration;

[Collection("NonParallel")]
public abstract class IntegrationTestsBase : IAsyncLifetime
{
    protected CheckpointManager CheckpointManager { get; set; } = null!;
    protected SessionManager SessionManager { get; set; } = null!;

    // Derived classes must implement these to provide the specific stores.
    protected abstract Task InitializeStoresAsync();
    protected abstract Task CleanupStoresAsync();

    public async Task InitializeAsync()
    {
        await InitializeStoresAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupStoresAsync();
    }

    // --- Shared integration tests (will be called by derived classes) ---

    protected async Task RoundTrip_Checkpoint_ShouldSaveLoadDeleteList()
    {
        // Arrange
        var weights = new byte[] { 1, 2, 3, 4 };
        var optimizer = new byte[] { 5, 6, 7 };
        var hyperParams = new HyperParameters { HiddenSize = 768 };
        var tokenizer = new TokenizerData { Type = "BPE" };
        var tags = new Dictionary<string, string> { { "env", "integration" } };

        // Act - Save
        var modelId = await CheckpointManager.SaveAsync(weights, optimizer, hyperParams, tokenizer, epoch: 1, loss: 0.5f, tags: tags);

        // Assert - Load
        var loaded = await CheckpointManager.LoadAsync(modelId);

        loaded.Should().NotBeNull();
        loaded.ModelId.Should().Be(modelId);
        loaded.WeightsBytes.Should().BeEquivalentTo(weights);
        loaded.OptimizerBytes.Should().BeEquivalentTo(optimizer);
        loaded.HyperParams.Should().BeEquivalentTo(hyperParams);
        loaded.Tokenizer.Should().BeEquivalentTo(tokenizer);
        loaded.CurrentEpoch.Should().Be(1);
        loaded.LastTrainingLoss.Should().Be(0.5f);
        loaded.Tags.Should().BeEquivalentTo(tags);

        // Assert - List
        var ids = await CheckpointManager.ListAsync();
        ids.Should().Contain(modelId);

        // Act - Delete
        await CheckpointManager.DeleteAsync(modelId);

        // Assert - Deleted
        var deleted = await CheckpointManager.LoadAsync(modelId);
        deleted.Should().BeNull();

        var idsAfterDelete = await CheckpointManager.ListAsync();
        idsAfterDelete.Should().NotContain(modelId);
    }

    protected async Task RoundTrip_Session_ShouldSaveLoadDeleteList()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var kvCache = new byte[] { 100, 200, 230 };
        var tokenHistory = new int[] { 1, 2, 3 };
        var fingerprint = "test-model";
        var sampling = new SamplingData { Temperature = 0.8f };
        var tags = new Dictionary<string, string> { { "user", "alice" } };

        // Act - Save
        var returnedId = await SessionManager.SaveAsync(sessionId, kvCache, tokenHistory, fingerprint, sampling, tags);

        // Assert
        returnedId.Should().Be(sessionId);

        // Load
        var loaded = await SessionManager.LoadAsync(sessionId);

        loaded.Should().NotBeNull();
        loaded.SessionId.Should().Be(sessionId);
        loaded.KvCacheBytes.Should().BeEquivalentTo(kvCache);
        loaded.TokenHistory.Should().BeEquivalentTo(tokenHistory);
        loaded.ModelFingerprint.Should().Be(fingerprint);
        loaded.SamplingConfig.Should().BeEquivalentTo(sampling);
        loaded.Tags.Should().BeEquivalentTo(tags);

        // List
        var ids = await SessionManager.ListAsync();
        ids.Should().Contain(sessionId);

        // Delete
        await SessionManager.DeleteAsync(sessionId);
        var deleted = await SessionManager.LoadAsync(sessionId);
        deleted.Should().BeNull();

        var idsAfterDelete = await SessionManager.ListAsync();
        idsAfterDelete.Should().NotContain(sessionId);
    }
}