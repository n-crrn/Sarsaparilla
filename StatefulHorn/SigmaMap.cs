using StatefulHorn.Messages;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// Represents the σ mapping, which can be considered a list of correspondences between
/// IMessages. A σ mapping is generated for UnifiedTo and UnificiableWith operations,
/// to allow new rules to be generated and reasoned about. Previously, the type
/// List<(IMessage Variable, IMessage Value)> was used in its place, but it became 
/// apparent that there was a need to do reasoning on the type.
/// </summary>
public class SigmaMap
{
    public static readonly SigmaMap Empty = new(Enumerable.Empty<(IMessage, IMessage)>());

    public SigmaMap(IMessage variable, IMessage val)
    {
        _Map = new() { (variable, val) };
    }

    public SigmaMap(IEnumerable<(IMessage, IMessage)> zippedSubs)
    {
        _Map = new(zippedSubs);
        _Map.Sort(EntryComparer);
    }

    private int EntryComparer((IMessage Variable, IMessage Value) e1, (IMessage Variable, IMessage Value) e2)
    {
        int cmp = e1.Variable.ToString().CompareTo(e2.Variable.ToString());
        if (cmp == 0)
        {
            cmp = e1.Value.ToString().CompareTo(e2.Variable.ToString());
        }
        return cmp;
    }

    public SigmaMap Union(SigmaMap other) => new(_Map.Concat(other._Map));

    #region Basic properties and access.

    private readonly List<(IMessage Variable, IMessage Value)> _Map;

    public IReadOnlyList<(IMessage Variable, IMessage Value)> Map => _Map;

    public bool TryGetValue(IMessage possVariable, out IMessage? value)
    {
        if (possVariable is VariableMessage)
        {
            foreach ((IMessage vr, IMessage val) in Map)
            {
                if (possVariable.Equals(vr))
                {
                    value = val;
                    return true;
                }
            }
        }
        value = null;
        return false;
    }

    public bool IsEmpty => Map.Count == 0;

    public bool IsAllVariables
    {
        get
        {
            static bool bothVars((IMessage, IMessage) pair) => pair.Item1 is VariableMessage && pair.Item2 is VariableMessage;
            return (from pair in Map select pair).All(bothVars);
        }
    } 

    #endregion

    public override string ToString()
    {
        if (Map.Count == 0)
        {
            return "{EMPTY}";
        }
        IEnumerable<string> parts = from m in Map select $"{m.Variable} ↦ {m.Value}";
        return "{" + string.Join(", ", parts) + "}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is SigmaMap sm && _Map.Count == sm._Map.Count)
        {
            for (int i = 0; i < _Map.Count; i++)
            {
#pragma warning disable IDE0042 // Deconstruct variable declaration - not sensible for this code.
                (IMessage Variable, IMessage Value) thisEntry = _Map[i];
                (IMessage Variable, IMessage Value) thatEntry = sm._Map[i];
#pragma warning restore IDE0042 // Deconstruct variable declaration
                if (!thisEntry.Variable.Equals(thatEntry.Variable) || !thisEntry.Variable.Equals(thatEntry.Variable))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    public override int GetHashCode() => _Map.Count == 0 ? 0 : _Map[0].GetHashCode();

}
