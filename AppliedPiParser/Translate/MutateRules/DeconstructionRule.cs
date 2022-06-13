using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class DeconstructionRule : IMutateRule
{

    public DeconstructionRule(
        string uniqueId, 
        IMessage lhs,
        IMessage rhs,
        string destCellName)
    {
        Id = uniqueId;
        SourceCell = new($"destr@{Id}", new() { lhs });
        DestinationCell = new(destCellName, new() { rhs });
    }

    private readonly string Id;

    public FunctionMessage SourceCellContaining(IMessage contents)
    {
        return new FunctionMessage(SourceCell.Name, new() { contents });
    }

    public FunctionMessage SourceCell { get; init; }

    public FunctionMessage DestinationCell { get; init; }

    #region IMutableRule implementation.

    public string Label => $"Deconst-{Id}-{DestinationCell.Name}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        factory.RegisterPremises(Event.Know(SourceCell));
        factory.GuardStatements = Conditions?.CreateGuard();
        Rule r = factory.CreateStateConsistentRule(Event.Know(DestinationCell));
        return IfBranchConditions.ApplyReplacements(Conditions, r);
    }

    public int RecommendedDepth => 0; // Does not lead to new Frame creation.

    #endregion
    #region Basic object override.

    public override string ToString() => $"Destructor {Id} to {DestinationCell}";

    public override bool Equals(object? obj)
    {
        return obj is DeconstructionRule r 
            && Id == r.Id 
            && SourceCell.Equals(r.SourceCell) 
            && DestinationCell.Equals(r.DestinationCell);
    }

    public override int GetHashCode() => SourceCell.GetHashCode();

    #endregion

}
