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

    private Nession(Rule? initRule, IEnumerable<Frame> frames, int lastVNumber)
    {
        InitialRule = initRule;
        History.AddRange(frames);
        UpdateNonceDeclarations();
        VNumber = lastVNumber;
    }

    #region Properties.

    public List<Frame> History { get; } = new();

    // Used when determining if the Nession can be integrated with another Nession.
    public Rule? InitialRule { get; init; }

    private HashSet<Event> NonceDeclarations { get; } = new();

    public Attack? FoundAttack { get; set; }

    public bool AttackFound => FoundAttack != null;

    // Used to simplify identification by the user.
    public string Label { get; set; } = "";

    #endregion
    #region Nested Frame Class

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

        public IEnumerable<Event> StateChangeMakes => from scp in StateChangePremises where scp.EventType == Event.Type.Make select scp;

        public IEnumerable<Event> StateChangeKnows => from scp in StateChangePremises where scp.IsKnow select scp;

        public IEnumerable<Event> StateChangeNews => from scp in StateChangePremises where scp.IsNew select scp;

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
            HashSet<Event> newMakes = new(from m in StateChangeMakes select m.PerformSubstitution(map));
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

    #endregion
    #region Private convenience.

    private void UpdateNonceDeclarations()
    {
        NonceDeclarations.Clear();
        foreach (Frame f in History)
        {
            NonceDeclarations.UnionWith(f.StateChangeNews);
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

    //public Nession Substitute(SigmaMap map) => new(InitialRule, from f in History select f.Substitute(map), VNumber);

    public Nession Substitute(SigmaMap map)
    {
        if (map.IsEmpty)
        {
            return new(InitialRule, from f in History select f.Clone(), VNumber);
        }
        return new(InitialRule, from f in History select f.Substitute(map), VNumber);
    }

    public (Nession?, bool) TryApplyTransfer(StateTransferringRule str)
    {
        StateTransferringRule r = (StateTransferringRule)str.SubscriptVariables(NextVNumber());
        if (CanApplyRuleAt(r, History.Count - 1, out SigmaFactory? sf))
        {
            Debug.Assert(sf != null);
            SigmaMap fwdMap = sf.CreateForwardMap();
            SigmaMap bwdMap = sf.CreateBackwardMap();

            bool canRemoveThis = bwdMap.IsEmpty;
            Nession updated = Substitute(bwdMap);
            StateTransferringRule updatedRule = (StateTransferringRule)r.PerformSubstitution(fwdMap);
            HashSet<State> newStateSet = updated.CreateStateSetOnTransfer(updatedRule);
            // It is possible to have duplicate states pop up in a trace, especially if you have
            // some sort of reset rule. This logic prevent it.
            if (!(updated.History.Count > 0 && newStateSet.SetEquals(updated.History[^1].StateSet)))
            {
                Frame newFrame = new(
                    new(updatedRule.Premises),
                    newStateSet,
                    new());
                updated.History.Add(newFrame);
                newFrame.TransferRule = updatedRule;
                UpdateNonceDeclarations();

                return (updated, canRemoveThis);
            }
        }
        StepBackVNumber();
        return (null, false);
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
            //if (fwdMap.IsEmpty)
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

        return generated;
    }

    #endregion
    #region When state instantiation and querying.

    public Nession? MatchingWhenAtEnd(State when)
    {
        // When has to match ONE of the states in the final frame.
        foreach (State s in History[^1].StateSet)
        {
            SigmaFactory sf = new();

            if (when.CanBeUnifiableWith(s, Guard.Empty, sf))
            {
                // The forward map does not matter - the backward match does matter as it will
                // propogate any required constants or nonces backwards in time.
                SigmaMap bwdMap = sf.CreateBackwardMap();
                if (bwdMap.IsEmpty)
                {
                    return this;
                }
                else
                {
                    return Substitute(bwdMap);
                }
            }
        }
        return null;
    }

    public HashSet<IMessage> FinalStatePremises()
    {
        HashSet<IMessage> accumulator = new();
        foreach (Frame f in History)
        {
            accumulator.UnionWith(from fp in f.StateChangePremises where fp.IsKnow select fp.Messages.Single());
        }
        return accumulator;
    }

    public HashSet<IMessage> FinalStateNonVariablePremises()
    {
        return new(from msg in FinalStatePremises() where msg is not VariableMessage select msg);
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
            // Work out the premises required to get to this state ...
            premises.UnionWith(from fp in f.StateChangeKnows select fp.Messages.Single());
            // ... and create clauses for the makes ...
            foreach (Event mk in f.StateChangeMakes)
            {
                HornClause makeClause = new(mk.Messages.Single(), from p in f.StateChangePremises where p.IsKnow select p.Messages.Single())
                {
                    Rank = rank,
                    // Note that the Transfer rule for the nession *must* not be null for there to be
                    // a make event in the nession's premises.
                    Source = new NessionRuleSource(this, rank, new(accumulator), f.TransferRule!)
                };
                clauses.Add(makeClause);
            }
            // ... and add the state transfer rule to the accumulated rules.
            if (f.TransferRule != null)
            {
                accumulator.Add(f.TransferRule);
            }

            // For every rule...
            foreach (StateConsistentRule r in f.Rules)
            {
                // ... add know events ...
                HashSet<IMessage> thisRulePremises = new(premises);
                thisRulePremises.UnionWith(from rp in r.Premises where rp.IsKnow select rp.Messages.Single());
                HornClause hc = new(r.Result.Messages.Single(), thisRulePremises)
                {
                    Rank = rank,
                    Source = new NessionRuleSource(this, rank, new(accumulator), r)
                };
                clauses.Add(hc);

                // ... and add make events.
                foreach (Event ep in r.Premises)
                {
                    if (ep.EventType == Event.Type.Make)
                    {
                        HornClause makeClause = new(ep.Messages.Single(), premises)
                        {
                            Rank = rank,
                            Source = new NessionRuleSource(this, rank, new(accumulator), r)
                        };
                        clauses.Add(makeClause);
                    }
                }
            }
            rank++;
        }
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => string.Join("\n", from f in History select f.ToString());

    #endregion

}
