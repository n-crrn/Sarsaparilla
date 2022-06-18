using StatefulHorn.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

public class StateConsistentRule : Rule
{
    public StateConsistentRule(string label, Guard g, HashSet<Event> prems, SnapshotTree ss, Event res) : base(label, g, prems, ss)
    {
        Result = res;
        GenerateHashCode();
    }

    public override Event Result { get; }

    public bool ResultIsTerminating => Result.EventType == Event.Type.Accept || Result.EventType == Event.Type.Leak;

    protected override string DescribeResult() => Result.ToString();

    /// <summary>
    /// When a rule is added to a set of rules, it may receive a unique identifier to speed up 
    /// the determination of identical rules. Valid tag values are equal to or greater than zero.
    /// </summary>
    public int IdTag { get; set; } = -1;

    public bool MatchesTagOf(StateConsistentRule other) => other.IdTag == IdTag && IdTag != -1;

    #region Filtering.

    protected override bool ResultContainsMessage(IMessage msg) => Result.ContainsMessage(msg);

    protected override bool ResultContainsEvent(Event ev) => Result.Equals(ev);

    protected override bool ResultContainsState(State st) => false;

    public bool IsFact => Premises.Count == 0 && Snapshots.IsEmpty && Result.IsKnow && !Result.ContainsVariables;

