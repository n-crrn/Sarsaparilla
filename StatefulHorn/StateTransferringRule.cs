using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

public class StateTransferringRule : Rule
{
    public StateTransferringRule(string label, Guard g, HashSet<Event> prems, SnapshotTree ss, StateTransformationSet st) :
        base(label, g, prems, ss)
    {
        Result = st;
        GenerateHashCode();
    }

    public override StateTransformationSet Result { get; }

    protected override bool ResultContainsMessage(IMessage msg) => Result.ContainsMessage(msg);

    protected override bool ResultContainsEvent(Event ev) => false;

    protected override bool ResultContainsState(State st) => Result.ContainsState(st);

    protected override string DescribeResult() => Result.ToString();

    public override Rule CreateDerivedRule(string label, Guard g, HashSet<Event> prems, SnapshotTree ss, SigmaMap substitutions)
    {
        // Note that the substitutions are not actually applied to the Result. This is because the
        // StateTransformationSet is deeply tied to the SnapshotTree. Therefore, Result has to be
        // derived from that tree rather than through the substitutions.
        return new StateTransferringRule(label, g, prems, ss, ss.ExtractStateTransformations());
    }

    #region Updated transformation code.

    public StateConsistentRule? TryTransform(StateConsistentRule r)
    {
        // If there is a match for each trace of the transformation, then this is possible.
        Guard combinedGuard = GuardStatements.UnionWith(r.GuardStatements);
        SigmaFactory sf = new();

        // The following tuple is of form Snapshot, Trace Index, Offset Index with Trace.
        List<(Snapshot, int, int)>? overallCorres = DetermineSnapshotCorrespondencesWith(r, combinedGuard, sf);
        if (overallCorres == null)
        {
            return null;
        }
        
        // Having determined that it is possible, the next step is to create the new rule.
        // Note that assumption that the ordering of traces does not change during substitutions.
        SigmaMap fwd = sf.CreateForwardMap();
        SigmaMap bwd = sf.CreateBackwardMap();

        // The transformed rule is used as a template for the construction of the final rule.
        StateConsistentRule transformedRule = (StateConsistentRule)r.PerformSubstitution(bwd);
        for (int i = 0; i < overallCorres.Count; i++)
        {
            (Snapshot guide, int traceIndex, int offsetIndex) = overallCorres[i];
            overallCorres[i] = (guide.PerformSubstitutions(fwd), traceIndex, offsetIndex);
        }

        HashSet<Event> newPremises = new(transformedRule.Premises);
        foreach ((Snapshot guide, int traceIndex, int offsetIndex) in overallCorres)
        {
            Snapshot ss = transformedRule.Snapshots.Traces[traceIndex];
            for (int oi = offsetIndex; oi > 0; oi--)
            {
                ss = ss.Prior!.S;
            }
            // Update the template snapshot.
            ss.AddPremises(guide.Premises);
            ss.TransfersTo = guide.TransfersTo;
            // Update the premises.
            foreach (Event premise in guide.Premises)
            {
                newPremises.Add(premise);
            }
        }
        transformedRule.Snapshots.ActivateTransfers();

        return new StateConsistentRule($"({Label}) ⋈ ({r.Label})", combinedGuard, newPremises, transformedRule.Snapshots, transformedRule.Result);
    }

    #endregion
}
