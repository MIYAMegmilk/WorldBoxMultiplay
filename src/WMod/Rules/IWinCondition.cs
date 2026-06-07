namespace WMod.Rules;

public class MatchResult
{
    public int winnerId { get; set; } = -1;
    public string reason { get; set; }
}

public interface IWinCondition
{
    string Name { get; }
    MatchResult Evaluate();
}
