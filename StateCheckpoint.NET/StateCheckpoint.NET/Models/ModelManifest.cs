using StateCheckpoint.NET.Models;

namespace StateCheckpoint.NET.Stores;

internal class ModelManifest
{
    public HyperParameters HyperParams { get; set; } = new();
    public TokenizerData Tokenizer { get; set; } = new();
    public int CurrentEpoch { get; set; }
    public float LastTrainingLoss { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}