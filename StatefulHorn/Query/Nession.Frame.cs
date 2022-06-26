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

        /// <summary>
        /// The list of State Cells in alphabetical order of their name.
        /// </summary>
        public IReadOnlyList<StateCell> Cells { get; private init; }

        /// <summary>
        /// The State Consistent Rules that have been found to apply to this frame. This member
        /// is expected to be directly manipulated by code external to this class for adding 
        /// or removing rules.
        /// </summary>
        public HashSet<StateConsistentRule> Rules { get; private init; }

        /// <summary>
        /// The collective guard in force for the whole Frame. This prevents contradicting rules
        /// from being part of the same frame.
        /// </summary>
        public Guard GuardStatements { get; private init; }

        /// <summary>
        /// Return a State Cell based on its name. If there is no cell with the given name, then
        /// null is return.
        /// </summary>
        /// <param name="name">Name of the State Cell requested.</param>
        /// <returns>The condition of the State Cell, or null if the Cell does not exist.</returns>
        public State? GetStateByName(string name)
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                StateCell s = Cells[i];
                if (s.Condition.Name == name)
                {
                    return s.Condition;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the offset of the State Cell within the Frame.
        /// </summary>
        /// <param name="name">State Cell name.</param>
        /// <returns>
        /// An integer giving the offset of the cell, which can be used for quicker access than 
        /// the name.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// Thrown if no State Cell exists with the name.
        /// </exception>
        public int GetCellOffsetByName(string name)
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

        /// <summary>
        /// Perform a message substitution of ALL messages within the Frame, affecting all State
        /// Cells and rules.
        /// </summary>
        /// <param name="map">Substitutions to enact.</param>
        /// <returns>
        /// A new Frame with all messages substituted. If the map is empty, this is effectively a
        /// cloning operation.
        /// </returns>
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

        /// <summary>
        /// Apply all of the given State Transferring Rules to generate the next Nession Frame.
        /// </summary>
        /// <param name="transfers">List of rules to apply to create new Frame.</param>
        /// <returns>New extended Frame.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if one or more of the rules could not be applied.
        /// </exception>
        public Frame ApplyTransfers(IReadOnlyList<StateTransferringRule> transfers)
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

        /// <summary>
        /// Return all New Events in the State Transfer Rules leading to this cell.
        /// </summary>
        /// <returns>Sequence of New Events.</returns>
        public IEnumerable<Event> NewEventsInStateChangeRules()
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                StateCell sc = Cells[i];
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

        /// <summary>
        /// Converts the given enumerable of rules to a set of their IdTags. Used as part of
        /// Nession equality comparison.
        /// </summary>
        /// <param name="rules">Rules to convert.</param>
        /// <returns>Set of the IdTags set on the rules.</returns>
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

        /// <summary>
        /// Returns true if the State Cell conditions are the same as those in the other Frame.
        /// </summary>
        /// <param name="other">Frame to compare cells against.</param>
        /// <returns>True if cells found to be equal.</returns>
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
