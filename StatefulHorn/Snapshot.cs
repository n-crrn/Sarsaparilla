using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

public static class SSOrdering
{
    public static string OperatorString(this Snapshot.Ordering o) => o switch
    {
        Snapshot.Ordering.LaterThan => "≤",
        Snapshot.Ordering.ModifiedOnceAfter => "⋖",
        Snapshot.Ordering.Unchanged => "～",
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
        Unchanged          // Models ～ relationship.
    }

    public Snapshot(State condDefinition, string? lbl = null)
    {
        Condition = condDefinition;
        _AssociatedPremises = new();
        _PriorSnapshots = new();
        Label = lbl;
    }

    internal Snapshot(Snapshot ss)
    {
        Condition = ss.Condition;
        _AssociatedPremises = new(ss._AssociatedPremises);
        _PriorSnapshots = new();
        TransfersTo = ss.TransfersTo;
        Label = ss.Label;
    }

    internal int Tag; // Used for Loop detection by SnapshotTree.

    private readonly List<Event> _AssociatedPremises;

    public State Condition { get; init; }

    // Note that this keeps track of Snapshots going backwards in time. It is kept accessible
    // to SnapshotTree as that structure needs to be able to clone itself (see CloneTree()).
    internal readonly List<(Snapshot S, Ordering O)> _PriorSnapshots;

    public State? TransfersTo { get; set; }

    public Snapshot PerformSubstitutions(SigmaMap substitutions)
    {
        Dictionary<Event, Event> replacements = new();
        foreach (Event ev in EventsInTrace)
        {
            replacements[ev] = ev.PerformSubstitution(substitutions);
        }
        return CloneTraceWithReplacements(replacements, substitutions);
    }

    public Snapshot 
        CloneTraceWithReplacements(Dictionary<Event, Event> replacements, SigmaMap substitutions)
    {
        Snapshot ss = new(Condition.CloneWithSubstitution(substitutions), Label);
        if (TransfersTo != null)
        {
            ss.TransfersTo = TransfersTo.CloneWithSubstitution(substitutions);
        }
        foreach (Event ev in _AssociatedPremises)
        {
            if (replacements.TryGetValue(ev, out Event? replaceEv))
            {
                ss._AssociatedPremises.Add(replaceEv);
            }
            else
            {
                ss._AssociatedPremises.Add(ev);
            }
        }
        foreach ((Snapshot priorSS, Ordering priorOrdering) in _PriorSnapshots)
        {
            ss._PriorSnapshots.Add((priorSS.CloneTraceWithReplacements(replacements, substitutions), priorOrdering));
        }
        return ss;
    }

    #region Assessing relationships with other snapshots.

    public bool IsRelatable(State other) => other.Name == Condition.Name;

    public IReadOnlyList<(Snapshot S, Ordering O)> PriorSnapshots => _PriorSnapshots;

    public void SetLaterThan(Snapshot other)
    {
        _PriorSnapshots.Add((other, Ordering.LaterThan));
    }

    public void SetModifiedOnceLaterThan(Snapshot other)
    {
        _PriorSnapshots.Add((other, Ordering.ModifiedOnceAfter));
    }

    public void SetUnifedWith(Snapshot other)
    {
        _PriorSnapshots.Add((other, Ordering.Unchanged));
    }

    public bool IsAfter(Snapshot other)
    {
        foreach ((Snapshot s, Ordering o) in _PriorSnapshots)
        {
            // The comparison on reference is intentional.
            if (o != Ordering.Unchanged && other == s)
            {
                return true;
            }
        }
        return false;
    }

    public bool HasPredecessor => _PriorSnapshots.Count > 0;

    public bool AllPredecessorsContainedIn(List<Snapshot> predecessorList)
    {
        bool notFound = false;
        foreach ((Snapshot ss, Ordering _) in _PriorSnapshots)
        {
            if (!predecessorList.Contains(ss))
            {
                notFound = true;
                break;
            }
        }
        return !notFound;
    }

