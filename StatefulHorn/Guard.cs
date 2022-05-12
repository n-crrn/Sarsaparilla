using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

public class Guard
{
    public static readonly Guard Empty = new();

    public Guard()
    {
        _Ununified = new();
    }

    public Guard(IAssignableMessage aMsg, IMessage vMsg)
    {
        _Ununified = new() { { aMsg, new() { vMsg } } };
    }

    private Guard(Dictionary<IAssignableMessage, HashSet<IMessage>> ununifiedComb)
    {
        _Ununified = ununifiedComb;
    }

    public Guard(IReadOnlyDictionary<IAssignableMessage, HashSet<IMessage>> ununifedCombOrig)
    {
        _Ununified = new(ununifedCombOrig);
    }

    public static Guard CreateFromSets(HashSet<(IAssignableMessage, IMessage)> ununifiedInput)
    {
        Dictionary<IAssignableMessage, HashSet<IMessage>> ununifiedComb = new();

        foreach ((IAssignableMessage, IMessage) row in ununifiedInput)
        {
            if(ununifiedComb.TryGetValue(row.Item1, out HashSet<IMessage>? values))
            {
                values.Add(row.Item2);
            }
            else
            {
                ununifiedComb[row.Item1] = new() { row.Item2 };
            }
        }

        return new(ununifiedComb);
    }

    private readonly Dictionary<IAssignableMessage, HashSet<IMessage>> _Ununified;

    public IReadOnlyDictionary<IAssignableMessage, HashSet<IMessage>> Ununified => _Ununified;

    public bool CanUnifyMessages(IAssignableMessage msg1, IMessage msg2)
    {
        if (_Ununified.TryGetValue(msg1, out HashSet<IMessage>? bannedList))
        {
            return !bannedList.Contains(msg2);
        }
        return true;
    }

    public Guard UnionWith(Guard other)
    {
        Dictionary<IAssignableMessage, HashSet<IMessage>> comb = new();
        ImportUnunifiedList(comb, _Ununified);
        ImportUnunifiedList(comb, other._Ununified);
        return new(comb);
    }

    private static void ImportUnunifiedList(
        Dictionary<IAssignableMessage, HashSet<IMessage>> newDict,
        Dictionary<IAssignableMessage, HashSet<IMessage>> oldDict)
    {
        foreach ((IAssignableMessage vMsg, HashSet<IMessage>? oldSet) in oldDict)
        {
            if (newDict.TryGetValue(vMsg, out HashSet<IMessage>? newSet))
            {
                newSet!.UnionWith(oldSet);
            }
            else
            {
                newDict[vMsg] = new(oldSet);
            }
        }
    }

    public Guard PerformSubstitution(SigmaMap sigma)
    {
        return _Ununified.Count == 0 ? this : new(Substitute(_Ununified, sigma));
    }

    private static Dictionary<IAssignableMessage, HashSet<IMessage>> Substitute(
        Dictionary<IAssignableMessage, HashSet<IMessage>> input, 
        SigmaMap sigma)
    {
        Dictionary<IAssignableMessage, HashSet<IMessage>> updated = new();

        foreach ((IAssignableMessage vMsg, HashSet<IMessage> set) in input)
        {
            if (!sigma.TryGetValue(vMsg, out IMessage? _)) // Skip the value if it is now defined.
            {
                updated[vMsg] = new HashSet<IMessage>(from s in set select s.PerformSubstitution(sigma));
            }
        }

        return updated;
    }

    public bool IsEmpty => _Ununified.Count == 0;

    #region Basic object overrides.

    public override string ToString()
    {
        if (_Ununified.Count == 0)
        {
            return "<EMPTY>";
        }

        List<string> nonunif = new();
        foreach ((IAssignableMessage vMsg, HashSet<IMessage> set) in _Ununified)
        {
            foreach (IMessage msg in set)
            {
                nonunif.Add($"{vMsg} ~/⤳ {msg}");
            }
        }
        return string.Join(", ", nonunif);
    }

    public override bool Equals(object? obj)
    {
        if (obj is Guard og)
        {
            if (_Ununified.Count != og._Ununified.Count)
            {
                return false;
            }
            foreach ((IAssignableMessage thisVMsg, HashSet<IMessage> thisSet) in _Ununified)
            {
                if (!og._Ununified.TryGetValue(thisVMsg, out HashSet<IMessage>? otherSet))
                {
                    return false;
                }
                if (!thisSet!.SetEquals(otherSet))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    private int _HashCode = 0;

    public override int GetHashCode()
    {
        if (_Ununified.Count == 0)
        {
            return 0;
        }
        if (_HashCode == 0)
        {
            _HashCode = 7727 * 7741;
            foreach ((IAssignableMessage vMsg, HashSet<IMessage> set) in _Ununified)
            {
                unchecked
                {
                    _HashCode = _HashCode * 7741 + vMsg.GetHashCode();
                    foreach (IMessage m in set)
                    {
                        _HashCode = _HashCode * 7741 + m.GetHashCode();
                    }
                }
            }
        }
        return _HashCode;
    }


    #endregion
}
