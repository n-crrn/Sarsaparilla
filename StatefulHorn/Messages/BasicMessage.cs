using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn.Messages;

public abstract class BasicMessage : IMessage
{
    public BasicMessage(string n)
    {
        Name = n;
        HashCode = Name.GetHashCode();
    }

    public string Name { get; init; }

    public int FindMaximumDepth() => 1;

    public abstract bool ContainsVariables { get; }

    public virtual void CollectVariables(ISet<IMessage> varSet) { /* Nothing to do. */ }

    public void CollectMessages(ISet<IMessage> msgSet, Predicate<IMessage> selector)
    {
        if (selector(this))
        {
            msgSet.Add(this);
        }
    }

    public bool ContainsMessage(IMessage other)
    {
        return Equals(other);
    }

    public abstract bool DetermineUnifiedToSubstitution(IMessage other, SigmaFactory sf);

    public bool DetermineUnifiedToSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        return DetermineUnifiedToSubstitution(other, sf) && sf.ForwardIsValidByGuard(gs);
    }

    public bool IsUnifiableWith(IMessage other) => DetermineUnifiableSubstitution(other, Guard.Empty, Guard.Empty, new());

    public abstract bool DetermineUnifiableSubstitution(IMessage other, SigmaFactory sf);

    public bool DetermineUnifiableSubstitution(IMessage other, Guard fwdGuard, Guard bwdGuard, SigmaFactory sf)
    {
        return DetermineUnifiableSubstitution(other, sf) && sf.ForwardIsValidByGuard(fwdGuard) && sf.BackwardIsValidByGuard(bwdGuard);
    }

    public abstract IMessage Substitute(SigmaMap sigma);

    public override abstract string ToString();

    public override bool Equals(object? obj) => obj != null && GetType() == obj.GetType() && ((BasicMessage)obj).Name == Name;

    private readonly int HashCode;

    public override int GetHashCode() => HashCode;
}
