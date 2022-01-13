using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace StatefulHorn;

public class StateTransferringRule : Rule
{
    public StateTransferringRule(string label, Guard g, List<Event> prems, SnapshotTree ss, StateTransformationSet st) :
        base(label, g, prems, ss)
    {
        Result = st;
    }

    public override StateTransformationSet Result { get; }

    protected override bool ResultContainsMessage(IMessage msg) => Result.ContainsMessage(msg);

    protected override bool ResultContainsEvent(Event ev) => false;

    protected override bool ResultContainsState(State st) => Result.ContainsState(st);

    protected override string DescribeResult() => Result.ToString();

    public override Rule CreateDerivedRule(string label, Guard g, List<Event> prems, SnapshotTree ss, SigmaMap substitutions)
    {
        // Note that the substitutions are not actually applied to the Result. This is because the
        // StateTransformationSet is deeply tied to the SnapshotTree. Therefore, Result has to be
        // derived from that tree rather than through the substitutions.
        return new StateTransferringRule(label, g, prems, ss, ss.ExtractStateTransformations());
    }

    public StateConsistentRule? Transform(StateConsistentRule r)
    {
        if (CanTransform(r, out SigmaMap? sigma))
        {
            Debug.Assert(sigma != null);
            Guard g = GuardStatements.PerformSubstitution(sigma).UnionWith(r.GuardStatements);

            SnapshotTree? newTree = Snapshots.TryActivateTransfersUpon(r.Snapshots, sigma);
            if (newTree != null)
            {
                return new StateConsistentRule($"({Label}) ⋈ ({r.Label})", g, new(r.Premises), newTree, r.Result);
            }
        }
        return null;
    }

    private bool CanTransform(StateConsistentRule r, out SigmaMap? sigma)
    {
        // If the transformation can be implied by r, then we can proceed.
        Guard combinedGuard = GuardStatements.UnionWith(r.GuardStatements);
        List<ISigmaUnifiable> transformDetails = new(_Premises);
        transformDetails.AddRange(Snapshots.States);
        List<ISigmaUnifiable> opDetails = new(r.Premises);
        opDetails.AddRange(r.Snapshots.States);
        SigmaFactory sf = new(false);
        bool canProceed = UnifyUtils.IsUnifiedToSubset(transformDetails, opDetails, combinedGuard, sf);
        sigma = sf.CreateForwardMap();
        return canProceed;
    }
}
