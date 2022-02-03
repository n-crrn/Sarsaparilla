using System.Collections.Generic;

namespace StatefulHorn;

public abstract class BasicMessage : IMessage
{
    public BasicMessage(string n)
    {
        Name = n;
        HashCode = Name.GetHashCode();
    }

    public string Name { get; init; }

    public abstract bool ContainsVariables { get; }

    public virtual void CollectVariables(HashSet<IMessage> varSet) { /* Nothing to do. */ }

    public bool ContainsMessage(IMessage other)
    {
        return Equals(other);
    }

    public abstract bool DetermineUnifiedToSubstitution(IMessage other, SigmaFactory sf);

    public bool DetermineUnifiedToSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        return gs.CanUnifyMessages(this, other) && DetermineUnifiedToSubstitution(other, sf);
    }

    public bool IsUnifiableWith(IMessage other) => DetermineUnifiableSubstitution(other, new(), new());

    public abstract bool DetermineUnifiableSubstitution(IMessage other, SigmaFactory sf);

    public bool DetermineUnifiableSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        return gs.CanUnifyMessages(this, other) && DetermineUnifiableSubstitution(other, sf);
    }

    public abstract IMessage PerformSubstitution(SigmaMap sigma);

    public override abstract string ToString();

    public override bool Equals(object? obj) => obj != null && GetType() == obj.GetType() && ((BasicMessage)obj).Name == Name;

    private readonly int HashCode;

    public override int GetHashCode() => HashCode;
}
