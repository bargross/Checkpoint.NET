namespace StateCheckpoint.NET.Models;

public class TokenizerData
{
    // 1. The actual type (e.g., "BPE", "Unigram", "WordPiece")
    public string Type { get; set; } = "BPE";

    // 2. The core vocabulary: Token string -> Integer ID
    public Dictionary<string, int> TokenToId { get; set; } = new();

    // 3. Reverse lookup: Integer ID -> Token string
    // (Saves user from having to reverse the dictionary manually)
    public Dictionary<int, string> IdToToken { get; set; } = new();

    // 4. BPE Merge Rules (only relevant for BPE/ByteLevelBPE).
    //    Order matters! The order of merges defines the tokenization priority.
    public List<(string Left, string Right)>? MergeRules { get; set; }

    // 5. Optional: Unigram log-probabilities (only used for Unigram tokenizers).
    //    Make it nullable to keep the JSON small for BPE users.
    public Dictionary<string, double>? TokenLogProbabilities { get; set; }

    // 6. Explicit Special Tokens (solves your "where is BOS?" problem).
    //    e.g., { "bos": 1, "eos": 2, "pad": 0, "unk": 3 }
    public Dictionary<string, int> SpecialTokens { get; set; } = new()
    {
        { "bos", 1 }, { "eos", 2 }, { "pad", 0 }, { "unk", 3 } // sane defaults
    };

    // 7. Optional byte-fallback mapping (for byte-level BPE like Llama/GPT-4)
    //    If the user uses byte-fallback, store the byte -> token mapping here.
    public Dictionary<byte, int>? ByteToToken { get; set; }

    // Helper method for the user to easily get the EOS id
    public int GetSpecialTokenId(string name, int defaultValue = -1)
    {
        return SpecialTokens.TryGetValue(name, out var id) ? id : defaultValue;
    }
}
