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

    public Rule GenerateRule(RuleFactory factory)
    {
        IMessage value = new VariableMessage("_v");
        Snapshot written = factory.RegisterState(From.WriteState(value));
        Snapshot waiting = factory.RegisterState(To.WaitingState());
        written.TransfersTo = From.WaitingState();
        waiting.TransfersTo = To.ReadState(value);
        return factory.CreateStateTransferringRule();
    }

    #region Basic object overrides.

    public override string ToString() => $"Finite cross-link between {From} and {To}.";

    public override bool Equals(object? obj) => obj is FiniteCrossLinkRule fr && From.Equals(fr.From) && To.Equals(fr.To);

    public override int GetHashCode() => From.GetHashCode() + To.GetHashCode();

    #endregion

}
