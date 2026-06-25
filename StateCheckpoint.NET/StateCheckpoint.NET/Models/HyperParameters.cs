namespace StateCheckpoint.NET.Models;

public class HyperParameters
{
    public int HiddenSize { get; set; } = 4096;
    public int NumLayers { get; set; } = 32;
    public int NumHeads { get; set; } = 32;
    public float LearningRate { get; set; } = 3e-4f;
    public int BatchSize { get; set; } = 8;
    public int ContextLength { get; set; } = 2048;
    public string ActivationFunction { get; set; } = "Swish";

    // Add whatever your custom engine uses.
    public Dictionary<string, object> Extras { get; set; } = new();
}

