namespace StateCheckpoint.NET.Models;

public class SessionCheckpoint
{
    public Guid SessionId { get; set; } = Guid.NewGuid();

    // Hash of the model file to prevent loading a session into the wrong model
    public string ModelFingerprint { get; set; } = string.Empty;

    // The raw KV-Cache bytes (engine-specific)
    public byte[] KvCacheBytes { get; set; } = Array.Empty<byte>();

    // The token history (so you can rebuild prompt context if needed)
    public int[] TokenHistory { get; set; } = Array.Empty<int>();

    // Sampling parameters
    public SamplingData SamplingConfig { get; set; } = new();

    // Timestamp
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    // Optional user tags
    public Dictionary<string, string> Tags { get; set; } = new();
}
