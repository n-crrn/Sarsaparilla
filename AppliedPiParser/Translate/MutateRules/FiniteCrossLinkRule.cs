using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

/// <summary>
/// Represents a rule to transfer values between a Read Socket's state and a Write Socket's
/// state.
/// </summary>
public class FiniteCrossLinkRule : MutateRule
{

    public FiniteCrossLinkRule(WriteSocket fromSocket, ReadSocket toSocket)
    {
        From = fromSocket;
        To = toSocket;
        Label = $"FinXLink:{From}-{To}";
        RecommendedDepth = 1;
    }

    public WriteSocket From { get; init; }

    public ReadSocket To { get; init; }

    #region IMutableRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        IMessage value = new VariableMessage("@v");
        Snapshot written = factory.RegisterState(From.WriteState(value));
        Snapshot waiting = factory.RegisterState(To.WaitingState());
        written.TransfersTo = From.WaitingState();
        waiting.TransfersTo = To.ReadState(value);
        return GenerateStateTransferringRule(factory);
    }

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
