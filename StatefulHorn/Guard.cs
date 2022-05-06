using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

// FIXME: A revision of this class is required to ensure that unuifiability and 
// inequivalence are being handled correctly.
public class Guard
{
    public static readonly Guard Empty = new();

    public Guard()
    {
        _Ununified = new();
        _Ununifiable = new();
    }

    private Guard(HashSet<(IMessage, IMessage)> ununifiedComb, HashSet<(IMessage, IMessage)> ununifiableComb)
    {
        _Ununified = ununifiedComb;
        _Ununifiable = ununifiableComb;
    }

    public static Guard CreateFromSets(HashSet<(IMessage, IMessage)> ununifiedInput, HashSet<(IMessage, IMessage)> ununifiableInput)
    {
        HashSet<(IMessage, IMessage)> ununifiedComb = new();
        HashSet<(IMessage, IMessage)> ununifiableComb = new();

        foreach ((IMessage, IMessage) row in ununifiedInput)
        {
            if (HasSwitched(ununifiedInput, row) || ununifiableInput.Contains(row) || HasSwitched(ununifiableInput, row))
            {
                ununifiableComb.Add(row);
            }
            else
            {
                ununifiedComb.Add(row);
            }
        }

        foreach ((IMessage, IMessage) row in ununifiableInput)
        {
            if (!HasSwitched(ununifiableComb, row))
            {
                ununifiableComb.Add(row);
            }
        }

        return new Guard(ununifiedComb, ununifiableComb);
    }

    private static bool HasSwitched(HashSet<(IMessage, IMessage)> fullSet, (IMessage, IMessage) row)
    {
        (IMessage, IMessage) switched = (row.Item2, row.Item1);
        return fullSet.Contains(switched);
    }

    private readonly HashSet<(IMessage, IMessage)> _Ununified;

    public IReadOnlySet<(IMessage, IMessage)> Ununified => _Ununified;

    private readonly HashSet<(IMessage, IMessage)> _Ununifiable;

    public IReadOnlySet<(IMessage, IMessage)> Ununifiable => _Ununifiable;

    public bool CanUnifyMessages(IMessage msg1, IMessage msg2)
    {
        return !(_Ununified.Contains((msg1, msg2)) ||
                 _Ununifiable.Contains((msg1, msg2)) ||
                 _Ununifiable.Contains((msg2, msg1)));
    }

    public Guard UnionWith(Guard other)
    {
        HashSet<(IMessage, IMessage)> ununifiedComb = new(_Ununified);
        ununifiedComb.UnionWith(other._Ununified);
        HashSet<(IMessage, IMessage)> ununifiableComb = new(_Ununifiable);
        ununifiableComb.UnionWith(other._Ununifiable);
        return new(ununifiedComb, ununifiableComb);
    }

    public Guard PerformSubstitution(SigmaMap sigma)
    {
        if (_Ununified.Count == 0 && _Ununifiable.Count == 0)
        {
            return this;
        }
        return new(Substitute(_Ununified, sigma), Substitute(_Ununifiable, sigma));
    }

    private static HashSet<(IMessage, IMessage)> Substitute(HashSet<(IMessage, IMessage)> inputSet, SigmaMap sigma)
    {
        // FIXME: If a substitute is provided for a variable, and the substitution does not
        // contradict the guard, then the variable can be removed from the guard completely.
        return new(from item in inputSet select (item.Item1.PerformSubstitution(sigma), item.Item2.PerformSubstitution(sigma)));
    }

    public bool IsEmpty => _Ununified.Count == 0 && _Ununifiable.Count == 0;

    #region Basic object overrides.

    public override string ToString()
    {
        // Not that non-ununifiables are the only items output as part of the string.
        List<string> nonunif = new();
        foreach((IMessage msg1, IMessage msg2) in _Ununified)
        {
            nonunif.Add($"{msg1} ~/⤳ {msg2}");
        }
        foreach ((IMessage msg1, IMessage msg2) in _Ununifiable)
        {
            nonunif.Add($"{msg1} ≠ {msg2}");
        }
        return string.Join(", ", nonunif);
    }

    public override bool Equals(object? obj)
    {
        return obj is Guard otherGuard &&
            ((IsEmpty && otherGuard.IsEmpty) ||
            ( _Ununified.SetEquals(otherGuard._Ununified) && _Ununifiable.SetEquals(otherGuard._Ununifiable)));
    }

    public override int GetHashCode()
    {
        if (_Ununified.Count == 0)
        {
            if (_Ununifiable.Count == 0)
            {
                return 0;
            }
            else
            {
                return GenerateHashCode(_Ununifiable);
            }
        }
        return GenerateHashCode(_Ununified);
    }

    private static int GenerateHashCode(HashSet<(IMessage, IMessage)> hs)
    {
        int code = 0;
        foreach ((IMessage, IMessage) row in hs)
        {
            code ^= row.GetHashCode();
        }
        return code;
    }

    #endregion
}
