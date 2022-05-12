using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn.Messages;

public class TupleMessage : IMessage
{
    public TupleMessage(List<IMessage> members)
    {
        _Members = members;

        ContainsVariables = false;
        HashCode = members.Count;
        foreach (IMessage msg in members)
        {
            if (msg.ContainsVariables)
            {
                ContainsVariables = true;
            }
            HashCode ^= msg.GetHashCode();
        }
    }

    public TupleMessage(IEnumerable<IMessage> members) : this(new List<IMessage>(members)) { }

    // FIXME: Reassess the requirement for this method.
    public TupleMessage Append(IMessage msgToAdd)
    {
        List<IMessage> memberList = new(_Members)
        {
            msgToAdd
        };
        return new(memberList);
    }

    private readonly List<IMessage> _Members;
    public IReadOnlyList<IMessage> Members => _Members;

    public IEnumerable<IMessage> Decompose()
    {
        foreach (IMessage m in _Members)
        {
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

    public int FindMaximumDepth() => (from m in _Members select m.FindMaximumDepth()).Max();

    public bool ContainsVariables { get; init; }

    public void CollectVariables(HashSet<IMessage> varSet)
    {
        foreach (IMessage msg in _Members)
        {
            msg.CollectVariables(varSet);
        }
    }

    public void CollectMessages(HashSet<IMessage> msgSet, Predicate<IMessage> selector)
    {
        if (selector(this))
        {
            msgSet.Add(this);
        }
        foreach (IMessage msg in _Members)
        {
            msg.CollectMessages(msgSet, selector);
        }
    }

    public bool ContainsMessage(IMessage other)
    {
        if (Equals(other))
        {
            return true;
        }
        foreach (IMessage msg in _Members)
        {
            if (msg.ContainsMessage(other))
            {
                return true;
            }
        }
        return false;
    }

    public bool ContainsFunctionNamed(string name) => (from m in _Members where m.ContainsFunctionNamed(name) select m).Any();

    public bool DetermineUnifiedToSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        return other is TupleMessage tMsg && sf.CanUnifyMessagesOneWay(_Members, tMsg._Members, gs);
    }

    public bool IsUnifiableWith(IMessage other) => DetermineUnifiableSubstitution(other, new(), new());

    public bool DetermineUnifiableSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        if (other is VariableMessage)
        {
            return sf.TryAdd(this, other);
        }
        return other is TupleMessage tMsg && sf.CanUnifyMessagesBothWays(_Members, tMsg._Members, gs);
    }

    public IMessage PerformSubstitution(SigmaMap sigma)
    {
        if (!ContainsVariables || sigma.IsEmpty)
        {
            return this;
        }
        return new TupleMessage(new(from m in _Members select m.PerformSubstitution(sigma)));
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => "<" + string.Join(", ", _Members) + ">";

    public override bool Equals(object? obj)
    {
        return obj is TupleMessage tMsg && _Members.Count == tMsg.Members.Count && _Members.SequenceEqual(tMsg._Members);
    }

    private readonly int HashCode;

    public override int GetHashCode() => HashCode; //_Members[0].GetHashCode();

    #endregion
}
