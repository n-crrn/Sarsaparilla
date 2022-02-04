using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// A Nonce sESSION. This class provides a symbolic trace once a specific nonce has been set.
/// </summary>
public class Nession
{
    public Nession(IEnumerable<State> initStates)
    {
        History.Add(new(new(), new(initStates), new()));
    }

    /// <summary>
    /// Creates a new Nession seeded by a State Consistent Rule.
    /// </summary>
    /// <param name="ndRule">The Nonce Declaring Rule.</param>
    public Nession(StateConsistentRule ndRule)
    {
        History.Add(new(new(ndRule.Premises), LatestStateWithRule(ndRule), new() { ndRule.Result }));
    }

    public Nession(StateTransferringRule ndRule)
    {
        History.Add(new(new(), LatestStateWithRule(ndRule), new()));
        History.Add(new(new(ndRule.Premises), StatesAfterTransfer(ndRule), new()));
    }

    private Nession(IEnumerable<Frame> frames)
    {
        History.AddRange(frames);
    }

    #region Properties.

    public record Frame(HashSet<Event> Premises, HashSet<State> StateSet, HashSet<Event> Results)
    {
        public State? GetStateByName(string name)
        {
            foreach (State s in StateSet)
            {
                if (s.Name == name)
                {
                    return s;
                }
            }
            return null;
        }

        public Frame Substitute(SigmaMap map)
        {
            HashSet<Event> newPremises = new(from p in Premises select p.PerformSubstitution(map));
            HashSet<State> newStateSet = new(from s in StateSet select new State(s.Name, s.Value.PerformSubstitution(map)));
            HashSet<Event> newResults = new(from r in Results select r.PerformSubstitution(map));
            return new(newPremises, newStateSet, newResults);
        }
    }

    public List<Frame> History { get; init; } = new();

    // Used when determining if the Nession can be integrated with another Nession.
    public Rule? InitialRule { get; init; }

    public HashSet<Event> NonceDeclarations { get; } = new();

    #endregion
    #region Private convenience.

    private static HashSet<State> LatestStateWithRule(Rule r) => new(from ss in r.Snapshots.Traces select ss.Condition);

    private static HashSet<State> StatesAfterTransfer(StateTransferringRule r)
    {
        HashSet<State> statesInRule = LatestStateWithRule(r);
        foreach ((Snapshot after, State updatedCondition) in r.Result.Transformations)
        {
            IEnumerable<State> toReplace = from s in statesInRule where s.Name == updatedCondition.Name select s;
            foreach (State s in toReplace)
            {
                statesInRule.Remove(s);
                statesInRule.Add(updatedCondition);
            }
        }
        return statesInRule;
    }

    #endregion
    #region State transferring rule application.

    public Nession Substitute(SigmaMap map) => new(from f in History select f.Substitute(map));

    public Nession? TryApplyTransfer(StateTransferringRule r)
    {
        if (CanApplyRuleAt(r, History.Count - 1, out SigmaFactory? sf))
        {
            Debug.Assert(sf != null);
            SigmaMap fwdMap = sf.CreateForwardMap();
            SigmaMap bwdMap = sf.CreateBackwardMap();

            Nession updated = Substitute(fwdMap);

            StateTransferringRule updatedRule = (StateTransferringRule)r.PerformSubstitution(bwdMap);
            updated.History.Add(new(new(updatedRule.Premises), updated.CreateStateSetOnTransfer(updatedRule), new()));

            return updated;
        }
        return null;
    }

    private HashSet<State> CreateStateSetOnTransfer(StateTransferringRule r)
    {
        Frame lastFrame = History[^1];
        HashSet<State> stateSet = new(lastFrame.StateSet);
        StateTransformationSet transformSet = r.Result;
        foreach ((Snapshot after, State newState) in transformSet.Transformations)
        {
            bool wasRemoved = stateSet.Remove(after.Condition);
            Debug.Assert(wasRemoved);
            stateSet.Add(newState);
        }
        return stateSet;
    }

