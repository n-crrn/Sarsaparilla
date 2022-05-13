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

    private static readonly IMessage InternalVariable = new VariableMessage("@v");

    #region IMutateRule implementation.

    public string Label => $"Know:{Socket}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot ss = factory.RegisterState(Socket.WriteState(InternalVariable));
        factory.RegisterPremises(ss, Event.Know(new NameMessage(Socket.ChannelName)));
        factory.GuardStatements = Conditions?.CreateGuard();
        Rule r = factory.CreateStateConsistentRule(Event.Know(InternalVariable));
        return IfBranchConditions.ApplyReplacements(Conditions, r);
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
