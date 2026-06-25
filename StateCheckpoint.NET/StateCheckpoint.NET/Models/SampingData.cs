namespace StateCheckpoint.NET.Models;

public class SamplingData
{
    public float Temperature { get; set; } = 0.7f;
    public float TopP { get; set; } = 0.9f;
    public int TopK { get; set; } = 40;
    public float RepeatPenalty { get; set; } = 1.1f;
    public int Seed { get; set; } = 42;
}
