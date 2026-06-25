using StateCheckpoint.NET.Models;
using FluentAssertions;

namespace StateCheckpoint.NET.Tests.Stores.Postgres;

[Collection("NonParallel")]
public class PostgresModelStoreTests : PostgresTestBase
{
    private ModelCheckpoint CreateTestCheckpoint(Guid? id = null)
    {
        return new ModelCheckpoint
        {
            ModelId = id ?? Guid.NewGuid(),
            WeightsBytes = new byte[] { 10, 20, 30, 40, 50 },
            OptimizerBytes = new byte[] { 60, 70, 80 },
            HyperParams = new HyperParameters { HiddenSize = 768, NumLayers = 12 },
            Tokenizer = new TokenizerData
            {
                Type = "BPE",
                TokenToId = new Dictionary<string, int> { { "hello", 100 }, { "world", 200 } },
                SpecialTokens = new Dictionary<string, int> { { "bos", 0 }, { "eos", 1 } }
            },
            CurrentEpoch = 5,
            LastTrainingLoss = 2.345f,
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
        var checkpoint = CreateTestCheckpoint();

        await ModelStore.SaveAsync(checkpoint);

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
        var id = Guid.NewGuid();
        var original = CreateTestCheckpoint(id);
        await ModelStore.SaveAsync(original);

        var updated = new ModelCheckpoint
        {
            ModelId = id,
            WeightsBytes = new byte[] { 99, 88, 77 },
            OptimizerBytes = new byte[] { 66, 55 },
            HyperParams = new HyperParameters { HiddenSize = 1024, NumLayers = 24 },
            Tokenizer = new TokenizerData { Type = "Unigram" },
            CurrentEpoch = 10,
            LastTrainingLoss = 0.123f,
            CreatedAt = DateTime.UtcNow,
            Tags = new Dictionary<string, string> { { "updated", "true" } }
        };

        await ModelStore.SaveAsync(updated);

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
        var checkpoint = CreateTestCheckpoint();

        await ModelStore.SaveAsync(checkpoint);
        await ModelStore.DeleteAsync(checkpoint.ModelId);

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
        var id1 = await SaveAndReturnId();
        var id2 = await SaveAndReturnId();
        var ids = await ModelStore.ListAsync();

        ids.Should().Contain(id1);
        ids.Should().Contain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_ShouldFilterByTag()
    {
        var id1 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "gpt" } });
        var id2 = await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "bert" } });
        var ids = await ModelStore.ListAsync("type", "gpt");

        ids.Should().Contain(id1);
        ids.Should().NotContain(id2);
    }

    [Fact]
    public async Task ListAsync_WithTagFilter_WhenNoMatches_ShouldReturnEmpty()
    {
        await SaveAndReturnId(tags: new Dictionary<string, string> { { "type", "gpt" } });

        var ids = await ModelStore.ListAsync("type", "bert");

        ids.Should().BeEmpty();
    }

    private async Task<Guid> SaveAndReturnId(Dictionary<string, string>? tags = null)
    {
        var checkpoint = CreateTestCheckpoint();

        if (tags != null) checkpoint.Tags = tags;

        await ModelStore.SaveAsync(checkpoint);

        return checkpoint.ModelId;
    }
}