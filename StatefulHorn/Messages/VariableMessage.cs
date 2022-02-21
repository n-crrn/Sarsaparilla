using System.Collections.Generic;
using System.Diagnostics;

namespace StatefulHorn.Messages;

public class VariableMessage : BasicMessage
{
    public VariableMessage(string n) : base(n) { }

    public override bool ContainsVariables => true;

    public override void CollectVariables(HashSet<IMessage> varSet)
    {
        _ = varSet.Add(this);
    }

    public override bool DetermineUnifiedToSubstitution(IMessage other, SigmaFactory sf)
    {
        // A variable can unify with everything, including other variables.
        return sf.TryAdd(this, other);
    }

    public override bool DetermineUnifiableSubstitution(IMessage other, SigmaFactory sf)
    {
        // A variable can unify with everything, including other variables. Note that we do not
        // try the reverse combination, as it would result in a situation where, in
        // unifying two rules, we would just swap the variables rather than converge on a single
        // rule.
        return sf.TryAdd(this, other);
    }

    public override IMessage PerformSubstitution(SigmaMap sigma)
    {
        if (sigma.TryGetValue(this, out IMessage? val))
        {
            Debug.Assert(val != null);
            return val;
        }
        return this;
    }

    public override string ToString() => Name;
}
