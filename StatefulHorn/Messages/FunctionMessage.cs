using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn.Messages;

/// <summary>
/// A message representing the result of an application (one-way) of a function.
/// </summary>
public class FunctionMessage : IMessage
{
    /// <summary>
    /// Create a new function message.
    /// </summary>
    /// <param name="n">Name of the function/constructor used.</param>
    /// <param name="parameters">Message parameters of the function.</param>
    public FunctionMessage(string n, List<IMessage> parameters)
    {
        Name = n;
        Parameters = parameters;

        ContainsVariables = false;
        // Prime numbers randomly selected.
        HashCode = 673 * 839 + Name.GetHashCode();
        for (int i = 0; i < Parameters.Count; i++)
        {
            IMessage msg = Parameters[i];
            if (msg.ContainsVariables)
            {
                ContainsVariables = true;
            }
            unchecked
            {
                HashCode = HashCode * 839 + msg.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Name of the function used upon the message parameters.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Parameter messages of the function used.
    /// </summary>
    public IReadOnlyList<IMessage> Parameters { get; init; }

    #region IMessage implementation.

    /// <summary>
    /// Cached value of the maximum depth of the function.
    /// </summary>
    private int MaxDepth = -1;

    public int FindMaximumDepth()
    {
        if (MaxDepth == -1)
        {
            for (int i = 0; i < Parameters.Count; i++)
            {
                MaxDepth = Math.Max(MaxDepth, Parameters[i].FindMaximumDepth());
            }
            MaxDepth++;
        }
        return MaxDepth;
    }

    public bool ContainsVariables { get; init; }

    public void CollectVariables(ISet<IMessage> varSet)
    {
        for (int i = 0; i < Parameters.Count; i++)
        {
            Parameters[i].CollectVariables(varSet);
        }
    }

    public void CollectMessages(ISet<IMessage> msgSet, Predicate<IMessage> selector)
    {
        if (selector(this))
        {
            msgSet.Add(this);
        }
        for (int i = 0; i < Parameters.Count; i++)
        {
            Parameters[i].CollectMessages(msgSet, selector);
        }
    }

    public bool ContainsMessage(IMessage other)
    {
        if (Equals(other))
        {
            return true;
        }
        for (int i = 0; i < Parameters.Count; i++)
        {
            if (Parameters[i].ContainsMessage(other))
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
            sf.CanUnifyMessagesOneWay(Parameters, fMsg.Parameters, gs);
    }

    public bool IsUnifiableWith(IMessage other) => DetermineUnifiableSubstitution(other, Guard.Empty, Guard.Empty, new());

    public bool DetermineUnifiableSubstitution(
        IMessage other, 
        Guard fwdGuard, 
        Guard bwdGuard, 
        SigmaFactory sf)
    {
        if (other is VariableMessage vOther && bwdGuard.CanUnifyMessages(vOther, this))
        {
            return sf.TryAdd(this, other, true);
        }
        return other is FunctionMessage fMsg &&
            Name.Equals(fMsg.Name) &&
            sf.CanUnifyMessagesBothWays(Parameters, fMsg.Parameters, fwdGuard, bwdGuard);
    }

    public IMessage Substitute(SigmaMap sigma)
    {
        if (!ContainsVariables || sigma.IsEmpty)
        {
            return this;
        }
        List<IMessage> subsParams = new(Parameters.Count);
        for (int i = 0; i < Parameters.Count; i++)
        {
            subsParams.Add(Parameters[i].Substitute(sigma));
        }
        return new FunctionMessage(Name, subsParams);
    }

    #endregion
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
        return obj is FunctionMessage fMsg && 
            Parameters.Count == fMsg.Parameters.Count && 
            Name.Equals(fMsg.Name) && 
            Parameters.SequenceEqual(fMsg.Parameters);
    }

    private readonly int HashCode;

    public override int GetHashCode() => HashCode; // Name.GetHashCode() ^ _Parameters[0].GetHashCode();

    #endregion
}
