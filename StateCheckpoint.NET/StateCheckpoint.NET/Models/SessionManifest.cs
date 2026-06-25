using StateCheckpoint.NET.Models;

namespace StateCheckpoint.NET.Stores;

internal class SessionManifest
{
    public string ModelFingerprint { get; set; } = string.Empty;
    public int[] TokenHistory { get; set; } = Array.Empty<int>();
    public SamplingData SamplingConfig { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
}