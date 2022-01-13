using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

public class SnapshotTree
{
    public SnapshotTree()
    {
        _Traces = new();
    }

    public SnapshotTree(IEnumerable<Snapshot> snapshots)
    {
        _Traces = new(snapshots);
        if (_Traces.Count == 0)
        {
            return;
        }

        // Now go through the _Traces list, and remove snapshots that exist in another
        // Snapshot's trace.
        for (int i = 0; i < _Traces.Count; i++)
        {
            Snapshot possHead = _Traces[i];
            for (int j = 0; j < _Traces.Count; j++)
            {
                if (i != j)
                {
                    if (_Traces[j].IsAfter(possHead))
                    {
                        _Traces.RemoveAt(i);
                        i--;
                        break;
                    }
                }
            }
        }

        // Finally, if we have fully redundant traces, eliminate them to save computation time
        // later.
        for (int i = 0; i < _Traces.Count; i++)
        {
            Snapshot origSS = _Traces[i];
            for (int j = i + 1; j < _Traces.Count; j++)
            {
                if (origSS.EqualsIncludingPremises(_Traces[j]))
                {
                    _Traces.RemoveAt(j);
                    j--; // Step backwards for the next loop.
                }
            }
        }

        if (_Traces.Count == 0)
        {
            throw new CyclicSnapshotTreeException("Inconsistently ordered set of snapshots passed to SnapshotTree constructor.");
        }
    }

    #region Tree creation operations (including cloning)

    public SnapshotTree CloneTree()
    {
        SnapshotTree newTree = new();
        newTree._Traces.AddRange(from t in _Traces select t.Clone());
        return newTree;
    }

    public SnapshotTree
        CloneTreeWithReplacementEvents(Dictionary<Event, Event> replacements, SigmaMap substitutions)
    {
        SnapshotTree newTree = new();
        newTree._Traces.AddRange(from t in _Traces select t.CloneTraceWithReplacements(replacements, substitutions));
        return newTree;
    }

    public SnapshotTree PerformSubstitutions(SigmaMap substitutions)
    {
        // Find all the events in the tree...
        HashSet<Event> allEvents = new();
        foreach (Snapshot trace in _Traces)
        {
            allEvents.UnionWith(trace.EventsInTrace);
        }

        // ... determine the substitutions ...
        Dictionary<Event, Event> replacements = new();
        foreach (Event ev in allEvents)
        {
            replacements[ev] = ev.PerformSubstitution(substitutions);
        }

        // ...return a new tree with substitutions.
        return CloneTreeWithReplacementEvents(replacements, substitutions);
    }

    public SnapshotTree MergeWith(SnapshotTree otherTree)
    {
        SnapshotTree newTree = new();
        newTree._Traces.AddRange(from t in _Traces select t.Clone());
        newTree._Traces.AddRange(from t in otherTree._Traces select t.Clone());
        // NOTE: State unification is a separate operation. In order to comply with
        // Li et al 2017 as closely as possible, states are not unified here.
        return newTree;
    }

    #endregion

    private readonly List<Snapshot> _Traces;

    public IReadOnlyList<Snapshot> Traces => _Traces;

    public bool IsEmpty => _Traces.Count == 0;

    public bool IsUnunified => _Traces.Count > 1;

    public void AddTrace(Snapshot ss)
    {
        // Check if it needs to be added.
        _Traces.Add(ss);
        _OrderedList = null; // Will need to be rebuilt.
    }

    #region Filtering.

    public bool ContainsMessage(IMessage msg)
    {
        foreach (Snapshot t in _Traces)
        {
            if (t.ContainsMessage(msg))
            {
                return true;
            }
        }
        return false;
    }

    public bool ContainsState(State sOther)
    {
        foreach (Snapshot t in _Traces)
        {
            if (t.ContainsState(sOther))
            {
                return true;
            }
        }
        return false;
    }

    #endregion
    #region Snapshot querying

    private List<Snapshot> FlattenedSnapshotList
    {
        get
        {
            List<Snapshot> allSSFlat = new();
            foreach (Snapshot ss in _Traces)
            {
                ss.FlattenToList(allSSFlat);
            }
            return allSSFlat;
        }
    }

    private List<Snapshot>? _OrderedList;

    public IReadOnlyList<Snapshot> OrderedList
    {
        get
        {
            if (_OrderedList != null)
            {
                return _OrderedList;
            }

            // The commented-out code is the original ordering. I am retaining the comment while I
            // trial the more basic ordering of just using a Flattened list.

            // We use a list instead of a set as the ordering of retrieval from the roots is
            // important.
            /*List<Snapshot> allSSFlat = FlattenedSnapshotList;

            List<Snapshot> ssOrdered = new();
            // Collect the snapshots without predecessors, and remove them from the flat list.
            ssOrdered.AddRange(from s in allSSFlat where !s.HasPredecessor select s);
            foreach (Snapshot s in ssOrdered)
            {
                allSSFlat.Remove(s);
            }
            // Go through the list continually adding snapshots if all of their predecessors
            // are in the list.
            while (allSSFlat.Count > 0)
            {
                for (int i = 0; i < allSSFlat.Count; i++)
                {
                    if (allSSFlat[i].AllPredecessorsContainedIn(ssOrdered))
                    {
                        ssOrdered.Add(allSSFlat[i]);
                        allSSFlat.RemoveAt(i);
                        i--;
                    }
                }
            }*/
            List<Snapshot> ssOrdered = FlattenedSnapshotList;
            // Ensure the snapshots are labelled.
            Snapshot.AutolabelOrderedSnapshots(ssOrdered);

            // Save the list to save recalculating it.
            _OrderedList = ssOrdered;
            return ssOrdered;
        }
    }

