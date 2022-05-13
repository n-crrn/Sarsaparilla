using System.Collections.Generic;
using System.Diagnostics;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class OpenReadSocketRule : IMutateRule
{

    public OpenReadSocketRule(ReadSocket s)
    {
        Socket = s;
    }

    public OpenReadSocketRule(ReadSocket s, IEnumerable<Socket> socketsRequiredShut) : this(s)
    {
        PriorSockets.UnionWith(socketsRequiredShut);
    }

    public ReadSocket Socket { get; init; }

    private readonly HashSet<Socket> PriorSockets = new();

    #region IMutateRule implementations.

    public string Label => $"Open:{Socket}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        foreach (Socket ps in PriorSockets)
        {
            factory.RegisterState(ps.ShutState());
        }
        Snapshot ss = factory.RegisterState(Socket.InitialState());
        ss.TransfersTo = Socket.WaitingState();
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Open reading on {Socket}.";

    public override bool Equals(object? obj)
    {
        return obj is OpenReadSocketRule r &&
            Socket.Equals(r.Socket) &&
            PriorSockets.SetEquals(r.PriorSockets) &&
            Equals(Conditions, r.Conditions);
    }

    public override int GetHashCode() => Socket.GetHashCode();

    #endregion

}
