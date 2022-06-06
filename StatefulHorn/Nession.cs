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
        List<State> states = new(initStates);
        states.Sort();
        List<StateCell> cells = new(from s in initStates select new StateCell(s, null));
        History.Add(new(cells, new(), null));
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

    // Used to simplify identification by the user.
    public string Label { get; set; } = "";

    /// <summary>
    /// A property that can be set if the QueryEngine determines that there is an attack on this
    /// Nession.
    /// </summary>
    public Attack? FoundAttack { get; set; }

    /// <summary>
    /// A property set by the QueryEngine to provide quick access to the FULL set of HornClauses
    /// in a model, including those that are not directly derived from this Nession.
    /// </summary>
    public IReadOnlySet<HornClause>? FoundSystemClauses { get; set; }

    #endregion
    #region Nested Frame class and support classes.

    public class StateCell : IComparable, IComparable<StateCell>
    {

        public StateCell(State c, StateTransferringRule? transfer = null)
        {
            Condition = c;
            TransferRule = transfer;
        }

        public State Condition;

        public StateTransferringRule? TransferRule;

        internal HashSet<Event>? CachedPremises;

        internal HashSet<StateTransferringRule>? CachedLeadupRules;

        public StateCell DeepCopy() => new(Condition, TransferRule);

        public StateCell Substitute(SigmaMap sigmaMap)
        {
            return new(
                Condition.CloneWithSubstitution(sigmaMap),
                (StateTransferringRule?)TransferRule?.PerformSubstitution(sigmaMap));
        }

        public int CompareTo(object? obj) => obj is StateCell sc ? CompareTo(sc) : 1;

        public int CompareTo(StateCell? sc) => sc == null ? 1 : Condition.CompareTo(sc.Condition);

        public override bool Equals(object? obj)
        {
            return obj is StateCell sc
                && Condition.Equals(sc.Condition)
                && Equals(TransferRule, sc.TransferRule);
        }

        public override int GetHashCode() => Condition.GetHashCode();

        public IEnumerable<Event> MakePremises()
        {
            if (TransferRule != null)
            {
                foreach (Event p in TransferRule.Premises)
                {
                    if (p.EventType == Event.Type.Make)
                    {
                        yield return p;
                    }
                }
            }
        }

    }

    public class Frame
    {
        public Frame(List<StateCell> cells,
                     HashSet<StateConsistentRule> rules,
                     Guard? guard)
        {
            Cells = cells;
            Cells.Sort();
            Rules = rules;
            GuardStatements = guard ?? Guard.Empty;
        }

        public Frame Clone() => new(new(from c in Cells select c.DeepCopy()), new(Rules), GuardStatements);

        public List<StateCell> Cells { get; init; }

        public HashSet<StateConsistentRule> Rules { get; init; }

        public Guard GuardStatements { get; init; }

        public State? GetStateByName(string name)
        {
            foreach (StateCell s in Cells)
            {
                if (s.Condition.Name == name)
                {
                    return s.Condition;
                }
            }
            return null;
        }

        internal int GetCellOffsetByName(string name)
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                if (Cells[i].Condition.Name == name)
                {
                    return i;
                }
            }
            throw new ArgumentException($"No cell named '{name}' within Nession");
        }

        public Frame Substitute(SigmaMap map)
        {
            List<StateCell> updatedCells = new(from c in Cells select c.Substitute(map));
            HashSet<StateConsistentRule> updatedRules = new();
            foreach (StateConsistentRule r in Rules)
            {
                StateConsistentRule newR = (StateConsistentRule)r.PerformSubstitution(map);
                newR.IdTag = r.IdTag;
                updatedRules.Add(newR);
            }
            return new(updatedCells, updatedRules, GuardStatements.PerformSubstitution(map));
        }

        public Frame ApplyTransfers(List<StateTransferringRule> transfers)
        {
            // It is assumed that the required sigma map applications have been conducted on both
            // this Nession and on the given rules.
            Guard gs = GuardStatements;
            List<StateCell> nextCells = new(Cells);
            foreach (StateTransferringRule str in transfers)
            {
                // Update guard.
                gs = gs.Union(str.GuardStatements);
                // Find the cell, and replace it.
                foreach ((Snapshot after, State newState) in str.Result.Transformations)
                {
                    bool found = false;
                    for (int i = 0; i < nextCells.Count; i++)
                    {
                        if (nextCells[i].Condition.Equals(after.Condition))
                        {
                            nextCells[i] = new(newState, str);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        throw new ArgumentException($"Transformation rule not applicable to frame: {str} to {this}.");
                    }
                }
            }
            return new(nextCells, new(), gs);
        }

        public bool ResultsContainMessage(IMessage msg) => (from r in Rules where r.Result.ContainsMessage(msg) select r).Any();

        public IEnumerable<Event> NewEventsInStateChangeRules()
        {
            foreach (StateCell sc in Cells)
            {
                if (sc.TransferRule != null)
                {
                    foreach (Event ev in sc.TransferRule.Premises)
                    {
                        if (ev.IsNew)
                        {
                            yield return ev;
                        }
                    }
                }
            }
        }

        public override string ToString() => string.Join(", ", from c in Cells select c.Condition.ToString());

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
                Cells.SequenceEqual(f.Cells) &&
                RulesToIds(Rules).SetEquals(RulesToIds(f.Rules)) &&
                Equals(GuardStatements, f.GuardStatements);
        }

        public bool CellsEqual(Frame other)
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                if (!Cells[i].Condition.Equals(other.Cells[i].Condition))
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode() => Cells.First().GetHashCode(); // Lazy but deterministic.
    }

    #endregion
    #region Private convenience.

    private void UpdateNonceDeclarations()
    {
        NonceDeclarations.Clear();
        foreach (Frame f in History)
        {
            NonceDeclarations.UnionWith(f.NewEventsInStateChangeRules());
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

    public Nession Substitute(SigmaMap map)
    {
        if (map.IsEmpty)
        {
            return new(InitialRule, from f in History select f.Clone(), VNumber);
        }
        return new(InitialRule, from f in History select f.Substitute(map), VNumber);
    }

    public (Nession?, bool) TryApplyMultipleTransfers(List<StateTransferringRule> transfers)
    {
        // Create tranfer rule working list, and make variables unique.
        List<StateTransferringRule> subTransfers = new();
        foreach (StateTransferringRule r in transfers)
        {
            subTransfers.Add((StateTransferringRule)r.SubscriptVariables(NextVNumber()));
        }
        
        // Sort out the sigma maps.
        SigmaFactory sf = new();
        foreach (StateTransferringRule str in subTransfers)
        {
            if (!CanApplyRule(str, sf))
            {
                return (null, false);
            }
        }
        SigmaMap fwdMap = sf.CreateForwardMap();
        SigmaMap bwdMap = sf.CreateBackwardMap();

        // Apply sigma maps.
        Nession updated = Substitute(bwdMap);
        for (int i = 0; i < subTransfers.Count; i++)
        {
            subTransfers[i] = (StateTransferringRule)subTransfers[i].PerformSubstitution(fwdMap);
        }

        // Create new frame.
        Frame nextFrame = updated.History[^1].ApplyTransfers(subTransfers);
        if (nextFrame.CellsEqual(updated.History[^1]))
        {
            return (null, false);
        }
        updated.History.Add(nextFrame);
        updated.UpdateNonceDeclarations();
        return (updated, bwdMap.IsEmpty);
    }

    public bool CanApplyRule(Rule r, SigmaFactory sf)
    {
        if (!RuleValidByNonces(r))
        {
            return false;
        }

        bool match = true;
        foreach (Snapshot ss in r.Snapshots.Traces)
        {
            int historyId = History.Count - 1;
            string scName = ss.Condition.Name;

            // Current snapshot MUST match.
            Frame hf = History[historyId];
            State? nessionCondition = hf.GetStateByName(scName);
            if (nessionCondition == null ||
                !ss.Condition.CanBeUnifiableWith(nessionCondition, hf.GuardStatements, r.GuardStatements, sf))
            {
                match = false;
                break;
            }

            // Try to match the history of the nession with the rule.
            Snapshot.PriorLink? prev = ss.Prior;
            State lastMatched = nessionCondition;
            historyId--;
            while (prev != null && historyId >= 0)
            {
                hf = History[historyId];
                nessionCondition = hf.GetStateByName(scName);
                if (nessionCondition == null)
                {
                    // Consistency issue if the condition cannot be found.
                    throw new InvalidOperationException($"Cannot find previous mentions of state {scName}.");
                }

                if (!lastMatched.Equals(nessionCondition)) // "No change" is ignored.
                {
                    //bool canMatch = 
                    if (prev.S.Condition.CanBeUnifiableWith(nessionCondition, hf.GuardStatements, r.GuardStatements, sf))
                    {
                        lastMatched = nessionCondition;
                        prev = prev.S.Prior;
                    }
                    else if (prev.O == Snapshot.Ordering.ModifiedOnceAfter)
                    {
                        match = false;
                        break;
                    }
                }
                historyId--;
            }
            if (prev != null)
            {
                match = false;
            }

            // Escape if we have disproven the match.
            if (!match)
            {
                break;
            }
        }

        return match;
    }

    private bool RuleValidByNonces(Rule r)
    {
        if (r.NonceDeclarations.Any((Event ev) => NonceDeclarations.Contains(ev))) // Do not allow redeclaration of nonces.
        {
            return false;
        }
        foreach (NonceMessage nMsg in r.NoncesRequired) // Ensure used nonces have been previously declared.
        {
            if (!NonceDeclarations.Contains(Event.New(nMsg)))
            {
                string held = string.Join(", ", NonceDeclarations);
                return false;
            }
        }
        return true;
    }

    #endregion
    #region System rule application.

    public List<Nession> TryApplySystemRule(StateConsistentRule scr)
    {
        Debug.Assert(!scr.Snapshots.IsEmpty);
        List<Nession> generated = new() { this };

        // Do a check to ensure that we don't have the same rule already added.
        foreach (StateConsistentRule existingRule in History[^1].Rules)
        {
            if (existingRule.MatchesTagOf(scr))
            {
                return generated;
            }
        }

        StateConsistentRule r = (StateConsistentRule)scr.SubscriptVariables(NextVNumber());
        SigmaFactory sf = new();
        if (CanApplyRule(r, sf))
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

        return generated;
    }

    #endregion
    #region When state instantiation and querying.

    public Nession? MatchingWhenAtEnd(State when)
    {
        // When has to match ONE of the states in the final frame.
        foreach (StateCell c in History[^1].Cells)
        {
            SigmaFactory sf = new();

            if (when.CanBeUnifiableWith(c.Condition, Guard.Empty, Guard.Empty, sf))
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

    public HashSet<IMessage> FinalStateNonVariablePremises(string cellName)
    {
        HashSet<IMessage> filtered = new();
        foreach (Event ev in PremisesForState(cellName))
        {
            IMessage msg = ev.Messages[0];
            if (msg is not VariableMessage)
            {
                filtered.Add(msg);
            }
        }
        return filtered;
    }

    #endregion
    #region Nession object opererations.

    public HashSet<Event> PremisesForState(string cell)
    {
        int cellOffset = History[^1].GetCellOffsetByName(cell);
        return new(InnerPremisesForState(History.Count - 1, cellOffset));
    }

    private IEnumerable<Event> InnerPremisesForState(int historyIndex, int cellOffset)
    {
        // See if this has already been determined.
        Frame f = History[historyIndex];
        StateCell c = f.Cells[cellOffset];
        if (c.CachedPremises != null)
        {
            return c.CachedPremises;
        }
        HashSet<Event> allPremises = new();

        // Deal with the immediate transfer rule at this level.
        if (c.TransferRule != null)
        {
            allPremises.UnionWith(c.TransferRule.Premises);
        }
        
        // Collect prior transfer rules that would have affected this cell.
        if (historyIndex > 0)
        {
            allPremises.UnionWith(InnerPremisesForState(historyIndex - 1, cellOffset));

            if (c.TransferRule != null)
            {
                foreach (Snapshot ss in c.TransferRule.Snapshots.Traces)
                {
                    int innerCellOffset = History[historyIndex - 1].GetCellOffsetByName(ss.Condition.Name);
                    if (innerCellOffset != cellOffset)
                    {
                        allPremises.UnionWith(InnerPremisesForState(historyIndex - 1, innerCellOffset));
                    }
                }
            }
        }

        // Cache result and return.
        c.CachedPremises = allPremises;
        return allPremises;
    }

    private IEnumerable<IMessage> InnerKnowsForState(int historyIndex, int cellOffset)
    {
        return from ev in InnerPremisesForState(historyIndex, cellOffset) where ev.IsKnow select ev.Messages[0];
    }

    private HashSet<StateTransferringRule> TransferringRulesForState(int historyIndex, int cellOffset, bool skipImmediate = false)
    {
        // Check if this has already been determined.
        Frame f = History[historyIndex];
        StateCell c = f.Cells[cellOffset];
        if (c.CachedLeadupRules != null)
        {
            return c.CachedLeadupRules;
        }

        // Determine the rules.
        HashSet<StateTransferringRule> rules = new();
        if (c.TransferRule != null && !skipImmediate)
        {
            rules.Add(c.TransferRule);
        }
        if (historyIndex > 0)
        {
            rules.UnionWith(TransferringRulesForState(historyIndex - 1, cellOffset));
            if (c.TransferRule != null)
            {
                foreach (Snapshot ss in c.TransferRule.Snapshots.Traces)
                {
                    int innerCellOffset = f.GetCellOffsetByName(ss.Condition.Name);
                    if (innerCellOffset != cellOffset)
                    {
                        rules.UnionWith(TransferringRulesForState(historyIndex - 1, innerCellOffset, false));
                    }
                }
            }
        }

        // Cache for reuse.
        c.CachedLeadupRules = rules;
        return rules;
    }

    public void CollectHornClauses(HashSet<HornClause> clauses)
    {
        List<StateTransferringRule> accumulator = new();
        for (int rank = 0; rank < History.Count; rank++)
        {
            Frame f = History[rank];

            // Collect make statements - go through each cell rule.
            for (int stateI = 0; stateI < f.Cells.Count; stateI++)
            {
                StateCell sc = f.Cells[stateI];
                // Note that nothing is returned from sc.MakePremises if TransferRule == null.
                IEnumerable<IMessage>? kPremises = null;
                foreach (Event mkPremise in sc.MakePremises())
                {
                    if (kPremises == null)
                    {
                        kPremises = from p in InnerPremisesForState(rank, stateI)
                                    where p.IsKnow
                                    select p.Messages.Single();
                    }
                    HornClause mkClause = new(mkPremise.Messages.Single(), kPremises)
                    {
                        Rank = rank,
                        Source = new NessionRuleSource(this, rank, TransferringRulesForState(rank, stateI, true).ToList(), sc.TransferRule!),
                        Guard = f.GuardStatements
                    };
                    clauses.Add(mkClause);
                }
            }

            // Collect rules - note that make events also need to be made if they are in the
            // premise of the rules.
            foreach (StateConsistentRule r in f.Rules)
            {
                // Add know events.
                HashSet<IMessage> premises = new(from p in r.Premises where p.IsKnow select p.Messages.Single());
                Guard g = f.GuardStatements.Union(r.GuardStatements);
                List<StateTransferringRule> transferRules = new();
                foreach (Snapshot rSS in r.Snapshots.Traces)
                {
                    int cellOffset = f.GetCellOffsetByName(rSS.Condition.Name);
                    premises.UnionWith(InnerKnowsForState(rank, cellOffset));
                    transferRules.AddRange(TransferringRulesForState(rank, cellOffset));
                }
                HornClause rClause = new(r.Result.Messages.Single(), premises)
                {
                    Rank = rank,
                    Source = new NessionRuleSource(this, rank, transferRules, r),
                    Guard = g
                };
                clauses.Add(rClause);

                // Add make events.
                foreach (Event ep in r.Premises)
                {
                    if (ep.EventType == Event.Type.Make)
                    {
                        HornClause mkClause = new(ep.Messages.Single(), premises)
                        {
                            Rank = rank,
                            Source = new NessionRuleSource(this, rank, transferRules, r),
                            Guard = g
                        };
                        clauses.Add(mkClause);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns the variables that are fundamentally part of the state transition system.
    /// </summary>
    public HashSet<IMessage> FindStateVariables()
    {
        HashSet<IMessage> varSet = new();
        foreach (Frame f in History)
        {
            foreach (StateCell c in f.Cells)
            {
                varSet.UnionWith(c.Condition.Variables);
            }
        }
        return varSet;
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => string.Join("\n", from f in History select f.ToString());

    public override bool Equals(object? obj)
    {
        if (obj is Nession n)
        {
            if (n.History.Count == History.Count)
            {
                for (int i = 0; i < History.Count; i++)
                {
                    if (!History[i].Equals(n.History[i]))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        return false;
    }

    public override int GetHashCode() => History[^1].Cells.First().GetHashCode();

    #endregion

}
