using System.Collections.Generic;

namespace AppliedPi.Model;

/// <summary>
/// When given an environment (i.e. a list of variables and their values), an ITermGenerator
/// can return Terms resolved to that environment with the appropriate substitutions made.
/// This interface is required to allow term-returning if statements to transparently work.
/// </summary>
public interface ITermGenerator
{
    /// <summary>
    /// Resolve the term based on the given variable substitions.
    /// </summary>
    /// <param name="varSubstitutions">List of variable substitions to be evaluated.</param>
    /// <returns>A term with the given variable substitutions made.</returns>
    public Term ResolveTerm(SortedList<string, string> varSubstitutions);

    /// <summary>
    /// Provides the list of variables or names that can be substituted.
    /// </summary>
    public SortedSet<string> Variables { get; }
}
