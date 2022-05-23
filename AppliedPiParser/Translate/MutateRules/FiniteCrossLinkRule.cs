using System.Diagnostics;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class FiniteCrossLinkRule : IMutateRule
{

    public FiniteCrossLinkRule(WriteSocket fromSocket, ReadSocket toSocket)
    {
        From = fromSocket;
        To = toSocket;
        Debug.Assert(!(From.IsInfinite && To.IsInfinite));
    }

    public WriteSocket From { get; init; }

    public ReadSocket To { get; init; }

    #region IMutableRule implementation.

    public string Label => $"FinXLink:{From}-{To}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        IMessage value = new VariableMessage("@v");
        factory.GuardStatements = Conditions?.CreateGuard();
        Snapshot written = factory.RegisterState(From.WriteState(value));
        Snapshot waiting = factory.RegisterState(To.WaitingState());
        written.TransfersTo = From.WaitingState();
        waiting.TransfersTo = To.ReadState(value);
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    public int RecommendedDepth => 1;

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Finite cross-link between {From} and {To}.";

    public override bool Equals(object? obj)
    {
        return obj is FiniteCrossLinkRule fr &&
            From.Equals(fr.From) &&
            To.Equals(fr.To) &&
            Equals(Conditions, fr.Conditions);
    }

    public override int GetHashCode() => From.GetHashCode() + To.GetHashCode();

    #endregion

}
