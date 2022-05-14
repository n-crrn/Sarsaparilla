using System.Collections.Generic;
using System.Linq;

using AppliedPi;
using AppliedPi.Model;
using AppliedPi.Processes;
using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class LetSetRule : IMutateRule
{

    public LetSetRule(
        string desig, 
        IEnumerable<StatefulHorn.Event> premises, 
        IEnumerable<Socket> shutSockets, 
        IfBranchConditions triggerConditions, 
        StatefulHorn.Event setKnow)
    {
        Designation = desig;
        Premises = new HashSet<StatefulHorn.Event>(premises);
        ShutSockets = new HashSet<Socket>(shutSockets);
        TriggerConditions = triggerConditions;
        SetKnow = setKnow;
    }

    public string Designation { get; init; }

    public IReadOnlySet<StatefulHorn.Event> Premises { get; init; }

    public IReadOnlySet<Socket> ShutSockets { get; init; }

    public IfBranchConditions TriggerConditions { get; init; }

    public StatefulHorn.Event SetKnow { get; init; }

    #region IMutableRule implementation.

    public string Label => $"LetSet-{Designation}-{SetKnow.Messages[0]}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        // Attach premises to the first state - though any state would be fine.
        IEnumerator<Socket> sIter = ShutSockets.GetEnumerator();
        if (sIter.MoveNext())
        {
            Socket firstSocket = sIter.Current;
            Snapshot ss = factory.RegisterState(firstSocket.ShutState());
            factory.RegisterPremises(ss, Premises);
            while (sIter.MoveNext())
            {
                factory.RegisterState(sIter.Current.ShutState());
            }
        }
        else
        {
            factory.RegisterPremises(Premises.ToArray());
        }
        IfBranchConditions fullConditions = Conditions.And(TriggerConditions);
        factory.GuardStatements = fullConditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(fullConditions, factory.CreateStateConsistentRule(SetKnow));
    }

    #endregion
    #region Basic object override.

    public override string ToString() => $"Set value {SetKnow}";

    public override bool Equals(object? obj)
    {
        return obj is LetSetRule r &&
            Designation.Equals(r.Designation) &&
            Premises.SetEquals(r.Premises) &&
            ShutSockets.SetEquals(r.ShutSockets) &&
            TriggerConditions.Equals(r.TriggerConditions) &&
            SetKnow.Equals(r.SetKnow) &&
            Conditions.Equals(r.Conditions);
    }

    public override int GetHashCode() => Designation.GetHashCode();

    #endregion

}
