using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

/// <summary>
/// Represents a rule that allows the attacker to know the contents of a socket when there is a
/// write to a socket of channel that the attacker knows.
/// </summary>
public class KnowChannelContentRule : IMutateRule
{

    public KnowChannelContentRule(WriteSocket s)
    {
        Socket = s;
    }

    public WriteSocket Socket;

    private static readonly IMessage InternalVariable = new VariableMessage("_v");

    public string Label => $"Know:{Socket}";

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot ss = factory.RegisterState(Socket.WriteState(InternalVariable));
        factory.RegisterPremises(ss, Event.Know(new NameMessage(Socket.ChannelName)));
        return factory.CreateStateConsistentRule(Event.Know(InternalVariable));
    }

    #region Basic object overrides.

    public override string ToString() => $"Know rule for {Socket}.";

    public override bool Equals(object? obj) => obj is KnowChannelContentRule r && Socket.Equals(r.Socket);

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
