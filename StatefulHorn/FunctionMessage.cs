﻿using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

public class FunctionMessage : IMessage
{
    public FunctionMessage(string n, List<IMessage> parameters)
    {
        Name = n;
        _Parameters = parameters;

        ContainsVariables = false;
        foreach (IMessage msg in _Parameters)
        {
            if (msg.ContainsVariables)
            {
                ContainsVariables = true;
                break;
            }
        }
    }

    public string Name { get; init; }

    private readonly List<IMessage> _Parameters;
    public IReadOnlyList<IMessage> Parameters => _Parameters;

    public bool ContainsVariables { get; init; }

    public void CollectVariables(HashSet<IMessage> varSet)
    {
        foreach (IMessage msg in _Parameters)
        {
            msg.CollectVariables(varSet);
        }
    }

    public bool ContainsMessage(IMessage other)
    {
        if (Equals(other))
        {
            return true;
        }
        foreach (IMessage msg in _Parameters)
        {
            if (msg.ContainsMessage(other))
            {
                return true;
            }
        }
        return false;
    }

    public bool DetermineUnifiedToSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        return other is FunctionMessage fMsg &&
            Name.Equals(fMsg.Name) &&
            sf.CanUnifyMessagesOneWay(_Parameters, fMsg._Parameters, gs);
    }

    public bool IsUnifiableWith(IMessage other) => DetermineUnifiableSubstitution(other, new(), new());

    public bool DetermineUnifiableSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        if (other is VariableMessage)
        {
            return sf.TryAdd(this, other);
        }
        return other is FunctionMessage fMsg &&
            Name.Equals(fMsg.Name) &&
            sf.CanUnifyMessagesBothWays(_Parameters, fMsg._Parameters, gs);
    }

    public IMessage PerformSubstitution(SigmaMap sigma)
    {
        if (!ContainsVariables)
        {
            return this;
        }
        return new FunctionMessage(Name, new(from p in _Parameters select p.PerformSubstitution(sigma)));
    }

    #region Basic object overrides.

    public override string ToString()
    {
        string enclosed = "";
        if (Parameters.Count > 0)
        {
            enclosed = "(" + string.Join(", ", Parameters) + ")";
        }
        return Name + enclosed;
    }

    public override bool Equals(object? obj)
    {
        return obj is FunctionMessage fMsg && Name.Equals(fMsg.Name) && _Parameters.SequenceEqual(fMsg._Parameters);
    }

    public override int GetHashCode() => Name.GetHashCode();

    #endregion
}