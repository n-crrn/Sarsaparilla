﻿using System;
using System.Collections.Generic;

namespace StatefulHorn.Query;

public partial class Nession
{

    /// <summary>
    /// Represents a named cell within a frame, which contains a value. This named value is used
    /// to determine which rules can apply in a frame, and which values the cell may take in the
    /// following frames. State Cells are ordered within a Nession Frame alphabetically. By doing
    /// this, indices can be used to predictably and quickly access cells within a Frame.
    /// </summary>
    public class StateCell : IComparable, IComparable<StateCell>
    {

        /// <summary>
        /// Create a new cell containing the given condition. If the condition was generated by
        /// extending a Nession with a State Transferring Rule, then the rule given is not 
        /// null.
        /// </summary>
        /// <param name="c">Condition for the state cell.</param>
        /// <param name="transfer">
        /// Transfer is the State Transferring Rule that led to the condition. If it is null, 
        /// the condition was set as an initial condition.
        /// </param>
        public StateCell(State c, StateTransferringRule? transfer = null)
        {
            Condition = c;
            TransferRule = transfer;
        }

        /// <summary>The state represented within the cell.</summary>
        public State Condition { get; private init; }

        /// <summary>
        /// The State Transferring Rule that led to this state's creation, if there was one.
        /// </summary>
        public StateTransferringRule? TransferRule { get; private init; }

        /// <summary>
        /// This member is used by Nession to store the full list of premises that led to this
        /// State Cell holding this value.
        /// </summary>
        internal HashSet<Event>? CachedPremises;

        /// <summary>
        /// This member is used by Nession to store the full list of rules that led to this State
        /// Cell holding this value.
        /// </summary>
        internal HashSet<StateTransferringRule>? CachedLeadupRules;

        /// <summary>
        /// Create a new StateCell with messages substituted as per the given SigmaMap.
        /// </summary>
        /// <param name="sigmaMap">Replacements for the substitution.</param>
        /// <returns>A new StateCell with substitutions made.</returns>
        public StateCell Substitute(SigmaMap sigmaMap)
        {
            return new(
                Condition.CloneWithSubstitution(sigmaMap),
                (StateTransferringRule?)TransferRule?.Substitute(sigmaMap));
        }

        /// <summary>
        /// Return all occurrences of the make event in an assigned State Consistent Rule's 
        /// premise. This method is used as part of Horn Clause generation.
        /// </summary>
        /// <returns>Sequence of make events.</returns>
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

        #region IComparable implementation.

        public int CompareTo(object? obj) => obj is StateCell sc ? CompareTo(sc) : 1;

        public int CompareTo(StateCell? sc) => sc == null ? 1 : Condition.CompareTo(sc.Condition);

        #endregion
        #region Basic object overrides.

        public override bool Equals(object? obj)
        {
            return obj is StateCell sc
                && Condition.Equals(sc.Condition)
                && Equals(TransferRule, sc.TransferRule);
        }

        public override int GetHashCode() => Condition.GetHashCode();

        #endregion

    }

}
