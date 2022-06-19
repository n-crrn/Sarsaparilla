using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

public static class SSOrdering
{
    public static string OperatorString(this Snapshot.Ordering o) => o switch
    {
        Snapshot.Ordering.LaterThan => "≤",
        Snapshot.Ordering.ModifiedOnceAfter => "⋖",
        _ => "INVALID"
    };

    public static bool AsOrMoreOrganisedThan(this Snapshot.Ordering o1, Snapshot.Ordering o2)
    {
        return !(o1 == Snapshot.Ordering.LaterThan && o2 != Snapshot.Ordering.LaterThan);
    }
}

public class Snapshot
{
    public enum Ordering
    {
        LaterThan,         // Models ≤ relationship.
        ModifiedOnceAfter, // Models ⋖ relationship.
    }

    public Snapshot(State condDefinition, string? lbl = null)
    {
        Condition = condDefinition;
        Premises = new();
        Label = lbl;
    }

    internal Snapshot(Snapshot ss)
    {
        Condition = ss.Condition;
        Premises = new(ss.Premises);
        TransfersTo = ss.TransfersTo;
        Label = ss.Label;
    }

    public readonly HashSet<Event> Premises;

    public State Condition { get; init; }

    /// <summary>
    /// This data structure is dedicated to representing the prior snapshot and what its
    /// relationship is to this snapshot. They are paired as they either need to both 
    /// be valid, or null. Using a Tuple instead of this Record results in unusual
    /// syntactic behaviour.
    /// </summary>
    /// <param name="S">The Snapshot.</param>
    /// <param name="O">Whether the Snapshot is immediately before or not.</param>
    public record PriorLink(Snapshot S, Ordering O);

    public PriorLink? Prior { get; private set; }

    public State? TransfersTo { get; set; }

    public Snapshot PerformSubstitutions(SigmaMap substitutions)
    {
        Snapshot ss = new(Condition.CloneWithSubstitution(substitutions), Label);
        ss.Premises.UnionWith(from p in Premises select p.PerformSubstitution(substitutions));
        ss.TransfersTo = TransfersTo?.CloneWithSubstitution(substitutions);
        ss.Prior = Prior == null ? null : new(Prior.S.PerformSubstitutions(substitutions), Prior.O);
        return ss;
    }

    public Snapshot 
        CloneTraceWithReplacements(Dictionary<Event, Event> replacements, SigmaMap substitutions)
    {
        Snapshot ss = new(Condition.CloneWithSubstitution(substitutions), Label);
        if (TransfersTo != null)
        {
            ss.TransfersTo = TransfersTo.CloneWithSubstitution(substitutions);
        }
        foreach (Event ev in Premises)
        {
            if (replacements.TryGetValue(ev, out Event? replaceEv))
            {
                ss.Premises.Add(replaceEv);
            }
            else
            {
                ss.Premises.Add(ev);
            }
        }
        if (Prior != null)
        {
            ss.Prior = new(Prior.S.CloneTraceWithReplacements(replacements, substitutions), Prior.O);
        }
        return ss;
    }

    public Snapshot CloneTrace()
    {
        Snapshot ss = new(Condition, Label)
        {
            TransfersTo = TransfersTo
        };
        ss.Premises.UnionWith(Premises);
        if (Prior != null)
        {
            ss.Prior = new(Prior.S.CloneTrace(), Prior.O);
        }
        return ss;
    }

    internal void CollectSnapshotsAssociatedWith(Event ev, List<Snapshot> foundList)
    {
        if (Premises.Contains(ev))
        {
            foundList.Add(this);
        }
        Prior?.S.CollectSnapshotsAssociatedWith(ev, foundList);
    }

    #region Assessing relationships with other snapshots.

    public bool IsRelatable(State other) => other.Name == Condition.Name;

    public void SetLaterThan(Snapshot other) => Prior = new(other, Ordering.LaterThan);

