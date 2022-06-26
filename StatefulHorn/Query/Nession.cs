using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using StatefulHorn.Messages;
using StatefulHorn.Query.Origin;

namespace StatefulHorn.Query;

/// <summary>
/// A Nonce sESSION. This class provides a symbolic trace once a specific nonce has been set.
/// </summary>
/// <remarks>
/// Nession has two nested class definitions: StateCell and Frame. To make the files easier
/// to edit and manage (while maintaining their namespace encapsulation), these nested 
/// classes are defined in separate files.
/// </remarks>
public partial class Nession
{

    /// <summary>
    /// Create a new Nession with the first frame's state cells set with the given states.
    /// </summary>
    /// <param name="initStates">Starting states of the nession.</param>
    public Nession(IEnumerable<State> initStates)
    {
        List<State> states = new(initStates);
        states.Sort();
        List<StateCell> cells = new(from s in initStates select new StateCell(s, null));
        _History.Add(new(cells, new(), null));
    }

    /// <summary>
    /// Internal structure that is called for the purposes of cloning and substituting. As this
    /// is starting a new nession based on an old one, it 
    /// </summary>
    /// <param name="frames">Frames to use.</param>
    /// <param name="lastVNumber">
    /// Last vNumber used within the Nession. See the VNumber member for details on its 
    /// significance.
    /// </param>
    private Nession(IEnumerable<Frame> frames, int lastVNumber)
    {
        _History.AddRange(frames);
        UpdateNonceDeclarations();
        VNumber = lastVNumber;
    }

    #region Properties.

    /// <summary>
    /// The internally accessible list of history frames.
    /// </summary>
    private readonly List<Frame> _History = new();

    /// <summary>
    /// The publicly accessible list of history frames, typically used for display purposes.
    /// </summary>
    public IReadOnlyList<Frame> History => _History;

    /// <summary>
    /// The set of nonces that have been declared by a New event so far in this nession.
    /// </summary>
    private readonly HashSet<Event> NonceDeclarations = new();

    /// <summary>
    /// String tag that is used to provide a user-readable description identifying this Nession.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// An attack that has been found on this nession. This is set by the QueryEngine.
    /// </summary>
    public Attack? FoundAttack { get; set; }

    /// <summary>
    /// A property set by the QueryEngine to provide quick access to the FULL set of HornClauses
    /// in a model, including those that are not directly derived from this Nession.
    /// </summary>
    public IReadOnlySet<HornClause>? FoundSystemClauses { get; set; }

    /// <summary>
    /// A value that provides the next unique subscript for the application of variables in rules.
    /// </summary>
    private int VNumber = 0;

    #endregion
    #region Private convenience.

    /// <summary>
    /// Retrieve the next guaranteed unique subscript for variables.
    /// </summary>
    /// <returns>A string of form v{number}.</returns>
    private string NextVNumber()
    {
        VNumber++;
        return $"v{VNumber}";
    }

    /// <summary>
    /// If a string retrieved using NextVNumber() is found to be not required, then this method may
    /// be called to make that string available again.
    /// </summary>
    private void StepBackVNumber()
    {
        VNumber--;
    }

    /// <summary>
    /// Performs a complete update of the set of new events held by the nession.
    /// </summary>
    private void UpdateNonceDeclarations()
    {
        NonceDeclarations.Clear();
        foreach (Frame f in _History)
        {
            NonceDeclarations.UnionWith(f.NewEventsInStateChangeRules());
            foreach (StateConsistentRule scr in f.Rules)
            {
                NonceDeclarations.UnionWith(scr.NewEvents);
            }
        }
    }

    #endregion
    #region State transferring rule application.

    /// <summary>
    /// Perform the substitution of a set of messages with another set of messages throughout a
    /// Nession.
    /// </summary>
    /// <param name="map">Map of value replacements.</param>
    /// <returns>
    /// A new Nession with the values replaced. If an empty SigmaMap is provided, the Nession is
    /// effectively cloned.
    /// </returns>
    public Nession Substitute(SigmaMap map)
    {
        if (map.IsEmpty)
        {
            return new(_History, VNumber);
        }
        return new(from f in _History select f.Substitute(map), VNumber);
    }

