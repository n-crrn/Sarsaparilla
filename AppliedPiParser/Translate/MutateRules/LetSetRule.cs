using System.Collections.Generic;
using System.Linq;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class LetSetRule : MutateRule
{

    public LetSetRule(
        string desig, 
        IEnumerable<Event> premises, 
        IEnumerable<Socket> shutSockets, 
        IEnumerable<Socket> initSockets,
        IfBranchConditions triggerConditions, 
        Event setKnow)
    {
        Designation = desig;
        Premises = new HashSet<Event>(premises);
        SocketStates = new List<State>();
        SocketStates.AddRange(from s in shutSockets select s.ShutState());
        SocketStates.AddRange(from i in initSockets select i.InitialState());
        TriggerConditions = triggerConditions;
        SetKnow = setKnow;
        Label = $"LetSet-{Designation}-{SetKnow.Message}";
    }

    #region Properties.

    public string Designation { get; init; }

    public IReadOnlySet<Event> Premises { get; init; }

    public List<State> SocketStates { get; init; }

    public IfBranchConditions TriggerConditions { get; init; }

    public Event SetKnow { get; init; }

    #endregion
    #region IMutableRule implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        // Attach premises to the first state - though any state would be fine.
        IEnumerator<State> sIter = SocketStates.GetEnumerator();
        if (sIter.MoveNext())
        {
            State firstSocket = sIter.Current;
            Snapshot ss = factory.RegisterState(firstSocket);
            factory.RegisterPremises(ss, Premises);
            while (sIter.MoveNext())
            {
                factory.RegisterState(sIter.Current);
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
            SocketStates.ToHashSet().SetEquals(r.SocketStates) &&
            TriggerConditions.Equals(r.TriggerConditions) &&
            SetKnow.Equals(r.SetKnow) &&
            Conditions.Equals(r.Conditions);
    }

    public override int GetHashCode() => Designation.GetHashCode();

    #endregion

}
