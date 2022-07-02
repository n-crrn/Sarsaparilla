using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class DeconstructionRule : MutateRule
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

        Label = $"Deconst-{Id}-{DestinationCell.Name}";
    }

    private readonly string Id;

    public FunctionMessage SourceCellContaining(IMessage contents)
    {
        return new FunctionMessage(SourceCell.Name, new() { contents });
    }

    public FunctionMessage SourceCell { get; init; }

    public FunctionMessage DestinationCell { get; init; }

    #region IMutableRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        factory.RegisterPremises(Event.Know(SourceCell));
        return GenerateStateConsistentRule(factory, Event.Know(DestinationCell));
    }

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
