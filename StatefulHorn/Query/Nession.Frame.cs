using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn.Query;

public partial class Nession
{

    /// <summary>
    /// A mutable class that represents a history frame within a Nession. It is used to collect
    /// State Consistent Rules for a given state set, which can then be transformed into
    /// Horn Clauses.
    /// </summary>
    public class Frame
    {

        /// <summary>
        /// Create a new frame with the given initial State Cells, rules and collective guard.
        /// </summary>
        /// <param name="cells">Starting StateCells. These are not expected to change.</param>
        /// <param name="rules">
        /// Starting State Consistent Rules. These will be added to over the lifetime of the
        /// frame.
        /// </param>
        /// <param name="guard">Collective guard in force.</param>
        public Frame(List<StateCell> cells,
                     HashSet<StateConsistentRule> rules,
                     Guard? guard)
        {
            cells.Sort();
            Cells = cells;
            Rules = rules;
            GuardStatements = guard ?? Guard.Empty;
        }

        public IReadOnlyList<StateCell> Cells { get; private init; }

        /// <summary>
        /// The State Consistent Rules that have been found to apply to this frame. This member
        /// is expected to be directly manipulated by code external to this class for adding 
        /// or removing rules.
        /// </summary>
        public HashSet<StateConsistentRule> Rules { get; private init; }

        public Guard GuardStatements { get; private init; }

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
                StateConsistentRule newR = (StateConsistentRule)r.Substitute(map);
                newR.IdTag = r.IdTag;
                updatedRules.Add(newR);
            }
            return new(updatedCells, updatedRules, GuardStatements.Substitute(map));
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
                gs = gs.Union(str.Guard);
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

        public override int GetHashCode() => Cells[0].GetHashCode(); // Lazy but deterministic.
    }

}
