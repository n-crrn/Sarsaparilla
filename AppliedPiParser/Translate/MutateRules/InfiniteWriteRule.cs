using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteWriteRule : IMutateRule
{
    public InfiniteWriteRule(
        WriteSocket s,
        IDictionary<Socket, int> finActionCounts,
        HashSet<Event> premises,
        IMessage value)
    {
        From = s;
        FiniteActionCounts = new Dictionary<Socket, int>(finActionCounts);
        Premises = new(premises); // Copy, so additional premises are not added.
        ValueToWrite = value;
    }

    public WriteSocket From { get; init; }

    public IDictionary<Socket, int> FiniteActionCounts { get; init; }

    public HashSet<Event> Premises { get; init; }

    public IMessage ValueToWrite { get; init; }

    #region IMutateRule implementation.

    public string Label => $"InfWrite:{From}-{ValueToWrite}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        foreach ((Socket s, int ic) in FiniteActionCounts)
        {
            s.RegisterHistory(factory, ic);
        }
        Snapshot latest = factory.RegisterState(From.WaitingState());
        factory.RegisterPremises(latest, Premises);
        latest.TransfersTo = From.WriteState(ValueToWrite);
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    public int RecommendedDepth => 2;

    #endregion
    #region Basic object override.

    public override string ToString() => $"Write to infinite socket rule for {From} with {ValueToWrite}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteWriteRule r &&
            From.Equals(r.From) &&
            FiniteActionCounts.ToHashSet().SetEquals(r.FiniteActionCounts) &&
            Premises.SetEquals(r.Premises) &&
            ValueToWrite.Equals(r.ValueToWrite) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => From.GetHashCode();

    #endregion

}
