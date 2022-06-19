using System;
using System.Collections.Generic;
using System.Linq;

using StatefulHorn.Messages;

namespace StatefulHorn;

public class State : ISigmaUnifiable, IComparable<State>
{
    public State(string name, IMessage val)
    {
        Name = name;
        Value = val;
    }

    public State CloneWithSubstitution(SigmaMap substitutions)
    {
        return new State(Name, Value.Substitute(substitutions));
    }

    public string Name { get; init; }

    public IMessage Value { get; init; }

    public bool ContainsMessage(IMessage other) => Value.ContainsMessage(other);

    public bool IsUnifiableWith(State other)
    {
        return Name == other.Name && Value.IsUnifiableWith(other.Value);
    }

    #region ISigmaUnifiable implementation.

    public bool ContainsVariables => Value.ContainsVariables;

    private HashSet<IMessage>? _Variables;

    public IReadOnlySet<IMessage> Variables
    {
        get
        {
            if (_Variables == null)
            {
                _Variables = new();
                Value.CollectVariables(_Variables);
            }
            return _Variables;
        }
    }

    public bool CanBeUnifiedTo(ISigmaUnifiable other, Guard g, SigmaFactory subs)
    {
        return other is State s &&
            Name.Equals(s.Name) &&
            Value.DetermineUnifiedToSubstitution(s.Value, g, subs) &&
            subs.ForwardIsValidByGuard(g);
    }

    public bool CanBeUnifiableWith(ISigmaUnifiable other, Guard fwdGuard, Guard bwdGuard, SigmaFactory subs)
    {
        return other is State s &&
            Name.Equals(s.Name) &&
            Value.DetermineUnifiableSubstitution(s.Value, fwdGuard, bwdGuard, subs);
    }

    public override string ToString() => $"{Name}({Value})";

    #endregion
    #region Basic object overrides (except ToString())

    public override bool Equals(object? obj) => obj is State s && Name.Equals(s.Name) && Value.Equals(s.Value);

    public override int GetHashCode() => Name.GetHashCode();

    #endregion
    #region IComparable implementation.

    public int CompareTo(State? other) => other == null ? 1 : Name.CompareTo(other.Name);

    #endregion
}
