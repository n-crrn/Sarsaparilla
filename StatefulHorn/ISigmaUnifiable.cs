using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// Provides an interface to elements that can be unified with one another based on a set of 
/// substitutions. This interface allows common algorithms across these types.
/// </summary>
public interface ISigmaUnifiable
{
    public bool ContainsVariables { get; }

    public IReadOnlySet<IMessage> Variables { get; }

    public bool CanBeUnifiedTo(ISigmaUnifiable other, Guard guardTest, SigmaFactory substitutions);

    public bool CanBeUnifiableWith(ISigmaUnifiable other, Guard guardTest, SigmaFactory substitutions);

    public string ToString();
}
