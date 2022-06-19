using System;
using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// A symbol representing data that can be generated or known, leading to other such symbols.
/// </summary>
public interface IMessage
{

    // Though ToString() is a basic object method, this redefinition forces the return type to be
    // non-null.
    public string ToString();

    /// <summary>
    /// Find the maximum nesting of this message. A basic, nonce or variable will have a depth of
    /// one, with every nesting of functions and tuples adding one.
    /// </summary>
    /// <returns>Maximum nesting of method.</returns>
    public int FindMaximumDepth();

    /// <summary>
    /// Indicates whether the value of a message will change if a substitution is performed.
    /// This property helps with caching values.
    /// </summary>
    public bool ContainsVariables { get; }

    /// <summary>
    /// Adds all variable messages found within this message to the given set.
    /// </summary>
    /// <param name="varSet">Set to add found variables to.</param>
    public void CollectVariables(ISet<IMessage> varSet);

    /// <summary>
    /// Add all sub-messages (including this one) that match the given predicate selector.
    /// </summary>
    /// <param name="msgSet">Set to add found messages to.</param>
    /// <param name="selector">
    /// Predicate that returns true when a message is to be added to msgSet.
    /// </param>
    public void CollectMessages(ISet<IMessage> msgSet, Predicate<IMessage> selector);

    /// <summary>
    /// Whether other is contained within, or matches, this message.
    /// </summary>
    /// <param name="other">Message to match with.</param>
    /// <returns>True if a message matching other is found.</returns>
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
    /// Returns true if there exists a common substitution that allows both this value and other to
    /// become equal.
    /// </summary>
    /// <param name="other">Message to attempt unification with.</param>
    /// <returns>True if both messages unifiable.</returns>
    public bool IsUnifiableWith(IMessage other);

    /// <summary>
    /// Calculates the σ mapping between two messages such that this =_σ other (if there is a
    /// valid one to be found).
    /// </summary>
    /// <param name="other">The other message to attempt to unify with.</param>
    /// <param name="fwdGuard">Guard for substituting from this message to other.</param>
    /// <param name="bwdGuard">Guard for substituting from other to this message.</param>
    /// <param name="sf">SigmaFactory to be populated with viable mappings.</param>
    /// <returns>
    ///   False if this message cannot be unified without contradicting existing substitutions in sf.
    ///   True otherwise, including if no substitution is required.
    /// </returns>
    public bool DetermineUnifiableSubstitution(IMessage other, Guard fwdGuard, Guard bwdGuard, SigmaFactory sf);

    /// <summary>
    /// Substitutes occurances of messages. Each member of the sigma parameter is a substitution
    /// of one message by another.
    /// </summary>
    /// <param name="sigma">Substitutions to apply.</param>
    /// <returns>
    /// A message with the substitutions made. If there is no change, then the same message may be
    /// returned. Otherwise a completely new message is returned.
    /// </returns>
    public IMessage Substitute(SigmaMap sigma);

}
