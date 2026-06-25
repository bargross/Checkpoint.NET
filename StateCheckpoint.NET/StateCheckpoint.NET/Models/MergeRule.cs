namespace StateCheckpoint.NET.Models;

public class MergeRule
{
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;

    public MergeRule() { }
    public MergeRule(string left, string right) { Left = left; Right = right; }
}
