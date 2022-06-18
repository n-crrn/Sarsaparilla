using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        SnapshotTree newTree = new();
        newTree._Traces.AddRange(from t in _Traces select t.PerformSubstitutions(substitutions));
        return newTree;
    }

    private List<Snapshot> CloneTraces() => new(from t in _Traces select t.CloneTrace());

    public SnapshotTree MergeWith(SnapshotTree otherTree)
    {
        SnapshotTree newTree = new();
        newTree._Traces.AddRange(CloneTraces());
        newTree._Traces.AddRange(otherTree.CloneTraces());

        // NOTE: State unification is a separate operation. In order to comply with
        // Li et al 2017 as closely as possible, states are not unified here.
        return newTree;
    }

    #endregion

    private readonly List<Snapshot> _Traces;

    public IReadOnlyList<Snapshot> Traces => _Traces;

    public bool IsEmpty => _Traces.Count == 0;

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
        List<Snapshot> foundSS = new();
        foreach (Snapshot t in _Traces)
        {
            t.CollectSnapshotsAssociatedWith(ev, foundSS);
        }
        return foundSS;
    }

    public int MaxTraceLength
    {
        get
        {
            int length = 0;
            for (int i = 0; i < _Traces.Count; i++)
            {
                int traceLength = 1;
                Snapshot ss = _Traces[i];
                while (ss.Prior != null)
                {
                    traceLength++;
                    ss = ss.Prior.S;
                }
                length = Math.Max(length, traceLength);
            }
            return length;
        }
    }

    #endregion
    #region Snapshot Tree Operations

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
            if (ss.Prior != null)
            {
                string op = ss.Prior.O.OperatorString();
                relationships.Add($"{ss.Prior.S.Label} {op} {ss.Label}");
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
