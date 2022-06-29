using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteWriteRule : IMutateRule
{

    public InfiniteWriteRule(
        WriteSocket s,
        PathSurveyor.Marker marker,
        HashSet<Event> premises,
        IMessage value)
    {
        Socket = s;
        Marker = marker;
        ValueToWrite = value;
        Premises = new(premises); // Copy, so that premises are not added afterwards.
    }

    public WriteSocket Socket { get; private init; }

    public PathSurveyor.Marker Marker { get; private init; }

    public HashSet<Event> Premises { get; private init; }

    public IMessage ValueToWrite { get; private init; }

    #region IMutableRule implementation.

    public string Label => $"InfWrite-{ValueToWrite}-{Socket}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        IDictionary<Socket, Snapshot> sockSS = Marker.Register(factory);
        Snapshot latest;
        if (Socket.IsInfinite)
        {
            latest = factory.RegisterState(Socket.WaitingState());
        }
        else
        {
            latest = sockSS[Socket];
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
        return obj is InfiniteWriteRule r 
            && Socket.Equals(r.Socket) 
            && Marker.Equals(r.Marker) 
            && Premises.SetEquals(r.Premises) 
            && ValueToWrite.Equals(r.ValueToWrite) 
            && Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
