using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

public class StateTransformationSet : ISigmaUnifiable
{

    public StateTransformationSet(Snapshot after, State condition) :
        this(new List<(Snapshot After, State Condition)>() { (after, condition) })
    { }

    public StateTransformationSet(IEnumerable<(Snapshot After, State Condition)> l)
    {
        _Transformations = new(l);
        _Variables = new();
        foreach ((Snapshot _, State c) in _Transformations)
        {
            _Variables.UnionWith(c.Variables);
        }
    }

    private readonly List<(Snapshot After, State Condition)> _Transformations;

    public IReadOnlyList<(Snapshot After, State Condition)> Transformations => _Transformations;

    public bool IsEmpty => _Transformations.Count == 0;

    #region Filtering.

    public bool ContainsMessage(IMessage msg)
    {
        foreach ((Snapshot _, State cond) in _Transformations)
        {
            if (cond.ContainsMessage(msg))
            {
                return true;
            }
        }
        return false;
    }

    public bool ContainsState(State st)
    {
        foreach ((Snapshot _, State cond) in _Transformations)
        {
            if (cond.Equals(st))
            {
                return true;
            }
        }
        return false;
    }

    #endregion
    #region ISigmaUnifiable implementation.

    public bool ContainsVariables => _Variables.Count > 0;

    private readonly HashSet<IMessage> _Variables;

    public IReadOnlySet<IMessage> Variables => _Variables;

    private List<IMessage> ToMsgList() => new(from c in _Transformations select c.Condition.Value);

    public bool CanBeUnifiedTo(ISigmaUnifiable other, Guard g, SigmaFactory sf)
    {
        return other is StateTransformationSet sts 
            && sf.CanUnifyMessagesOneWay(ToMsgList(), sts.ToMsgList(), g);
    }

    public bool CanBeUnifiableWith(ISigmaUnifiable other, Guard fwdGuard, Guard bwdGuard, SigmaFactory sf)
    {
        return other is StateTransformationSet sts 
            && sf.CanUnifyMessagesBothWays(ToMsgList(), sts.ToMsgList(), fwdGuard, bwdGuard);
    }

    public override string ToString()
    {
        return string.Join(", ", from t in _Transformations select $"<{t.After.Label}: {t.Condition}>");
    }

    #endregion
    #region Basic object overrides (except ToString()).

    public override bool Equals(object? obj)
    {
        if (obj is StateTransformationSet sts)
        {
            if (_Transformations.Count == sts._Transformations.Count && _Variables.SetEquals(sts._Variables))
            {
                // Pragmatically, only the states being changed to can be directly compared.
                // Snapshot comparison across rules is problematic, and so ignored for this equality test.
                // If snapshots are important, then the respective SnapshotTrees should be compared instead,
                // as those tree also include the state transformation information.
                foreach ((Snapshot _, State s) in _Transformations)
                {
                    if (!sts.HasState(s))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        return false;
    }

    private bool HasState(State stateToFind)
    {
        foreach ((Snapshot _, State s) in _Transformations)
        {
            if (stateToFind.Equals(s))
            {
                return true;
            }
        }
        return false;
    }

    public override int GetHashCode() => _Transformations.Count == 0 ? 0 : _Transformations[0].Condition.GetHashCode();

    #endregion
}
