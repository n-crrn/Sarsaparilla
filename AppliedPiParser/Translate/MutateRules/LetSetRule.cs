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
        IEnumerable<Socket> initSockets,
        IfBranchConditions triggerConditions, 
        StatefulHorn.Event setKnow)
    {
        Designation = desig;
        Premises = new HashSet<StatefulHorn.Event>(premises);
        SocketStates = new List<State>();
        foreach (Socket s in shutSockets)
        {
            SocketStates.Add(s.ShutState());
        }
        foreach (Socket s in initSockets)
        {
            SocketStates.Add(s.InitialState());
        }
        TriggerConditions = triggerConditions;
        SetKnow = setKnow;
    }

    public string Designation { get; init; }

    public IReadOnlySet<StatefulHorn.Event> Premises { get; init; }

    public List<State> SocketStates { get; init; }

    public IfBranchConditions TriggerConditions { get; init; }

    public StatefulHorn.Event SetKnow { get; init; }

    #region IMutableRule implementation.

    public string Label => $"LetSet-{Designation}-{SetKnow.Messages[0]}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
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

    public int RecommendedDepth => 0;

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
