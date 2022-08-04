using System;
using System.Collections.Generic;

namespace StatefulHorn.Messages;

/// <summary>
/// A base class for messages that are composed of a name, such as NameMessage, NonceMessage and
/// VariableMessage. It provides implementations for IMessage methods that are common for such
/// simple message types.
/// </summary>
public abstract class BasicMessage : IMessage
{
    /// <summary>
    /// Create a simple message. This should only be called from a child class constructor.
    /// </summary>
    /// <param name="n">Name of the new message.</param>
    protected BasicMessage(string n)
    {
        Name = n;
        HashCode = Name.GetHashCode();
    }

    /// <summary>
    /// Identifier for the message.
    /// </summary>
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

    /// <summary>
    /// Determine if this message can be unified to other, without guard considerations.
    /// </summary>
    /// <param name="other">Other message.</param>
    /// <param name="sf">SigmaFactory to use for testing.</param>
    /// <returns>True if this message can be unified to other.</returns>
    public abstract bool DetermineUnifiedToSubstitution(IMessage other, SigmaFactory sf);

    public bool DetermineUnifiedToSubstitution(IMessage other, Guard gs, SigmaFactory sf)
    {
        return DetermineUnifiedToSubstitution(other, sf) && sf.ForwardIsValidByGuard(gs);
    }

    public bool IsUnifiableWith(IMessage other)
    {
        return DetermineUnifiableSubstitution(other, Guard.Empty, Guard.Empty, new());
    }

    /// <summary>
    /// Determine if this message is unifiable with other, without guard considerations.
    /// </summary>
    /// <param name="other">Other message.</param>
    /// <param name="sf">SigmaFactory to use for testing.</param>
    /// <returns>True if this message and other are unifiable with a substitution.</returns>
    public abstract bool DetermineUnifiableSubstitution(IMessage other, SigmaFactory sf);

    public bool DetermineUnifiableSubstitution(
        IMessage other, 
        Guard fwdGuard, 
        Guard bwdGuard, 
        SigmaFactory sf)
    {
        return DetermineUnifiableSubstitution(other, sf) 
            && sf.ForwardIsValidByGuard(fwdGuard) 
            && sf.BackwardIsValidByGuard(bwdGuard);
    }

    public abstract IMessage Substitute(SigmaMap sigma);

    public int CompareTo(IMessage? other) => MessageUtils.Compare(this, other);

    public override abstract string ToString();

    public override bool Equals(object? obj)
    {
        return obj != null 
            && GetType() == obj.GetType() 
            && ((BasicMessage)obj).Name == Name;
    }

    /// <summary>
    /// Cache of the hash value for the message.
    /// </summary>
    private readonly int HashCode;

    public override int GetHashCode() => HashCode;
}
