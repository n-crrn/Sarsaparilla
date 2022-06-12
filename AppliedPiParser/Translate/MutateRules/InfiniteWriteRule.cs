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
        Socket = s;
        FiniteActionCounts = new Dictionary<Socket, int>(finActionCounts);
        ValueToWrite = value;
        Premises = new(premises); // Copy, so that premises are not added afterwards.
    }

    public WriteSocket Socket { get; init; }

    public IDictionary<Socket, int> FiniteActionCounts { get; init; }

    public HashSet<Event> Premises { get; init; }

    public IMessage ValueToWrite { get; init; }

    #region IMutableRule implementation.

    public string Label => $"InfWrite-{ValueToWrite}-{Socket}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot? latest = null;
        foreach ((Socket s, int ic) in FiniteActionCounts)
        {
            Snapshot ss = s.RegisterHistory(factory, ic);
            if (s.Equals(Socket))
            {
                latest = ss;
            }
        }
        if (latest == null)
        {
            latest = factory.RegisterState(Socket.WaitingState());
        }
        factory.RegisterPremises(latest, Premises);
        factory.RegisterPremises(latest, Event.Know(new NameMessage(Socket.ChannelName)));
        factory.GuardStatements = Conditions?.CreateGuard();
        Rule r = factory.CreateStateConsistentRule(Event.Know(ValueToWrite));
        return IfBranchConditions.ApplyReplacements(Conditions, r);
    }

    public int RecommendedDepth => 2;

    #endregion
    #region Basic object override.

    public override string ToString() => $"Infinite write to {Socket} of value {ValueToWrite}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteWriteRule r &&
            Socket.Equals(r.Socket) &&
            FiniteActionCounts.ToHashSet().SetEquals(r.FiniteActionCounts) &&
            Premises.SetEquals(r.Premises) &&
            ValueToWrite.Equals(r.ValueToWrite) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
