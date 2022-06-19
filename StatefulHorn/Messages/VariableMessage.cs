using System.Collections.Generic;
using System.Diagnostics;

namespace StatefulHorn.Messages;

/// <summary>
/// Represents a message that can be replaced within a message.
/// </summary>
public class VariableMessage : BasicMessage, IAssignableMessage
{
    /// <summary>
    /// Creates a new VariableMessage with the given name.
    /// </summary>
    /// <param name="n">Name of the variable.</param>
    public VariableMessage(string n) : base(n) { }

    #region BasicMessage/IMessage implementation.

    public override bool ContainsVariables => true;

    public override void CollectVariables(ISet<IMessage> varSet)
    {
        _ = varSet.Add(this);
    }

    public override bool DetermineUnifiedToSubstitution(IMessage other, SigmaFactory sf)
    {
        // A variable can unify with everything, including other variables.
        return sf.TryAdd(this, other, false);
    }

    public override bool DetermineUnifiableSubstitution(IMessage other, SigmaFactory sf)
    {
        // A variable can unify with everything, including other variables. Note that we do not
        // try the reverse combination, as it would result in a situation where, in
        // unifying two rules, we would just swap the variables rather than converge on a single
        // rule.
        return sf.TryAdd(this, other, true);
    }

    public override IMessage Substitute(SigmaMap sigma)
    {
        if (sigma.TryGetValue(this, out IMessage? val))
        {
            Debug.Assert(val != null);
            return val;
        }
        return this;
    }

    #endregion

    public override string ToString() => Name;
}
