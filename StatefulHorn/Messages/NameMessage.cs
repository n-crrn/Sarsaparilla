namespace StatefulHorn.Messages;

/// <summary>
/// A message representing a basic constant symbol of the knowledge system.
/// </summary>
public class NameMessage : BasicMessage
{
    /// <summary>
    /// A resolved named message used as a placeholder for any valid NameMessage.
    /// </summary>
    public static readonly NameMessage Any = new("Any");

    /// <summary>
    /// Create a new NameMessage with the given text name.
    /// </summary>
    /// <param name="n">Name for the message.</param>
    public NameMessage(string n) : base(n) { }

    public override bool ContainsVariables => false;

    public override bool DetermineUnifiedToSubstitution(IMessage other, SigmaFactory sf)
    {
        return other is NameMessage nmOther && nmOther.Name.Equals(Name);
    }

    public override bool DetermineUnifiableSubstitution(IMessage other, SigmaFactory sf)
    {
        if (other is NameMessage nmOther)
        {
            return Name == nmOther.Name;
        }
        return other is VariableMessage && sf.TryAdd(this, other, true);
    }

    public override IMessage Substitute(SigmaMap sigma)
    {
        return this;
    }

    public override string ToString() => Name + "[]";
}