    public bool CanApplyRuleAt(Rule r, int startOffset, out SigmaFactory? sf)
    {
        sf = new();
        List<Snapshot> ruleTraces = new(from t in r.Snapshots.Traces select t);
        for (int i = 0; i < ruleTraces.Count; i++)
        {
            int historyId = startOffset;
            Frame historyFrame = History[historyId];

            Snapshot ruleSS = ruleTraces[i];
            State? nessionCondition = historyFrame.GetStateByName(ruleSS.Condition.Name);
            if (nessionCondition == null ||
                !ruleSS.Condition.CanBeUnifiableWith(nessionCondition, r.GuardStatements, sf))
            {
                goto txFail;
            }

            historyId--;
            while (ruleSS.Prior != null)
            {
                Snapshot.PriorLink next = ruleSS.Prior;
                if (historyId < 0)
                {
                    goto txFail;
                }
                if (next.O == Snapshot.Ordering.ModifiedOnceAfter)
                {
                    historyFrame = History[historyId];
                    nessionCondition = historyFrame.GetStateByName(next.S.Condition.Name);
                    if (nessionCondition == null)
                    {
                        // Consistency issue if the condition cannot be found.
                        throw new InvalidOperationException($"Cannot find previous mentions of state {ruleSS.Condition.Name}.");
                    }
                    if (!next.S.Condition.CanBeUnifiableWith(nessionCondition, r.GuardStatements, sf))
                    {
                        goto txFail;
                    }
                }
                else // Modified later than, which means it just has to find an earlier match in the nession.
                {
                    while (historyId >= 0)
                    {
                        historyFrame = History[historyId];
                        nessionCondition = historyFrame.GetStateByName(next.S.Condition.Name);
                        if (nessionCondition == null)
                        {
                            // Consistency issue if the condition cannot be found.
                            throw new InvalidOperationException($"Cannot find previous mentions of state {ruleSS.Condition.Name}.");
                        }
                        if (next.S.Condition.CanBeUnifiableWith(nessionCondition, r.GuardStatements, sf))
                        {
                            break;
                        }
                        historyId--;
                    }
                    if (historyId < 0)
                    {
                        goto txFail;
                    }
                }

                ruleSS = next.S;
                historyId--;
            }
        }
        return true;

    txFail:
        sf = null;
        return false;
    }

    #endregion
    #region System rule application.

    public List<Nession> TryApplySystemRule(StateConsistentRule r)
    {
        Debug.Assert(!r.Snapshots.IsEmpty);

        List<Nession> generated = new() { this };
        int maxTraceLength = r.Snapshots.MaxTraceLength;
        if (maxTraceLength > History.Count)
        {
            // Cannot possibly imply rule.
            return generated;
        }

        for (int frameOffset = maxTraceLength - 1; frameOffset < History.Count; frameOffset++)
        {
            if (CanApplyRuleAt(r, frameOffset, out SigmaFactory? sf))
            {
                Debug.Assert(sf != null);
                SigmaMap fwdMap = sf.CreateForwardMap();
                SigmaMap bwdMap = sf.CreateBackwardMap();
                StateConsistentRule updatedRule = (StateConsistentRule)r.PerformSubstitution(bwdMap);
                if (fwdMap.IsEmpty)
                {
                    Frame historyFrame = History[frameOffset];
                    historyFrame.Premises.UnionWith(updatedRule.Premises);
                    historyFrame.Results.Add(updatedRule.Result);
                }
                else
                {
                    Nession updatedNession = Substitute(fwdMap);
                    Frame historyFrame = updatedNession.History[frameOffset];
                    historyFrame.Premises.UnionWith(updatedRule.Premises);
                    historyFrame.Results.Add(updatedRule.Result);
                    generated.Add(updatedNession);
                }
            }
        }

        return generated;
    }

    #endregion

}
