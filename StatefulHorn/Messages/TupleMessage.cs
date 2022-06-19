using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn.Messages;

/// <summary>
/// Represents a message that is composed of a ordered number of other messages. It can be thought
/// of as a message that 
/// </summary>
public class TupleMessage : IMessage
{
    /// <summary>
    /// Create a new tuple message containing the provided members.
    /// </summary>
    /// <param name="members">The inner messages of the tuple.</param>
    public TupleMessage(List<IMessage> members)
    {
        Members = members;

        ContainsVariables = false;
        HashCode = 673;
        for (int i = 0; i < Members.Count; i++)
        {
            IMessage msg = Members[i];
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
    /// Create a new tuple message containing the members provided by the enumeration.
    /// </summary>
    /// <param name="members">The inner messages of the tuple.</param>
    public TupleMessage(IEnumerable<IMessage> members) : this(new List<IMessage>(members)) { }

    /// <summary>
    /// Inner messages of the tuple.
    /// </summary>
    public IReadOnlyList<IMessage> Members { get; init; }

    /// <summary>
    /// Split the tuple into its members. Any inner tuples of this message are recursively
    /// decomposed. For example, a tuple like (a[], (b[], c[])) will be decomposed into
    /// a[], b[] and c[].
    /// </summary>
    /// <returns>Each decomposed member in turn.</returns>
    public IEnumerable<IMessage> Decompose()
    {
        for (int i = 0; i < Members.Count; i++)
        {
            IMessage m = Members[i];
            if (m is TupleMessage tMsg)
            {
                foreach (IMessage innerMessage in tMsg.Decompose())
                {
                    yield return innerMessage;
                }
            }
            else
            {
                yield return m;
            }
        }
    }

    #region IMessage implementation.

    private int MaxDepth = -1;

    public int FindMaximumDepth()
    {
        if (MaxDepth == -1)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                MaxDepth = Math.Max(MaxDepth, Members[i].FindMaximumDepth());
            }
            MaxDepth++;
        }
        return MaxDepth;
    }

    public bool ContainsVariables { get; init; }

    public void CollectVariables(ISet<IMessage> varSet)
    {
        for (int i = 0; i < Members.Count; i++)
        {
            Members[i].CollectVariables(varSet);
        }
    }

    public void CollectMessages(ISet<IMessage> msgSet, Predicate<IMessage> selector)
    {
        if (selector(this))
        {
            msgSet.Add(this);
        }
        for (int i = 0; i < Members.Count; i++)
        {
            Members[i].CollectMessages(msgSet, selector);
        }
    }

    public bool ContainsMessage(IMessage other)
    {
        if (Equals(other))
        {
            return true;
        }
        for (int i = 0; i < Members.Count; i++)
        {
            if (Members[i].ContainsMessage(other))
            {
                return true;
            }
        }
        return false;
    }

    public bool DetermineUnifiedToSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        return other is TupleMessage tMsg && sf.CanUnifyMessagesOneWay(Members, tMsg.Members, gs);
    }

    public bool IsUnifiableWith(IMessage other) => DetermineUnifiableSubstitution(other, Guard.Empty, Guard.Empty, new());

    public bool DetermineUnifiableSubstitution(IMessage other, Guard fwdG, Guard bwdG, SigmaFactory sf)
    {
        if (other is VariableMessage)
        {
            return sf.TryAdd(this, other, true);
        }
        return other is TupleMessage tMsg && sf.CanUnifyMessagesBothWays(Members, tMsg.Members, fwdG, bwdG);
    }

    public IMessage Substitute(SigmaMap sigma)
    {
        if (!ContainsVariables || sigma.IsEmpty)
        {
            return this;
        }
        List<IMessage> subsParam = new(Members.Count);
        for (int i = 0; i < Members.Count; i++)
        {
            subsParam.Add(Members[i].Substitute(sigma));
        }
        return new TupleMessage(subsParam);
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => "<" + string.Join(", ", Members) + ">";

    public override bool Equals(object? obj)
    {
        return obj is TupleMessage tMsg 
            && Members.Count == tMsg.Members.Count 
            && Members.SequenceEqual(tMsg.Members);
    }

    private readonly int HashCode;

    public override int GetHashCode() => HashCode;

    #endregion
}
