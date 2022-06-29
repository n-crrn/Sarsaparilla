using System.Collections.Generic;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class FiniteWriteRule : IMutateRule
{

    public FiniteWriteRule(
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

    public string Label
    {
        get
        {
            if (Socket.IsInfinite)
            {
                return $"FinWrite-{ValueToWrite}-{Socket}";
            }
            return $"FinWrite-{ValueToWrite}-{Socket}";
        }
    } 

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
        latest.TransfersTo = Socket.WriteState(ValueToWrite);
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    public int RecommendedDepth => 2;

    #endregion
    #region Basic object override.

    public override string ToString() => $"Finite write to socket rule for {Socket} of value {ValueToWrite}.";

    public override bool Equals(object? obj)
    {
        return obj is FiniteWriteRule r &&
            Socket.Equals(r.Socket) &&
            Marker.Equals(r.Marker) &&
            Premises.SetEquals(r.Premises) &&
            ValueToWrite.Equals(r.ValueToWrite) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