    public bool IsResolved
    {
        get
        {
            if (!Result.ContainsVariables)
            {
                foreach (Event prem in Premises)
                {
                    if (prem.ContainsVariables)
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }
    }

    public bool AreAllPremisesKnown(HashSet<IMessage> ruleSet1, HashSet<IMessage> ruleSet2)
    {
        foreach (Event ev in Premises)
        {
            if (ev.IsKnow && !IsPremiseKnown(ruleSet1, ruleSet2, ev.Messages[0]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsPremiseKnown(HashSet<IMessage> rs1, HashSet<IMessage> rs2, IMessage premiseMessage)
    {
        bool predicate(IMessage msg) => IsPremiseKnown(rs1, rs2, msg);
        return
            rs1.Contains(premiseMessage) ||
            rs2.Contains(premiseMessage) ||
            (premiseMessage is TupleMessage tMsg && tMsg.Members.All(predicate)) ||
            (premiseMessage is FunctionMessage fMsg && fMsg.Parameters.All(predicate));
    }

    // FIXME: Is this method even required?
    public IEnumerable<IMessage> GetAllBasicMessages()
    {
        Stack<IMessage> compoundStack = new();
        foreach (Event ev in Premises)
        {
            if (ev.IsKnow)
            {
                IMessage msg = ev.Messages[0];
                if (msg is BasicMessage)
                {
                    yield return msg;
                }
                else
                {
                    compoundStack.Push(msg);
                    while (compoundStack.Count > 0)
                    {
                        IMessage cmpMsg = compoundStack.Pop();
                        if (cmpMsg is TupleMessage tplMsg)
                        {
                            foreach (IMessage innerMsg in tplMsg.Members)
                            {
                                compoundStack.Push(innerMsg);
                            }
                        }
                        else if (cmpMsg is FunctionMessage funcMsg)
                        {
                            foreach (IMessage innerMsg in funcMsg.Parameters)
                            {
                                compoundStack.Push(innerMsg);
                            }
                        }
                        else
                        {
                            yield return cmpMsg;
                        }
                    }
                }
            }
        }
        // FIXME: Search for State messages as well.
    }

    #endregion

    public override Rule CreateDerivedRule(string label, Guard g, HashSet<Event> prems, SnapshotTree ss, SigmaMap substitutions)
    {
        return new StateConsistentRule(label, g, prems, ss, Result.PerformSubstitution(substitutions));
    }

    private bool CanComposeWith(Rule r, out SigmaFactory? sf)
    {
        foreach (Event premise in r.Premises)
        {
            sf = new();
            if (Result.CanBeUnifiableWith(premise, Guard, r.Guard, sf))
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
        SigmaMap fwdSigma = substitutions.CreateForwardMap();
        SigmaMap bwdSigma = substitutions.CreateBackwardMap();

        Guard g = Guard.Substitute(fwdSigma).Union(r.Guard.Substitute(bwdSigma));

        Dictionary<Event, Event> updatedPremises = new(Premises.Count + r.Premises.Count - 1);
        HashSet<Event> h = new(Premises.Count);
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
        foreach (Event ofPremises in otherFilteredPremises)
        {
            h.Add(ofPremises);
        }

        string lbl = $"({Label}) ○_{{{Result}}} ({r.Label})";
        output = r.CreateDerivedRule(lbl, g, h, newTree, bwdSigma);

        // Final check - if this is a StateConsistentRule, then we need to ensure that the result
        // is not in the premise. It can only be done at this point.
        if (output is StateConsistentRule scr)
        {
            Event outputEvent = scr.Result;
            if (h.Contains(outputEvent))
            {
                output = null;
                return false;
            }
        }

        return true;
    }

    // === Updated Transform Code ===

    public List<NonceMessage> NewNonces => (from p in Premises where p.IsNew select (NonceMessage)p.Messages.Single()).ToList();

    public IEnumerable<Event> NewEvents => from p in Premises where p.IsNew select p;

    private bool CanComposeUpon(StateConsistentRule r, out SigmaFactory? sf, out List<(Snapshot, int, int)>? overallCorrespondence)
    {
        overallCorrespondence = null;
        foreach (Event premise in r.Premises)
        {
            sf = new();
            if (Result.CanBeUnifiableWith(premise, Guard, r.Guard, sf))
            {
                // If nonced declared in this rule are declared in the other r, then the two rules
                // cannot be composed as they cross sessions.
                List<NonceMessage> otherNonces = r.NewNonces;
                foreach (NonceMessage nMsg in NewNonces)
                {
                    if (otherNonces.Contains(nMsg))
                    {
                        sf = null;
                        return false;
                    }
                }

                // Check if we match traces as well.
                if (Snapshots.IsEmpty || r.Snapshots.IsEmpty)
                {
                    overallCorrespondence = new();
                    return true;
                }
                List<(Snapshot, int, int)>? overallCorres = DetermineSnapshotCorrespondencesWith(r, sf);
                if (overallCorres != null)
                {
                    overallCorrespondence = overallCorres;
                    return true;
                }
            }
        }
        sf = null;
        return false;
    }

    public StateConsistentRule? TryComposeUpon(StateConsistentRule r)
    {
        // Check that we can do a composition, set output to null and return false if we can't.
        if (!CanComposeUpon(r, out SigmaFactory? substitutions, out List<(Snapshot, int, int)>? overallCorres))
        {
            return null;
        }
        Debug.Assert(substitutions != null && overallCorres != null);
        SigmaMap fwdSigma = substitutions.CreateForwardMap();
        SigmaMap bwdSigma = substitutions.CreateBackwardMap();

        Guard g = Guard.Substitute(fwdSigma).Union(r.Guard.Substitute(bwdSigma));

        Dictionary<Event, Event> updatedPremises = new(Premises.Count + r.Premises.Count - 1);
        HashSet<Event> h = new(Premises.Count);
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
            // Remember that e0 was not actually included in updatedPremises.
            newTree = r.Snapshots.CloneTreeWithReplacementEvents(updatedPremises, bwdSigma);
            foreach (Snapshot ss in newTree.GetSnapshotsAssociatedWith(e0))
            {
                ss.ReplacePremises(e0, h);
            }
            foreach ((Snapshot guide, int traceIndex, int offsetIndex) in overallCorres)
            {
                Snapshot ss = newTree.Traces[traceIndex];
                for (int oi = offsetIndex; oi > 0; oi--)
                {
                    ss = ss.Prior!.S;
                }
                ss.AddPremises(from ap in guide.Premises select ap.PerformSubstitution(fwdSigma));
            }
        }
        foreach (Event ofPremises in otherFilteredPremises)
        {
            h.Add(ofPremises);
        }

        string lbl = $"({Label}) ○_{{{Result}}} ({r.Label})";
        StateConsistentRule output = new(lbl, g, h, newTree, r.Result.PerformSubstitution(bwdSigma));

        // Final check - if this is a StateConsistentRule, then we need to ensure that the result
        // is not in the premise. It can only be done at this point.
        if (h.Contains(output.Result))
        {
            return null;
        }
        if (output is StateConsistentRule scr)
        {
            Event outputEvent = scr.Result;
            if (h.Contains(outputEvent))
            {
                return null;
            }
        }

        return output;
    }
}
