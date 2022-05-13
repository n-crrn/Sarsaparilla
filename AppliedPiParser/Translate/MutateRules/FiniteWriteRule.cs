using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using StatefulHorn;
using AppliedPi.Model;

namespace AppliedPi.Translate.MutateRules;

public class FiniteWriteRule : IMutateRule
{

    public FiniteWriteRule(WriteSocket s, IDictionary<Socket, int> finActionCounts, HashSet<StatefulHorn.Event> premises, IMessage value)
    {
        Socket = s;
        FiniteActionCounts = new Dictionary<Socket, int>(finActionCounts);
        ValueToWrite = value;
        Premises = new(premises); // Copy, so that premises are not added afterwards.
    }

    public WriteSocket Socket { get; init; }

    public int PriorWriteCount => FiniteActionCounts[Socket];

    public IDictionary<Socket, int> FiniteActionCounts { get; init; }

    HashSet<StatefulHorn.Event> Premises { get; init; }

    public IMessage ValueToWrite { get; init; }

    #region IMutableRule implementation.

    public string Label => $"FinWrite-{ValueToWrite}-{Socket}({PriorWriteCount})";

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
        Debug.Assert(latest != null);
        factory.RegisterPremises(latest, Premises);
        latest.TransfersTo = Socket.WriteState(ValueToWrite);
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    #endregion
    #region Basic object override.

    public override string ToString() => $"Write to socket rule for {Socket} after {PriorWriteCount} prior writes.";

    public override bool Equals(object? obj)
    {
        return obj is FiniteWriteRule r &&
            Socket.Equals(r.Socket) &&
            FiniteActionCounts.SequenceEqual(r.FiniteActionCounts) &&
            Premises.SetEquals(r.Premises) &&
            ValueToWrite.Equals(r.ValueToWrite) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
