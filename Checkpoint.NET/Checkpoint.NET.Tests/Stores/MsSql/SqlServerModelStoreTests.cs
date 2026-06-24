using Checkpoint.NET.Models;
using Checkpoint.NET.Tests.Stores.SqlServer;
using FluentAssertions;

namespace Checkpoint.NET.Tests.Stores.Mysql;

[Collection("NonParallel")]
public class SqlServerModelStoreTests : SqlServerTestBase
{
    private ModelCheckpoint CreateTestCheckpoint(Guid? id = null)
    {
        return new ModelCheckpoint
        {
            ModelId = id ?? Guid.NewGuid(),
            WeightsBytes = new byte[] { 10, 20, 30, 40 },
            OptimizerBytes = new byte[] { 50, 60, 70 },
            HyperParams = new HyperParameters { HiddenSize = 768, NumLayers = 12 },
            Tokenizer = new TokenizerData
            {
                Type = "BPE",
                TokenToId = new Dictionary<string, int> { { "hello", 100 } }
            },
            CurrentEpoch = 3,
            LastTrainingLoss = 1.234f,
            CreatedAt = DateTime.UtcNow,
            Tags = new Dictionary<string, string> { { "env", "test" }, { "model", "gpt" } }
        };
    }

    [Fact]
    public async Task EnsureSchemaAsync_ShouldBeIdempotent()
    {
        var act = async () => await ModelStore.EnsureSchemaAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveAsync_ShouldInsertCheckpoint()
    {
        // Arrange
        var checkpoint = CreateTestCheckpoint();

        // Act
        await ModelStore.SaveAsync(checkpoint);

        // Assert
        var loaded = await ModelStore.LoadAsync(checkpoint.ModelId);
        loaded.Should().NotBeNull();
        loaded.ModelId.Should().Be(checkpoint.ModelId);
        loaded.WeightsBytes.Should().BeEquivalentTo(checkpoint.WeightsBytes);
        loaded.OptimizerBytes.Should().BeEquivalentTo(checkpoint.OptimizerBytes);
        loaded.HyperParams.Should().BeEquivalentTo(checkpoint.HyperParams);
        loaded.Tokenizer.Should().BeEquivalentTo(checkpoint.Tokenizer);
        loaded.CurrentEpoch.Should().Be(checkpoint.CurrentEpoch);
        loaded.LastTrainingLoss.Should().Be(checkpoint.LastTrainingLoss);
        loaded.CreatedAt.Should().BeCloseTo(checkpoint.CreatedAt, TimeSpan.FromSeconds(1));
        loaded.Tags.Should().BeEquivalentTo(checkpoint.Tags);
    }

    [Fact]
    public async Task SaveAsync_ShouldUpdateExistingCheckpoint()
    {
        // Arrange
        var id = Guid.NewGuid();
        var original = CreateTestCheckpoint(id);
        await ModelStore.SaveAsync(original);

        var updated = new ModelCheckpoint
        {
            ModelId = id,
            WeightsBytes = new byte[] { 99, 88 },
            OptimizerBytes = new byte[] { 77, 66 },
            HyperParams = new HyperParameters { HiddenSize = 1024 },
            Tokenizer = new TokenizerData { Type = "Unigram" },
            CurrentEpoch = 10,
            LastTrainingLoss = 0.999f,
            CreatedAt = DateTime.UtcNow,
            Tags = new Dictionary<string, string> { { "updated", "true" } }
        };

        // Act
        await ModelStore.SaveAsync(updated);

        // Assert
        var loaded = await ModelStore.LoadAsync(id);
        loaded.Should().NotBeNull();
        loaded.WeightsBytes.Should().BeEquivalentTo(updated.WeightsBytes);
        loaded.OptimizerBytes.Should().BeEquivalentTo(updated.OptimizerBytes);
        loaded.HyperParams.Should().BeEquivalentTo(updated.HyperParams);
        loaded.Tokenizer.Should().BeEquivalentTo(updated.Tokenizer);
        loaded.CurrentEpoch.Should().Be(updated.CurrentEpoch);
        loaded.LastTrainingLoss.Should().Be(updated.LastTrainingLoss);
        loaded.Tags.Should().BeEquivalentTo(updated.Tags);
    }

    [Fact]
    public async Task LoadAsync_WhenNotFound_ShouldReturnNull()
    {
        var loaded = await ModelStore.LoadAsync(Guid.NewGuid());
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteCheckpoint()
    {
        // Arrange
        var checkpoint = CreateTestCheckpoint();
        await ModelStore.SaveAsync(checkpoint);

        // Act
        await ModelStore.DeleteAsync(checkpoint.ModelId);

        // Assert
        var loaded = await ModelStore.LoadAsync(checkpoint.ModelId);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WhenNotExists_ShouldNotThrow()
    {
        var act = async () => await ModelStore.DeleteAsync(Guid.NewGuid());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListAsync_ShouldReturnAllModelIds()
    {
        // Arrange
        var id1 = await SaveAndReturnId();
        var id2 = await SaveAndReturnId();

        // Act
        var ids = await ModelStore.ListAsync();

        // Assert
        ids.Should().Contain(id1);
        ids.Should().Contain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_ShouldFilterByTag()
    {
        // Arrange
        var id1 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "key", "value1" } });
        var id2 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "key", "value2" } });

        // Act
        var ids = await ModelStore.ListAsync("key", "value1");

        // Assert
        ids.Should().Contain(id1);
        ids.Should().NotContain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_WhenNoMatches_ShouldReturnEmpty()
    {
        // Arrange
        await SaveAndReturnId(tags: new Dictionary<string, string> { { "key", "value1" } });

        // Act
        var ids = await ModelStore.ListAsync("key", "nonexistent");

        // Assert
        ids.Should().BeEmpty();
    }

    private async Task<Guid> SaveAndReturnId(Dictionary<string, string>? tags = null)
    {
        var checkpoint = CreateTestCheckpoint();
        if (tags != null)
            checkpoint.Tags = tags;
        await ModelStore.SaveAsync(checkpoint);
        return checkpoint.ModelId;
    }
}