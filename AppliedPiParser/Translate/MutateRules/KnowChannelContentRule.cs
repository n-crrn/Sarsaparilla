using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

/// <summary>
/// Represents a rule that allows the attacker to know the contents of a socket when there is a
/// write to a socket of channel that the attacker knows.
/// </summary>
public class KnowChannelContentRule : MutateRule
{

    public KnowChannelContentRule(WriteSocket s)
    {
        Socket = s;
        Label = $"Know:{Socket}";
    }

    public WriteSocket Socket;

    private static readonly IMessage InternalVariable = new VariableMessage("@v");

    #region IMutateRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        Snapshot ss = factory.RegisterState(Socket.WriteState(InternalVariable));
        factory.RegisterPremises(ss, Event.Know(new NameMessage(Socket.ChannelName)));
        return GenerateStateConsistentRule(factory, Event.Know(InternalVariable));
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Know rule for {Socket}.";

    public override bool Equals(object? obj)
    {
        return obj is KnowChannelContentRule r && 
            Socket.Equals(r.Socket) && 
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