    public bool HasImmediatePredecessor { 
        get
        {
            foreach ((Snapshot _, Ordering o) in _PriorSnapshots)
            {
                if (o == Ordering.ModifiedOnceAfter || o == Ordering.Unchanged)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Proceed through the entire predecessor tree to check if the other snapshot is present.
    /// This method is used to check that rules are being constructed correctly.
    /// </summary>
    /// <param name="other">Other snapshot to check.</param>
    /// <returns>True if the other snapshot is a predecessor, false otherwise.</returns>
    public bool OccursSometimeAfter(Snapshot other)
    {
        foreach ((Snapshot s, Ordering _) in _PriorSnapshots)
        {
            if (s == other || s.OccursSometimeAfter(other))
            {
                return true;
            }
        }
        return false;
    }

    #endregion
    #region Filtering

    public bool ContainsMessage(IMessage msg)
    {
        if (Condition.ContainsMessage(msg))
        {
            return true;
        }
        foreach ((Snapshot s, Ordering _) in _PriorSnapshots)
        {
            if (s.ContainsMessage(msg))
            {
                return true;
            }
        }
        return false;
    }

    public bool ContainsState(State sOther)
    {
        if (Condition.Equals(sOther))
        {
            return true;
        }
        foreach ((Snapshot ss, Ordering _) in _PriorSnapshots)
        {
            if (ss.ContainsState(sOther))
            {
                return true;
            }
        }
        return false;
    }

    #endregion

    /// <summary>
    /// Changes the trace from a tree to a list, and is useful for operations which need to be
    /// applied to all conditions or premises once per invocation.
    /// </summary>
    /// <param name="allSS">List to add found items to.</param>
    public void FlattenToList(List<Snapshot> allSS)
    {
        foreach ((Snapshot ss, Ordering o) in _PriorSnapshots)
        {
            if (o != Ordering.Unchanged)
            {
                ss.FlattenToList(allSS);
            }
        }
        allSS.Add(this);
    }

    #region Premise handling.

    public IReadOnlyList<Event> AssociatedPremises => _AssociatedPremises;

    internal void AddPremises(IEnumerable<Event> events)
    {
        foreach (Event ev in events)
        {
            // Duplicated events don't make sense, so only add if not already added.
            if (!_AssociatedPremises.Contains(ev))
            {
                _AssociatedPremises.Add(ev);
            }
        }
    }

    internal void ReplacePremises(Event toBeReplaced, IEnumerable<Event> replacements)
    {
        _AssociatedPremises.Remove(toBeReplaced);
        AddPremises(replacements);
    }

    private HashSet<Event>? _EventsInTrace;

    /// <summary>
    /// Returns a set of all the Events that are featured in the premises of the trace.
    /// </summary>
    public HashSet<Event> EventsInTrace
    {
        get
        {
            if (_EventsInTrace == null)
            {
                // To minimise memory usage, we have a more complicated algorithm here that doesn't
                // just call each previous trace's EventsInTrace property.
                _EventsInTrace = new();
                Stack<Snapshot> ssToCheck = new();
                foreach (Event ev in _AssociatedPremises)
                {
                    _EventsInTrace.Add(ev);
                }
                foreach ((Snapshot pt, Ordering _) in _PriorSnapshots)
                {
                    ssToCheck.Push(pt);
                }

                while (ssToCheck.Count > 0)
                {
                    Snapshot ss = ssToCheck.Pop();
                    if (ss._EventsInTrace != null)
                    {
                        _EventsInTrace.UnionWith(ss._EventsInTrace);
                        // No need to check previous snapshots.
                    }
                    else
                    {
                        foreach (Event ev in ss._AssociatedPremises)
                        {
                            _EventsInTrace.Add(ev);
                        }
                        foreach ((Snapshot pt, Ordering _) in ss._PriorSnapshots)
                        {
                            ssToCheck.Push(pt);
                        }
                    }
                }
            }
            return _EventsInTrace;
        }
    }

    #endregion
    #region Trace implication.

    internal bool CanImplyTrace(Ordering ptOrder, Snapshot ot, Ordering otOrder, HashSet<Event> wp)
    {
        List<(Snapshot S, Ordering O)> nextPt;
        List<(Snapshot S, Ordering O)> nextOt;

        if (Condition.Equals(ot.Condition))
        {
            if (!ptOrder.AsOrMoreOrganisedThan(otOrder))
            {
                return false;
            }
            wp.UnionWith(_AssociatedPremises);
            nextPt = _PriorSnapshots;
            nextOt = ot._PriorSnapshots;
        }
        else
        {
            if (ptOrder == Ordering.Unchanged)
            {
                if (otOrder != Ordering.Unchanged)
                {
                    return false;
                }
                nextOt = new(from os in ot._PriorSnapshots where os.O == Ordering.Unchanged select os);
                if (nextOt.Count == 0)
                {
                    return false;
                }
                nextPt = new() { (this, Ordering.Unchanged) };
            }
            else if (ptOrder == Ordering.ModifiedOnceAfter)
            {
                if (otOrder == Ordering.Unchanged)
                {
                    nextOt = new(from os in ot._PriorSnapshots
                                 where os.O == Ordering.Unchanged || os.O == Ordering.ModifiedOnceAfter
                                 select os);
                    nextPt = new() { (this, Ordering.ModifiedOnceAfter) };
                }
                else if (otOrder == Ordering.ModifiedOnceAfter)
                {
                    nextOt = new(from os in ot._PriorSnapshots where os.O == Ordering.Unchanged select os);
                    nextPt = new() { (this, Ordering.Unchanged) };
                }
                else
                {
                    return false;
                }
            }
            else // ptOrder == Ordering.LaterThan
            {
                nextOt = ot._PriorSnapshots;
                nextPt = new() { (this, Ordering.LaterThan) };
            }
        }
        wp.ExceptWith(ot._AssociatedPremises);

        return CheckNextTraceLevel(nextPt, nextOt, wp);
    }

    private static bool CheckNextTraceLevel(
        List<(Snapshot S, Ordering O)> nextPt,
        List<(Snapshot S, Ordering O)> nextOt,
        HashSet<Event> wp)
    {
        if (nextPt.Count == 0)
        {
            return wp.Count == 0;
        }
        else if (nextOt.Count == 0)
        {
            return false;
        }
        foreach ((Snapshot nptS, Ordering nptO) in nextPt)
        {
            bool found = false;
            foreach ((Snapshot notS, Ordering notO) in nextOt)
            {
                // Duplicate wp as it may be modified during the method call.
                HashSet<Event> wpCopy = new(wp);
                if (nptS.CanImplyTrace(nptO, notS, notO, wpCopy))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return false;
            }
        }
        return true;
    }

    internal (bool, List<(Snapshot, Snapshot)>) CanImplyTraceWithCorrespondences(Ordering ptOrder, Snapshot ot, Ordering otOrder, HashSet<Event> wp)
    {
        List<(Snapshot S, Ordering O)> nextPt;
        List<(Snapshot S, Ordering O)> nextOt;

        List<(Snapshot, Snapshot)> correspondences = new();
        if (Condition.Equals(ot.Condition))
        {
            if (!ptOrder.AsOrMoreOrganisedThan(otOrder))
            {
                return (false, new());
            }
            correspondences.Add((this, ot));
            wp.UnionWith(_AssociatedPremises);
            nextPt = _PriorSnapshots;
            nextOt = ot._PriorSnapshots;
        }
        else
        {
            if (ptOrder == Ordering.Unchanged)
            {
                if (otOrder != Ordering.Unchanged)
                {
                    return (false, correspondences); // Correspondences will be empty.
                }
                nextOt = new(from os in ot._PriorSnapshots where os.O == Ordering.Unchanged select os);
                if (nextOt.Count == 0)
                {
                    return (false, correspondences); // Correspondences will be empty.
                }
                nextPt = new() { (this, Ordering.Unchanged) };
            }
            else if (ptOrder == Ordering.ModifiedOnceAfter)
            {
                if (otOrder == Ordering.Unchanged)
                {
                    nextOt = new(from os in ot._PriorSnapshots
                                 where os.O == Ordering.Unchanged || os.O == Ordering.ModifiedOnceAfter
                                 select os);
                    nextPt = new() { (this, Ordering.ModifiedOnceAfter) };
                }
                else if (otOrder == Ordering.ModifiedOnceAfter)
                {
                    nextOt = new(from os in ot._PriorSnapshots where os.O == Ordering.Unchanged select os);
                    nextPt = new() { (this, Ordering.Unchanged) };
                }
                else
                {
                    return (false, correspondences); // Correspondences will be empty.
                }
            }
            else // ptOrder == Ordering.LaterThan
            {
                nextOt = ot._PriorSnapshots;
                nextPt = new() { (this, Ordering.LaterThan) };
            }
        }
        wp.ExceptWith(ot._AssociatedPremises);

        (bool nextLevelGood, List<(Snapshot, Snapshot)> nextLevelCorres) = CheckNextTraceLevelWithCorrespondences(nextPt, nextOt, wp);
        if (nextLevelGood && correspondences.Count > 0)
        {
            nextLevelCorres.AddRange(correspondences);
        }
        return (nextLevelGood, nextLevelCorres);
    }

    private static (bool, List<(Snapshot, Snapshot)>) CheckNextTraceLevelWithCorrespondences(
        List<(Snapshot S, Ordering O)> nextPt,
        List<(Snapshot S, Ordering O)> nextOt,
        HashSet<Event> wp)
    {
        if (nextPt.Count == 0)
        {
            return (wp.Count == 0, new());
        }
        else if (nextOt.Count == 0)
        {
            return (false, new());
        }
        List<(Snapshot, Snapshot)> correspondences = new();
        foreach ((Snapshot nptS, Ordering nptO) in nextPt)
        {
            bool found = false;
            foreach ((Snapshot notS, Ordering notO) in nextOt)
            {
                // Duplicate wp as it may be modified during the method call.
                HashSet<Event> wpCopy = new(wp);
                (bool canImply, List<(Snapshot, Snapshot)> nextLevelCorres) = nptS.CanImplyTraceWithCorrespondences(nptO, notS, notO, wpCopy);
                if (canImply)
                {
                    found = true;
                    correspondences.AddRange(nextLevelCorres);
                    break;
                }
            }
            if (!found)
            {
                return (false, new());
            }
        }
        return (true, correspondences);
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

    public override bool Equals(object? obj)
    {
        // Two snapshots are considered equal if they represent the same state and the previous
        // states are themselves equal.
        if (obj is Snapshot ss)
        {
            if (Condition.Equals(ss.Condition) && _PriorSnapshots.Count == ss._PriorSnapshots.Count)
            {
                // Note that _PriorSnapshots lists may not be in same order.
                foreach ((Snapshot prevSS, Ordering o) in _PriorSnapshots)
                {
                    bool found = false;
                    foreach ((Snapshot otherPrevSS, Ordering otherO) in ss._PriorSnapshots)
                    {
                        if (o == otherO && prevSS.Equals(otherPrevSS))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        return false;
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
        if (Condition.Equals(other.Condition) &&
            _PriorSnapshots.Count == other._PriorSnapshots.Count && 
            _AssociatedPremises.Count == other._AssociatedPremises.Count)
        {
            // As the ordering of _PriorSnapshots and _Premises may be different, we have to use
            // an inefficient algorithm.
            foreach ((Snapshot prevSS, Ordering o) in _PriorSnapshots)
            {
                bool prevSSFound = false;
                foreach ((Snapshot otherPrevSS, Ordering otherO) in other._PriorSnapshots)
                {
                    if (o == otherO && prevSS.EqualsIncludingPremises(otherPrevSS))
                    {
                        prevSSFound = true;
                        break;
                    }
                }
                if (!prevSSFound)
                {
                    return false;
                }
            }
            foreach (Event premise in _AssociatedPremises)
            {
                bool eventFound = false;
                foreach (Event otherPremise in other._AssociatedPremises)
                {
                    if (premise.Equals(otherPremise))
                    {
                        eventFound = true;
                        break;
                    }
                }
                if (!eventFound)
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }
}
