using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class ShutSocketsRule : IMutateRule
{

    public ShutSocketsRule(IReadOnlyDictionary<Socket, int> interactionCounts)
    {
        PriorInteractions = interactionCounts;
    }

    public IReadOnlyDictionary<Socket, int> PriorInteractions;

    #region IMutateRule implementation.

    public string Label => $"ShutSockets:" + string.Join(":", PriorInteractions.Keys);

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        foreach ((Socket s, int intCount) in PriorInteractions)
        {
            Snapshot prior;
            if (s.Direction == SocketDirection.In)
            {
                prior = ((ReadSocket)s).RegisterReadSequence(factory, intCount);
            }
            else
            {
                prior = ((WriteSocket)s).RegisterWriteSequence(factory, intCount, s.WaitingState());
            }
            prior.TransfersTo = s.ShutState();
        }
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateTransferringRule());
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Shut sockets rule for " + string.Join(", ", PriorInteractions.Keys) + ".";

    public override bool Equals(object? obj)
    {
        return obj is ShutSocketsRule ssr &&
            PriorInteractions.ToHashSet().SetEquals(ssr.PriorInteractions.ToHashSet());
    }

    public override int GetHashCode() => PriorInteractions.Count; // Only semi-efficient way to do it.

    #endregion

}
