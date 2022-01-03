using System.Collections.Generic;

namespace StatefulHorn;

public interface IMessage
{
    public string ToString();

    /// <summary>
    /// Indicates whether the value of a message will change if a substitution is performed.
    /// This property helps with caching values.
    /// </summary>
    public bool ContainsVariables { get; }

    public void CollectVariables(HashSet<IMessage> varSet);

    public bool ContainsMessage(IMessage other);

    /// <summary>
    /// Calculates the σ mapping from this message to other such that this ↝_σ other. That is,
    /// the substitution σ will result in other.
    /// <param name="other">The target message.</param>
    /// <param name="guardStatements">Guard statements for indicating ununifiable messages.</param>
    /// <param name="sf">SigmaFactory to be populated with viable mappings.</param>
    /// <returns>
    ///   False if this message cannot be unified without contradicting existing substitutions in sf.
    ///   True otherwise, including if no substitution is required.
    /// </returns>
    public bool DetermineUnifiedToSubstitution(IMessage other, Guard guardStatements, SigmaFactory sf);

    /// <summary>
    /// Returns true if there exists a substitution that allows both this value and other to 
    /// become equal.
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool IsUnifiableWith(IMessage other);

    /// <summary>
    /// Calculates the σ mapping between two messages such that this =_σ other (if there is a
    /// valid one to be found).
    /// </summary>
    /// <param name="other">The other message to attempt to unify with.</param>
    /// <param name="guardStatements">Guard statements for indicating ununifiable messages.</param>
    /// <param name="sf">SigmaFactory to be populated with viable mappings.</param>
    /// <returns>
    ///   False if this message cannot be unified without contradicting existing substitutions in sf.
    ///   True otherwise, including if no substitution is required.
    /// </returns>
    public bool DetermineUnifiableSubstitution(IMessage other, Guard guardStatements, SigmaFactory sf);

    /// <summary>
    /// Substitutes occurances of messages. Each member of the sigma parameter is a substitution
    /// of one message by another.
    /// </summary>
    /// <param name="msg"></param>
    /// <returns></returns>
    public IMessage PerformSubstitution(SigmaMap sigma);
}
