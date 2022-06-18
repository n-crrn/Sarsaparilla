namespace StatefulHorn.Messages;

public class NonceMessage : BasicMessage
{
    public NonceMessage(string n) : base(n) { }

    public override bool ContainsVariables => false;

    public override bool DetermineUnifiedToSubstitution(IMessage other, SigmaFactory sf)
    {
        return other is NonceMessage nmOther && nmOther.Name.Equals(Name);
    }

    public override bool DetermineUnifiableSubstitution(IMessage other, SigmaFactory sf)
    {
        if (other is NonceMessage nmOther)
        {
            return Name == nmOther.Name;
        }
        return other is VariableMessage vr && sf.TryAdd(this, vr, true);
    }

    public override IMessage PerformSubstitution(SigmaMap sigma)
    {
        return this;
    }

    public override string ToString() => "[" + Name + "]";
}