    /// <summary>
    /// Attempt to apply multiple State Transferring Rules at once. If any of the given rules 
    /// cannot be applied, none of the rules are applied.
    /// </summary>
    /// <param name="transfers">List of State Transferring Rules to attempt to apply.</param>
    /// <returns>
    /// A tuple of two values. If the rules could not be applied, then (null, false) is returned.
    /// Otherwise, the first value is the new Nession with the rules applied, and the second is 
    /// true if the previous Nession is still a prefix to the new one. This can be used to 
    /// determine whether an old Nession needs to be retained for further processing at the 
    /// NessionManager level.
    /// </returns>
    public (Nession?, bool) TryApplyMultipleTransfers(List<StateTransferringRule> transfers)
    {
        // Create tranfer rule working list, and make variables unique.
        List<StateTransferringRule> subTransfers = new(transfers.Count);
        for (int i = 0; i < transfers.Count; i++)
        {
            subTransfers.Add((StateTransferringRule)transfers[i].SubscriptVariables(NextVNumber()));
        }

        // Sort out the sigma maps.
        SigmaFactory sf = new();
        for (int i = 0; i < subTransfers.Count; i++)
        {
            if (!CanApplyRule(subTransfers[i], sf))
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
            subTransfers[i] = (StateTransferringRule)subTransfers[i].Substitute(fwdMap);
        }

        // Create new frame.
        Frame nextFrame = updated._History[^1].ApplyTransfers(subTransfers);
        if (nextFrame.CellsEqual(updated._History[^1]))
        {
            return (null, false);
        }
        updated._History.Add(nextFrame);
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
        for (int i = 0; i < r.Snapshots.Traces.Count; i++)
        {
            Snapshot ss = r.Snapshots.Traces[i];
            int historyId = _History.Count - 1;
            string scName = ss.Condition.Name;

            // Current snapshot MUST match.
            Frame hf = _History[historyId];
            State? nessionCondition = hf.GetStateByName(scName);
            if (nessionCondition == null ||
                !ss.Condition.CanBeUnifiableWith(nessionCondition, hf.GuardStatements, r.Guard, sf))
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
                hf = _History[historyId];
                nessionCondition = hf.GetStateByName(scName);
                if (nessionCondition == null)
                {
                    // Consistency issue if the condition cannot be found.
                    throw new InvalidOperationException($"Cannot find previous mentions of state {scName}.");
                }

                if (!lastMatched.Equals(nessionCondition)) // "No change" is ignored.
                {
                    //bool canMatch = 
                    if (prev.S.Condition.CanBeUnifiableWith(nessionCondition, hf.GuardStatements, r.Guard, sf))
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

    /// <summary>
    /// Returns true if the rule does not declare any nonces that have been previously declared,
    /// and that it does not use nonces that have not been created.
    /// </summary>
    /// <param name="r">Rule to check.</param>
    /// <returns>
    /// True if the rule's use of nonces does not preclude its use in the final frame.
    /// </returns>
    private bool RuleValidByNonces(Rule r)
    {
        if (r.NonceDeclarations.Any((ev) => NonceDeclarations.Contains(ev))) 
        {
            // Do not allow redeclaration of nonces.
            return false;
        }
        foreach (NonceMessage nMsg in r.NoncesRequired) 
        {
            // Ensure used nonces have been previously declared.
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

    /// <summary>
    /// Attempt to apply the given State Consistent Rule to the final history frame.
    /// </summary>
    /// <param name="scr">Rule to attempt to add.</param>
    /// <returns>
    /// A list of either one or two nessions. If the rule can be added without requiring 
    /// substitution of prior messages in the nession, then only this nession is returned.
    /// Otherwise, both the original nession and the new nession with the applied 
    /// system rule is returned.
    /// </returns>
    public List<Nession> TryApplySystemRule(StateConsistentRule scr)
    {
        List<Nession> generated = new() { this };

        // Do a check to ensure that we don't have the same rule already added.
        foreach (StateConsistentRule existingRule in _History[^1].Rules)
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
            StateConsistentRule updatedRule = (StateConsistentRule)r.Substitute(fwdMap);
            updatedRule.IdTag = scr.IdTag;
            if (bwdMap.IsEmpty)
            {
                Frame historyFrame = _History[^1];
                historyFrame.Rules.Add(updatedRule);
                NonceDeclarations.UnionWith(updatedRule.NonceDeclarations);
            }
            else
            {
                Nession updatedNession = Substitute(bwdMap);
                Frame historyFrame = updatedNession._History[^1];
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

    /// <summary>
    /// Attempts to return a version of this nession where the given state cell is unifiable
    /// with a State Cell in the final frame.
    /// </summary>
    /// <param name="when">State cell to be unifiable with.</param>
    /// <returns>
    /// If possible, a version of this Nession (i.e. with substitutions made) that has a final
    /// State Cell that is unifiable with the parameter when. Otherwise null is returned.
    /// </returns>
    public Nession? MatchingWhenAtEnd(State when)
    {
        // When has to match ONE of the states in the final frame.
        foreach (StateCell c in _History[^1].Cells)
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
        int cellOffset = _History[^1].GetCellOffsetByName(cell);
        return new(InnerPremisesForState(_History.Count - 1, cellOffset));
    }

    private IEnumerable<Event> InnerPremisesForState(int historyIndex, int cellOffset)
    {
        // See if this has already been determined.
        Frame f = _History[historyIndex];
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
                    int innerCellOffset = _History[historyIndex - 1].GetCellOffsetByName(ss.Condition.Name);
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

    /// <summary>
    /// Collect all of the rules that led to a given cell in a frame being set.
    /// </summary>
    /// <param name="historyIndex">Which frame the State Cell exists in.</param>
    /// <param name="cellOffset">Which State Cell to find the rules for.</param>
    /// <param name="skipImmediate">Skip the rule that immediately leads to this cell.</param>
    /// <returns>The set of rules that led to this State Cell being in its condition.</returns>
    private HashSet<StateTransferringRule> TransferringRulesForState(
        int historyIndex,
        int cellOffset,
        bool skipImmediate = false)
    {
        // Check if this has already been determined.
        Frame f = _History[historyIndex];
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
        for (int rank = 0; rank < _History.Count; rank++)
        {
            Frame f = _History[rank];

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
                    HornClause mkClause = new(mkPremise.Messages.Single(), kPremises, f.GuardStatements)
                    {
                        Rank = rank,
                        Source = new NessionRuleSource(this, rank, TransferringRulesForState(rank, stateI, true).ToList(), sc.TransferRule!)
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
                Guard g = f.GuardStatements.Union(r.Guard);
                List<StateTransferringRule> transferRules = new();
                foreach (Snapshot rSS in r.Snapshots.Traces)
                {
                    int cellOffset = f.GetCellOffsetByName(rSS.Condition.Name);
                    premises.UnionWith(InnerKnowsForState(rank, cellOffset));
                    transferRules.AddRange(TransferringRulesForState(rank, cellOffset));
                }
                HornClause rClause = new(r.Result.Messages.Single(), premises, g)
                {
                    Rank = rank,
                    Source = new NessionRuleSource(this, rank, transferRules, r)
                };
                clauses.Add(rClause);

                // Add make events.
                foreach (Event ep in r.Premises)
                {
                    if (ep.EventType == Event.Type.Make)
                    {
                        HornClause mkClause = new(ep.Messages.Single(), premises, g)
                        {
                            Rank = rank,
                            Source = new NessionRuleSource(this, rank, transferRules, r)
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
        foreach (Frame f in _History)
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

    public override string ToString() => string.Join("\n", from f in _History select f.ToString());

    public override bool Equals(object? obj)
    {
        if (obj is Nession n)
        {
            if (n._History.Count == _History.Count)
            {
                for (int i = 0; i < _History.Count; i++)
                {
                    if (!_History[i].Equals(n._History[i]))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        return false;
    }

    public override int GetHashCode() => _History[^1].Cells.First().GetHashCode();

    #endregion

}