    public IEnumerable<State> States => from ol in OrderedList select ol.Condition;

    public List<Snapshot> GetSnapshotsAssociatedWith(Event ev)
    {
        Stack<Snapshot> ssStack = new();
        // Reverse order to maintain final ordering similar to that of traces.
        for (int i = _Traces.Count - 1; i >= 0; i--)
        {
            ssStack.Push(_Traces[i]);
        }

        List<Snapshot> found = new();
        while (ssStack.Count > 0)
        {
            Snapshot next = ssStack.Pop();
            if (next.AssociatedPremises.Contains(ev))
            {
                found.Add(next);
            }
            for (int i = next.PriorSnapshots.Count - 1; i >= 0; i--)
            {
                ssStack.Push(next.PriorSnapshots[i].S);
            }
        }
        return found;
    }

    #endregion
    #region Snapshot Tree Operations

    /// <summary>
    /// Determines whether the current tree could imply the given tree using the given substitutions. 
    /// This is typically part of a larger rule implication function, with the substitutions limited
    /// by the results of the rule. The full definition and reasoning behind this method is given in
    /// the thesis, and replaces the operation of (((M * O) · σ ⊆ (M' * O')) ∧ (δ(O) · σ ⊆ δ(O'))) 
    /// in Definition 4 of Li et al 2017.
    /// </summary>
    public bool CanImply(SnapshotTree other, SigmaMap substitutions)
    {
        SnapshotTree p = substitutions.IsEmpty ? this : PerformSubstitutions(substitutions);

        foreach (Snapshot pt in p._Traces)
        {
            bool found = false;
            foreach (Snapshot ot in other._Traces)
            {
                (bool canImply, var _) =
                    pt.CanImplyTrace(Snapshot.Ordering.LaterThan, ot, Snapshot.Ordering.LaterThan, new());
                if (canImply)
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

    public SnapshotTree? TryActivateTransfersUpon(SnapshotTree toActivate, SigmaMap sigma)
    {
        SnapshotTree alignedTree = PerformSubstitutions(sigma);
        SnapshotTree other = toActivate.CloneTree();

        List<(Snapshot, Snapshot)> correspondences = new();
        // Check that the tree being used to activate the other implies the other. As part of this
        // algorithm, the snapshot correspondences will be collected, which will allow us to add
        // the new activations as required.
        foreach (Snapshot at in alignedTree._Traces)
        {
            bool found = false;
            foreach (Snapshot ot in other._Traces)
            {
                Snapshot.Ordering lt = Snapshot.Ordering.LaterThan;
                (bool canImply, List<(Snapshot, Snapshot)> foundCorres) = at.CanImplyTrace(lt, ot, lt, new());
                if (canImply)
                {
                    found = true;
                    correspondences.AddRange(foundCorres);
                    break;
                }
            }
            if (!found)
            {
                return null;
            }
        }

        foreach ((Snapshot catalyst, Snapshot ot) in correspondences)
        {
            if (catalyst.TransfersTo != null)
            {
                Snapshot newTrace = ActivateTransferSnapshot(catalyst.TransfersTo, ot);
                other._Traces.Remove(ot);
                other._Traces.Add(newTrace);
            }
        }
        return other;
    }

    private static Snapshot ActivateTransferSnapshot(State condition, Snapshot prev)
    {
        Snapshot ss = new(condition);
        ss.SetModifiedOnceLaterThan(prev);
        return ss;
    }

    #endregion

    public StateTransformationSet ExtractStateTransformations()
    {
        List<(Snapshot After, State Condition)> found = new();
        foreach (Snapshot ss in FlattenedSnapshotList)
        {
            if (ss.TransfersTo != null)
            {
                found.Add((ss, ss.TransfersTo));
            }
        }
        return new(found);
    }

    #region Basic object overrides.

    public override string ToString()
    {
        IReadOnlyList<Snapshot> inOrder = OrderedList;
        List<string> ssDesc = new(from s in inOrder select s.ToString());

        List<string> relationships = new();
        foreach (Snapshot ss in inOrder)
        {
            foreach ((Snapshot prevSS, Snapshot.Ordering o) in ss.PriorSnapshots)
            {
                string op = o.OperatorString();
                relationships.Add($"{prevSS.Label} {op} {ss.Label}");
            }
        }

        return string.Join(", ", ssDesc) + " : {" + string.Join(", ", relationships) + "}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is SnapshotTree otherTree && otherTree._Traces.Count == _Traces.Count)
        {
            foreach (Snapshot t in _Traces)
            {
                bool found = false;
                foreach (Snapshot u in otherTree._Traces)
                {
                    if (t.Equals(u))
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
        return false;
    }

    public override int GetHashCode() => _Traces.Count == 0 ? 0 : _Traces[0].GetHashCode();

    #endregion
}

public class CyclicSnapshotTreeException : Exception
{
    public CyclicSnapshotTreeException(string msg) : base(msg) { }
}
