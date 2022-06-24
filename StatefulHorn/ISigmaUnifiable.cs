using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// Provides an interface to elements that can be unified with one another based on a set of 
/// substitutions. This interface allows common algorithms across these types.
/// </summary>
public interface ISigmaUnifiable
{

    /// <summary>
    /// Indicates that the element contains variable elements. This method is used to detect
    /// cases where optimisations arising from constant comparisons may be possible.
    /// </summary>
    public bool ContainsVariables { get; }

    /// <summary>
    /// Return the set of variable messages contained within this element.
    /// </summary>
    public IReadOnlySet<IMessage> Variables { get; }

    /// <summary>
    /// Determine if this message can be unified to the element other while complying
    /// with the given guard statement. If so, the required substitutions are stored in 
    /// substitutions.
    /// </summary>
    /// <param name="other">Other meessage to be unified to.</param>
    /// <param name="guardTest">Guard restrictions to comply with.</param>
    /// <param name="substitutions">
    /// Storage for the replacements required to unify to other. Substitutions may 
    /// already contain unrelated replacements prior to calling this method.
    /// </param>
    /// <returns>True if this element can be unified to other.</returns>
    public bool CanBeUnifiedTo(ISigmaUnifiable other, Guard guardTest, SigmaFactory substitutions);

    /// <summary>
    /// Determine if this message and other are unifiable to a common element.
    /// </summary>
    /// <param name="other">Other message to be unifiable with.</param>
    /// <param name="fwdGuard">
    /// Guard that substitutions within this element must comply with.
    /// </param>
    /// <param name="bwdGuard">Guard that substitutions within other must comply with.</param>
    /// <param name="substitutions">
    /// Storage for the replacements required for this message and other to be unifiable. 
    /// Substitutions may already contain unrelated replacements prior to calling this 
    /// method.
    /// </param>
    /// <returns>True if this element and other are unifiable.</returns>
    public bool CanBeUnifiableWith(
        ISigmaUnifiable other, 
        Guard fwdGuard, 
        Guard bwdGuard, 
        SigmaFactory substitutions);

    // Used to ensure that ToString() is guaranteed to return a string rather than string?.
    public string ToString();
}
