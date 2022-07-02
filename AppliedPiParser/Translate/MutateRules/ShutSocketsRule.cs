using System.Collections.Generic;
using System.Linq;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class ShutSocketsRule : MutateRule
{

    public ShutSocketsRule(PathSurveyor.Marker marker, IList<Socket> socketsToShut)
    {
        Marker = marker;
        Sockets = socketsToShut;
        Label = $"ShutSockets:" + string.Join(":", Sockets);
        RecommendedDepth = 1;
    }

    public PathSurveyor.Marker Marker { get; private init; }

    public IList<Socket> Sockets { get; private init; }

    #region IMutateRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        IDictionary<Socket, Snapshot> allSS = Marker.Register(factory);
        foreach (Socket sToShut in Sockets)
        {
            allSS[sToShut].TransfersTo = sToShut.ShutState();
        }
        return GenerateStateTransferringRule(factory);
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Shut sockets rule for " + string.Join(", ", Sockets) + ".";

    public override bool Equals(object? obj)
    {
        return obj is ShutSocketsRule ssr
            && Marker.Equals(ssr.Marker)
            && Sockets.ToHashSet().SetEquals(ssr.Sockets);
    }

    public override int GetHashCode() => Sockets.Count; // Only semi-efficient way to do it.

    #endregion

}
