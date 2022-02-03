﻿using System;
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
        newTree._Traces.AddRange(CloneTraces());
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

    private List<Snapshot> CloneTraces()
    {
        Dictionary<string, Snapshot> ssByRef = new();

        // Create the new structures.
        foreach (Snapshot ss in OrderedList)
        {
            ssByRef[ss.Label!] = new(ss);
        }

        // Stitch the new structures together.
        foreach (Snapshot ss in OrderedList)
        {
            Snapshot newSS = ssByRef[ss.Label!];
            foreach ((Snapshot prevItem, Snapshot.Ordering ord) in ss._PriorSnapshots)
            {
                newSS._PriorSnapshots.Add((ssByRef[prevItem.Label!], ord));
            }
        }

        return new(from t in _Traces select ssByRef[t.Label!]);
    }

    public SnapshotTree MergeWith(SnapshotTree otherTree)
    {
        SnapshotTree newTree = new();
        newTree._Traces.AddRange(CloneTraces());
        newTree._Traces.AddRange(otherTree.CloneTraces());

        // NOTE: State unification is a separate operation. In order to comply with
        // Li et al 2017 as closely as possible, states are not unified here.
        return newTree;
    }

    /// <summary>
    /// Attempts to fold states together to remove unnecessary variables.
    /// </summary>
    /// <param name="g">Guard statements.</param>
    /// <returns>
    /// If compression is possible, a new tree and the corresponding sigma transformations
    /// that were required to make the compression possible. Otherwise two null values are
    /// returned.
    /// </returns>
    public (SnapshotTree?, SigmaFactory?) TryCompress(Guard g)
    {
        List<Snapshot> flat = new(OrderedList);
        bool compressFound = false;
        SigmaFactory sf = new();
        for (int i = 0; i < flat.Count; i++)
        {
            Snapshot current = flat[i];
            Snapshot? compressed = flat[i].TryCompress(g, sf);
            if (compressed != null)
            {
                // For the compress operation to succeed, there must be a single predecessor.
                (Snapshot prevSS, Snapshot.Ordering _) = current.PriorSnapshots[0];
                flat[i] = compressed; // Remove the old and replace with the new.
                flat.Remove(prevSS);
                compressFound = true;
                i--; // flat.Remove would remove an element before i.
            }
        }
        return compressFound ? (new(flat), sf) : (null, null);
    }

    #endregion

    private readonly List<Snapshot> _Traces;

    public IReadOnlyList<Snapshot> Traces => _Traces;

    public bool IsEmpty => _Traces.Count == 0;

    public bool IsUnified
    {
        get
        {
            if (_Traces.Count <= 1)
            {
                return true;
            }
            for (int i = 0; i < _Traces.Count - 1; i++)
            {
                if (!Snapshot.AreUnified(_Traces[i], _Traces[i + 1]))
                {
                    return false;
                }
            }
            return true;
        }
    }

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

    internal bool HasLoop()
    {
        List<Snapshot> flattened = FlattenedSnapshotList;
        for (int i = 0; i < flattened.Count; i++)
        {
            flattened[i].Tag = i;
        }
        foreach (Snapshot startSS in flattened)
        {
            bool[] found = new bool[flattened.Count];
            Stack<Snapshot> toProcess = new();
            toProcess.Push(startSS);
            while (toProcess.Count > 0)
            {
                Snapshot ss = toProcess.Pop();
                if (found[ss.Tag])
                {
                    // Has been encountered before.
                    return true;
                }
                found[ss.Tag] = true;
                foreach ((Snapshot prevSS, Snapshot.Ordering _) in ss._PriorSnapshots)
                {
                    toProcess.Push(prevSS);
                }
            }
        }
        return false;
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
                if(pt.CanImplyTrace(Snapshot.Ordering.LaterThan, ot, Snapshot.Ordering.LaterThan, new()))
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
                (bool canImply, List<(Snapshot, Snapshot)> foundCorres) = at.CanImplyTraceWithCorrespondences(lt, ot, lt, new());
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

    internal void ActivateTransfers()
    {
        for (int i = 0; i < _Traces.Count; i++)
        {
            if (_Traces[i].TransfersTo != null)
            {
                Snapshot newSS = new(_Traces[i].TransfersTo!);
                _Traces[i].TransfersTo = null;
                newSS.SetModifiedOnceLaterThan(_Traces[i]);
                _Traces[i] = newSS;
            }
        }
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