    public void SetModifiedOnceLaterThan(Snapshot other) => Prior = new(other, Ordering.ModifiedOnceAfter);
    
    public bool IsAfter(Snapshot other) => Prior != null && (Prior.S == other || Prior.S.IsAfter(other));

    public bool HasPredecessor => Prior != null;

    private HashSet<Snapshot> AncestorSet(HashSet<Snapshot> links)
    {
        if (Prior != null)
        {
            Prior.S.AncestorSet(links);
            links.Add(Prior.S);
        }
        return links;
    }

    public bool AllPredecessorsContainedIn(List<Snapshot> predecessorList)
    {
        HashSet<Snapshot> allPriors = AncestorSet(new());
        foreach (Snapshot ss in predecessorList)
        {
            if (!allPriors.Contains(ss))
            {
                return false;
            }
        }
        return true;
    }

    public bool HasImmediatePredecessor => Prior != null && Prior.O == Ordering.ModifiedOnceAfter;

    #endregion
    #region Filtering

    public bool ContainsMessage(IMessage msg) => Condition.ContainsMessage(msg) || (Prior != null && Prior.S.ContainsMessage(msg));

    public bool ContainsState(State sOther) => Condition.Equals(sOther) || (Prior != null && Prior.S.ContainsState(sOther));

    #endregion

    /// <summary>
    /// Changes the trace from a tree to a list, and is useful for operations which need to be
    /// applied to all conditions or premises once per invocation.
    /// </summary>
    /// <param name="allSS">List to add found items to.</param>
    public void FlattenToList(List<Snapshot> allSS)
    {
        Prior?.S.FlattenToList(allSS);
        allSS.Add(this);
    }

    #region Premise handling.

    internal void AddPremises(IEnumerable<Event> events)
    {
        Premises.UnionWith(events);
    }

    internal void ReplacePremises(Event toBeReplaced, IEnumerable<Event> replacements)
    {
        Premises.Remove(toBeReplaced);
        AddPremises(replacements);
    }

    /// <summary>
    /// Returns a set of all the Events that are featured in the premises of the trace.
    /// </summary>
    public HashSet<Event> EventsInTrace
    {
        get
        {
            HashSet<Event> allEvents = new();
            Snapshot? currentSS = this;
            while (currentSS != null)
            {
                allEvents.UnionWith(currentSS.Premises);
                currentSS = currentSS.Prior?.S;
            }
            return allEvents;
        }
    }

    #endregion
    #region Labelling and display.

    public string? Label { get; set; }

    public static void AutolabelOrderedSnapshots(IEnumerable<Snapshot> snapshots)
    {
        int i = 0;
        foreach (Snapshot ss in snapshots)
        {
            ss.Label = $"a_{i}";
            i++;
        }
    }
    #endregion
    #region Basic object overrides.

    public override string ToString()
    {
        string lbl = Label ?? "UNLABELLED";
        return $"({Condition}, {lbl})";
    }

    public override bool Equals(object? obj) //=> obj is Snapshot ss && Condition.Equals(ss.Condition)
    {
        return obj is Snapshot ss &&
            Condition.Equals(ss.Condition) &&
            ((Prior == null && ss.Prior == null) || (Prior != null && Prior.Equals(ss.Prior)));
    }

    public override int GetHashCode() => Condition.GetHashCode();

    #endregion

    /// <summary>
    /// Checks whether another trace is absolutely equivalent to this one, including in the
    /// premises mapped to snapshots. This method is used as part of the redundancy
    /// removal in SnapshotTree.
    /// </summary>
    /// <param name="other">Other snapshot trace to compare with.</param>
    /// <returns>True if the other snapshot is entirely equivalent.</returns>
    public bool EqualsIncludingPremises(Snapshot other)
    {
        return Equals(other) && Premises.Count == other.Premises.Count && Premises.SetEquals(other.Premises);
    }
}
