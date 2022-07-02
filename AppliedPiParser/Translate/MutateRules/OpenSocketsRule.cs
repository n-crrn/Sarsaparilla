using System.Collections.Generic;
using System.Linq;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class OpenSocketsRule : MutateRule
{

    public OpenSocketsRule(Socket open)
      : this(new List<Socket>() { open }, new List<Socket>())
    { }

    public OpenSocketsRule(IEnumerable<Socket> open, IEnumerable<Socket> shut)
    {
        SocketsRequiredOpen = open.ToHashSet();
        SocketsRequiredShut = shut.ToHashSet();
        Label = $"OpenSockets-" + string.Join(":", SocketsRequiredOpen);
        RecommendedDepth = 1;
    }

    public IReadOnlySet<Socket> SocketsRequiredOpen { get; init; }

    public IReadOnlySet<Socket> SocketsRequiredShut { get; init; }

    #region IMutateRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        foreach (Socket shutS in SocketsRequiredShut)
        {
            factory.RegisterState(shutS.ShutState());
        }
        foreach (Socket openS in SocketsRequiredOpen)
        {
            Snapshot ss = factory.RegisterState(openS.InitialState());
            ss.TransfersTo = openS.WaitingState();
        }
        return GenerateStateTransferringRule(factory);
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Open reading on sockets " + string.Join(", ", SocketsRequiredOpen) + ".";

    public override bool Equals(object? obj)
    {
        return obj is OpenSocketsRule r &&
            SocketsRequiredOpen.SetEquals(r.SocketsRequiredOpen) &&
            SocketsRequiredShut.SetEquals(r.SocketsRequiredShut);
    }

    public override int GetHashCode() => SocketsRequiredOpen.Count + SocketsRequiredShut.Count;

    #endregion

}
