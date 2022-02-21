using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StatefulHorn.Messages;
using StatefulHorn.Origin;

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
    /*public Nession(StateConsistentRule ndRule)
    {
        InitialRule = ndRule;
        History.Add(new(new(), LatestStateWithRule(ndRule), new() { ndRule }));
        UpdateNonceDeclarations();
    }

    public Nession(StateTransferringRule ndRule)
    {
        InitialRule = ndRule;
        History.Add(new(new(), LatestStateWithRule(ndRule), new()));
        History.Add(new(new(ndRule.Premises), StatesAfterTransfer(ndRule), new()));
        UpdateNonceDeclarations();
    }*/

    private Nession(Rule? initRule, IEnumerable<Frame> frames, int lastVNumber)
    {
        InitialRule = initRule;
        History.AddRange(frames);
        UpdateNonceDeclarations();
        VNumber = lastVNumber;
    }

    /*
    public static Nession FromRule(Rule r)
    {
        if (r is StateConsistentRule scr)
        {
            return new(scr);
        }
        else if (r is StateTransferringRule str)
        {
            return new(str);
        }
        else
        {
            throw new NotImplementedException($"Rule type '{r.GetType()}' is not StateConsistentRule or StateTransferringRule is not supported.");
        }
    }*/

    #region Properties.

    public class Frame
    {
        public Frame(HashSet<Event> premises, HashSet<State> stateSet, HashSet<StateConsistentRule> rules, StateTransferringRule? transferRule = null)
        {
            StateChangePremises = premises;
            StateSet = stateSet;
            Rules = rules;
            TransferRule = transferRule;
        }

        public Frame Clone() => new(new(StateChangePremises), new(StateSet), new(Rules), TransferRule);

        public HashSet<Event> StateChangePremises { get; init; }

        public StateTransferringRule? TransferRule { get; internal set; }

        public HashSet<State> StateSet { get; init; }

        public HashSet<StateConsistentRule> Rules { get; init; }

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
            HashSet<Event> newPremises = new(from p in StateChangePremises select p.PerformSubstitution(map));
            HashSet<State> newStateSet = new(from s in StateSet select new State(s.Name, s.Value.PerformSubstitution(map)));
            HashSet<StateConsistentRule> newRules = new();
            foreach (StateConsistentRule r in Rules)
            {
                StateConsistentRule newR = (StateConsistentRule)r.PerformSubstitution(map);
                newR.IdTag = r.IdTag;
                newRules.Add(newR);
            }
            Frame nf = new(newPremises, newStateSet, newRules);
            if (TransferRule != null)
            {
                nf.TransferRule = (StateTransferringRule)TransferRule.PerformSubstitution(map);
            }
            return nf;
        }

        public bool ResultsContainMessage(IMessage msg) => (from r in Rules where r.Result.ContainsMessage(msg) select r).Any();

        public override string ToString()
        {
            string premises = string.Join(", ", StateChangePremises);
            if (premises != string.Empty)
            {
                premises += "\n";
            }
            else
            {
                premises = "<NO STATE CHANGE PREMISES>\n";
            }
            string states = string.Join(", ", StateSet);
            string results = string.Join("\n\t", from r in Rules select r.ToString());
            if (results == string.Empty)
            {
                results = "<NO RULES>";
            }
            return $"{premises}\tSTATES [ {states} ]->\n\t{results}";
        }

        private static HashSet<int> RulesToIds(IEnumerable<StateConsistentRule> rules)
        {
            HashSet<int> hs = new();
            foreach (StateConsistentRule scr in rules)
            {
                Debug.Assert(scr.IdTag != -1);
                hs.Add(scr.IdTag);
            }
            return hs;
        }

        public override bool Equals(object? obj)
        {
            return obj is Frame f &&
                f.StateChangePremises.SetEquals(StateChangePremises) &&
                f.StateSet.SetEquals(StateSet) &&
                RulesToIds(Rules).SetEquals(RulesToIds(f.Rules));
        }

        public override int GetHashCode() => StateSet.First().GetHashCode();
    }

    public List<Frame> History { get; init; } = new();

    // Used when determining if the Nession can be integrated with another Nession.
    public Rule? InitialRule { get; init; }

    public HashSet<Event> NonceDeclarations { get; } = new();

    #endregion
    #region Private convenience.

    private void UpdateNonceDeclarations()
    {
        NonceDeclarations.Clear();
        foreach (Frame f in History)
        {
            NonceDeclarations.UnionWith(from p in f.StateChangePremises where p.IsNew select p);
            foreach (StateConsistentRule scr in f.Rules)
            {
                NonceDeclarations.UnionWith(scr.NewEvents);
            }
        }
    }

    // Unique subscript for individual rule applications.
    private int VNumber = 0;

    private string NextVNumber()
    {
        VNumber++;
        return $"v{VNumber}";
    }

    private void StepBackVNumber()
    {
        VNumber--;
    }

    #endregion
    #region State transferring rule application.

    public Nession Substitute(SigmaMap map) => new(InitialRule, from f in History select f.Substitute(map), VNumber);

    public Nession? TryApplyTransfer(StateTransferringRule str)
    {
        StateTransferringRule r = (StateTransferringRule)str.SubscriptVariables(NextVNumber());
        if (CanApplyRuleAt(r, History.Count - 1, out SigmaFactory? sf))
        {
            Debug.Assert(sf != null);
            SigmaMap fwdMap = sf.CreateForwardMap();
            SigmaMap bwdMap = sf.CreateBackwardMap();

            Nession updated = Substitute(bwdMap);

            StateTransferringRule updatedRule = (StateTransferringRule)r.PerformSubstitution(fwdMap);
            Frame newFrame = new(new(updatedRule.Premises), updated.CreateStateSetOnTransfer(updatedRule), new());
            updated.History.Add(newFrame);
            newFrame.TransferRule = updatedRule;
            UpdateNonceDeclarations();

            return updated;
        }
        else
        {
            StepBackVNumber();
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
        // Handle nonce boundaries.
        /*if (InitialRule == null && r.NonceDeclarations.Any()) // Initial traces do not contain nonce declarations.
        {
            sf = null;
            return false;
        }*/
        if (r.NonceDeclarations.Any((Event ev) => NonceDeclarations.Contains(ev))) // Do not allow redeclaration of nonces.
        {
            sf = null;
            return false;
        }
        foreach (NonceMessage nMsg in r.NoncesRequired) // Ensure used nonces have been previously declared.
        {
            if (!NonceDeclarations.Contains(Event.New(nMsg)))
            {
                string held = string.Join(", ", NonceDeclarations);
                sf = null;
                return false;
            }
        }

        // Check that the rule traces are implied by this Nession's history.
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

    public List<Nession> TryApplySystemRule(StateConsistentRule scr)
    {
        Debug.Assert(!scr.Snapshots.IsEmpty);
        List<Nession> generated = new() { this };
        /*int maxTraceLength = scr.Snapshots.MaxTraceLength;
        if (maxTraceLength > History.Count)
        {
            // Cannot possibly imply rule.
            return generated;
        }*/

        /*if (scr.Snapshots.IsEmpty) // Empty always applies - probably a nonce declaration/usage.
        {
            StateConsistentRule emptyR = (StateConsistentRule)scr.SubscriptVariables(NextVNumber());
            if (CanApplyRuleAt(emptyR, History.Count - 1, out SigmaFactory? _))
            {
                // Note that the SigmaFactory can be ignored, as there is no state to compare with.
                if (emptyR.NonceDeclarations.Any())
                {
                    Nession nonceNession = new(InitialRule, History, VNumber);
                    nonceNession.History[^1].Rules.Add(emptyR);
                    nonceNession.UpdateNonceDeclarations();
                    generated.Add(nonceNession);
                }
                else
                {
                    History[^1].Rules.Add(emptyR);
                }
            }
            return generated;
        }*/

        //for (int frameOffset = maxTraceLength - 1; frameOffset < History.Count; frameOffset++)
        //{
        // Do a check to ensure that we don't already have the same rule already added.
        foreach (StateConsistentRule existingRule in History[^1].Rules)
        {
            if (existingRule.MatchesTagOf(scr))
            {
                return generated;
            }
        }

        StateConsistentRule r = (StateConsistentRule)scr.SubscriptVariables(NextVNumber());
        if (CanApplyRuleAt(r, History.Count - 1, out SigmaFactory? sf))
        {
            // Determine the final form of the rule.
            Debug.Assert(sf != null);
            SigmaMap fwdMap = sf.CreateForwardMap();
            SigmaMap bwdMap = sf.CreateBackwardMap();
            StateConsistentRule updatedRule = (StateConsistentRule)r.PerformSubstitution(fwdMap);
            updatedRule.IdTag = scr.IdTag;
            if (bwdMap.IsEmpty)
            {
                Frame historyFrame = History[^1];
                historyFrame.Rules.Add(updatedRule);
                UpdateNonceDeclarations();
            }
            else
            {
                Nession updatedNession = Substitute(bwdMap);
                Frame historyFrame = updatedNession.History[^1];
                historyFrame.Rules.Add(updatedRule);
                updatedNession.UpdateNonceDeclarations();
                generated.Add(updatedNession);
            }
        }
        else
        {
            StepBackVNumber();
        }
        //}

        return generated;
    }

    #endregion
    #region Nession object opererations.

    public bool IsPrefixOf(Nession other)
    {
        if (other.History.Count < History.Count)
        {
            return false;
        }
        for (int i = 0; i < History.Count; i++)
        {
            if (!History[i].Equals(other.History[i]))
            {
                return false;
            }
        }
        return true;
    }

    public void CollectHornClauses(HashSet<HornClause> clauses, HashSet<IMessage> proceeding)
    {
        HashSet<IMessage> premises = new(proceeding);
        List<StateTransferringRule> accumulator = new();
        int rank = 0;
        foreach (Frame f in History)
        {
            premises.UnionWith(from fp in f.StateChangePremises where fp.IsKnow select fp.Messages.Single());
            if (f.TransferRule != null)
            {
                accumulator.Add(f.TransferRule);
            }
            foreach (StateConsistentRule r in f.Rules)
            {
                // Add know events.
                HashSet<IMessage> thisRulePremises = new(premises);
                thisRulePremises.UnionWith(from rp in r.Premises where rp.IsKnow select rp.Messages.Single());
                HornClause hc = new(r.Result.Messages.Single(), thisRulePremises);
                hc.Rank = rank;
                hc.Source = new NessionRuleSource(this, rank, new(accumulator), r);
                clauses.Add(hc);

                // Add make events.
                foreach (Event ep in r.Premises)
                {
                    if (ep.EventType == Event.Type.Make)
                    {
                        HornClause makeClause = new(ep.Messages.Single(), premises);
                        makeClause.Rank = rank;
                        makeClause.Source = new NessionRuleSource(this, rank, new(accumulator), r);
                        clauses.Add(makeClause);
                    }
                }
            }
            rank++;
        }
    }

    public void CollectHornClauses(HashSet<HornClause> clauses, HashSet<IMessage> proceeding, State? when)
    {
        if (when == null)
        {
            CollectHornClauses(clauses, proceeding);
            return;
        }

        HashSet<IMessage> premises = new(proceeding);
        HashSet<HornClause> buffered = new();
        HashSet<HornClause> allPrevAdded = new();
        List<StateTransferringRule> accumulator = new();

        Guard tempGuard = Guard.Empty;

        int rank = 0;
        foreach (Frame f in History)
        {
            premises.UnionWith(from fp in f.StateChangePremises where fp.IsKnow select fp.Messages.Single());
            if (f.TransferRule != null)
            {
                accumulator.Add(f.TransferRule);
            }
            foreach (StateConsistentRule r in f.Rules)
            {
                HashSet<IMessage> thisRulePremises = new(premises);
                thisRulePremises.UnionWith(from rp in r.Premises where rp.IsKnow select rp.Messages.Single());
                HornClause hc = new(r.Result.Messages.Single(), thisRulePremises);
                hc.Rank = rank;
                hc.Source = new NessionRuleSource(this, rank, new(accumulator), r);
                buffered.Add(hc);

                foreach (Event ep in r.Premises)
                {
                    if (ep.EventType == Event.Type.Make)
                    {
                        HornClause makeClause = new(ep.Messages.Single(), premises);
                        makeClause.Rank = rank;
                        makeClause.Source = new NessionRuleSource(this, rank, new(accumulator), r);
                        buffered.Add(makeClause);
                    }
                }
            }

            bool matchState = false;
            foreach (State s in f.StateSet)
            {
                if (when.CanBeUnifiedTo(s, tempGuard, new()))
                {
                    matchState = true;
                    break;
                }
            }
            if (matchState)
            {
                buffered.ExceptWith(allPrevAdded);
                clauses.UnionWith(buffered);
                allPrevAdded.UnionWith(buffered);
                buffered.Clear();
            }
            rank++;
        }
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => string.Join("\n", from f in History select f.ToString());

    #endregion

}
