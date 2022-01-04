using System.Collections.Generic;
using System.Diagnostics;

namespace StatefulHorn;

public class StateConsistentRule : Rule
{
    public StateConsistentRule(string label, Guard g, List<Event> prems, SnapshotTree ss, Event res) : base(label, g, prems, ss)
    {
        Result = res;
    }

    public override Event Result { get; }

    public bool ResultIsTerminating => Result.EventType == Event.Type.Accept || Result.EventType == Event.Type.Leak;

    protected override string DescribeResult() => Result.ToString();

    #region Filtering.

    protected override bool ResultContainsMessage(IMessage msg) => Result.ContainsMessage(msg);

    protected override bool ResultContainsEvent(Event ev) => Result.Equals(ev);

    protected override bool ResultContainsState(State st) => false;

    #endregion

    public override Rule CreateDerivedRule(string label, Guard g, List<Event> prems, SnapshotTree ss, SigmaMap substitutions)
    {
        return new StateConsistentRule(label, g, prems, ss, Result.PerformSubstitution(substitutions));
    }

    public bool CanComposeWith(Rule r, out SigmaFactory? sf)
    {
        foreach (Event premise in r.Premises)
        {
            sf = new();
            if (Result.CanBeUnifiableWith(premise, GuardStatements, sf))
            {
                return true;
            }
        }
        sf = null;
        return false;
    }

    public bool TryComposeWith(Rule r, out Rule? output)
    {
        // Check that we can do a composition, set output to null and return false if we can't.
        if (!CanComposeWith(r, out SigmaFactory? substitutions))
        {
            output = null;
            return false;
        }
        Debug.Assert(substitutions != null);
        SigmaMap fwdSigma = substitutions.CreateFowardMap();
        SigmaMap bwdSigma = substitutions.CreateBackwardMap();

        Guard g = GuardStatements.PerformSubstitution(fwdSigma).UnionWith(r.GuardStatements.PerformSubstitution(bwdSigma));

        Dictionary<Event, Event> updatedPremises = new(Premises.Count + r.Premises.Count - 1);
        List<Event> h = new(Premises.Count);
        foreach (Event premEv in Premises)
        {
            Event newEv = premEv.PerformSubstitution(fwdSigma);
            updatedPremises[premEv] = newEv;
            h.Add(newEv);
        }
        List<Event> otherFilteredPremises = new(r.Premises.Count - 1);
        Event updatedResult = Result.PerformSubstitution(fwdSigma);
        Event? e0 = null;
        foreach (Event rPremEv in r.Premises)
        {
            Event newEv = rPremEv.PerformSubstitution(bwdSigma);
            if (!updatedResult.Equals(newEv))
            {
                updatedPremises[rPremEv] = newEv;
                otherFilteredPremises.Add(newEv);
            }
            else
            {
                e0 = rPremEv;
            }
        }
        Debug.Assert(e0 != null, "Could not find e0 event (one result's result, the other's premise) after substitution.");

        // The snapshot combining can be tricky - so we try to cheat if one or the other Rule
        // does not have a proper set of snapshots.
        SnapshotTree newTree;
        if (Snapshots.IsEmpty)
        {
            if (r.Snapshots.IsEmpty)
            {
                newTree = new();
            }
            else
            {
                // This rule's snapshot tree is empty, while the other has something.
                // Associate the premises of this tree with the snapshots of the result event.
                newTree = r.Snapshots.CloneTreeWithReplacementEvents(updatedPremises, bwdSigma);
                // Remember that e0 was not actually updated in the tree - hence why we 
                // use e0 instead of updatedResult for search and replacement in the
                // SnapshotTree.
                foreach (Snapshot ss in newTree.GetSnapshotsAssociatedWith(e0))
                {
                    ss.ReplacePremises(e0, h);
                }
            }
        }
        else if (r.Snapshots.IsEmpty)
        {
            // This rule's snapshot tree has something, but the other has nothing.
            // Associate the premises of the other with the trace heads of this tree.
            newTree = Snapshots.CloneTreeWithReplacementEvents(updatedPremises, fwdSigma);
            foreach (Snapshot ss in newTree.Traces)
            {
                ss.AddPremises(otherFilteredPremises);
            }
        }
        else
        {
            // Need to merge the SnapshotTrees on the result.
            SnapshotTree correctedOtherTree = r.Snapshots.CloneTreeWithReplacementEvents(updatedPremises, bwdSigma);
            // Remember that e0 was not actually included in updatedPremises.
            foreach (Snapshot ss in correctedOtherTree.GetSnapshotsAssociatedWith(e0))
            {
                ss.ReplacePremises(e0, h);
            }
            newTree = Snapshots.CloneTreeWithReplacementEvents(updatedPremises, fwdSigma).MergeWith(correctedOtherTree);
        }
        h.AddRange(otherFilteredPremises);

        string lbl = $"({Label}) ○_{{{Result}}} ({r.Label})";
        output = r.CreateDerivedRule(lbl, g, h, newTree, bwdSigma);
        return true;
    }

    public List<Rule> GenerateStateUnifications()
    {
        IReadOnlyList<Snapshot> orderedSnapshots = Snapshots.OrderedList;
        List<(int, int, SigmaMap)> matches = new();

        for (int i = 0; i < orderedSnapshots.Count; i++)
        {
            State c1 = orderedSnapshots[i].Condition;
            for (int j = i + 1; j < orderedSnapshots.Count; j++)
            {
                State c2 = orderedSnapshots[j].Condition;
                SigmaFactory sf = new();
                if (c1.CanBeUnifiableWith(c2, GuardStatements, sf))
                {
                    SigmaMap? sm = sf.TryCreateMergeMap();
                    if (sm != null)
                    {
                        matches.Add((i, j, sm));
                    }
                }
            }
        }

        List<Rule> newRules = new();
        foreach ((int ssIndex1, int ssIndex2, SigmaMap sigma) in matches)
        {
            Rule newRule1 = PerformSubstitution(sigma);
            Rule newRule2 = newRule1.Clone();
            newRules.Add(UnifySnapshots(newRule1, ssIndex1, ssIndex2));
            newRules.Add(UnifySnapshots(newRule2, ssIndex2, ssIndex1));
        }
        return newRules;
    }

    /// <summary>
    /// Convenience method supporting GenerateStateUnifications. It takes the 
    /// </summary>
    /// <param name="newRule"></param>
    /// <param name="index1"></param>
    /// <param name="index2"></param>
    /// <returns></returns>
    private static Rule UnifySnapshots(Rule newRule, int index1, int index2)
    {
        IReadOnlyList<Snapshot> ss = newRule.Snapshots.OrderedList;
        ss[index1].SetUnifedWith(ss[index2]);
        newRule.Label = $"{newRule.Label}[{ss[index1].Label} ~ {ss[index2].Label}]";
        return newRule;
    }
}
