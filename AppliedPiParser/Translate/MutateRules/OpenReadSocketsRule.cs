using System.Collections.Generic;
using System.Linq;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class OpenReadSocketsRule : IMutateRule
{

    public OpenReadSocketsRule(Socket open)
      : this(new List<Socket>() { open }, new List<Socket>())
    { }

    public OpenReadSocketsRule(IEnumerable<Socket> open, IEnumerable<Socket> shut)
    {
        SocketsRequiredOpen = open.ToHashSet();
        SocketsRequiredShut = shut.ToHashSet();
    }

    public IReadOnlySet<Socket> SocketsRequiredOpen { get; init; }

    public IReadOnlySet<Socket> SocketsRequiredShut { get; init; }

    #region IMutateRule implementation.

    public string Label => $"OpenSockets-" + string.Join(":", SocketsRequiredOpen);

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
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
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    public int RecommendedDepth => 1;

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Open reading on sockets " + string.Join(", ", SocketsRequiredOpen) + ".";

    public override bool Equals(object? obj)
    {
        return obj is OpenReadSocketsRule r &&
            SocketsRequiredOpen.SetEquals(r.SocketsRequiredOpen) &&
            SocketsRequiredShut.SetEquals(r.SocketsRequiredShut);
    }

    public override int GetHashCode() => SocketsRequiredOpen.Count + SocketsRequiredShut.Count;

    #endregion

}
