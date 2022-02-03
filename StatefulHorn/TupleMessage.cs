using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

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

    private readonly List<IMessage> _Members;
    public IReadOnlyList<IMessage> Members => _Members;

    public bool ContainsVariables { get; init; }

    public void CollectVariables(HashSet<IMessage> varSet)
    {
        foreach(IMessage msg in _Members)
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
        foreach (IMessage msg in _Members)
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
        if (!ContainsVariables)
        {
            return this;
        }
        return new TupleMessage(new(from m in _Members select m.PerformSubstitution(sigma)));
    }

    public override string ToString() => "<" + string.Join(", ", _Members) + ">";

    public override bool Equals(object? obj)
    {
        return obj is TupleMessage tMsg && _Members.Count == tMsg.Members.Count && _Members.SequenceEqual(tMsg._Members);
    }

    private readonly int HashCode;

    public override int GetHashCode() => HashCode; //_Members[0].GetHashCode();

}
