namespace StateCheckpoint.NET.Models;

public class ModelCheckpoint
{
    // The primary key for managing multiple models
    public Guid ModelId { get; set; } = Guid.NewGuid();

    // 1. Model Weights (The neural network parameters)
    public byte[] WeightsBytes { get; set; } = Array.Empty<byte>();

    // 2. Optimizer State (Momentum, variance, etc.)
    public byte[] OptimizerBytes { get; set; } = Array.Empty<byte>();

    // 3. Hyperparameters & Tokenizer (Stored as structured POCOs -> JSON)
    public HyperParameters HyperParams { get; set; } = new();
    public TokenizerData Tokenizer { get; set; } = new();

    // Training Lifecycle Metadata
    public int CurrentEpoch { get; set; }
    public float LastTrainingLoss { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // User-defined search tags (e.g., "Llama-7B", "FineTune-SQL", "Checkpoint_100")
    public Dictionary<string, string> Tags { get; set; } = new();
}

