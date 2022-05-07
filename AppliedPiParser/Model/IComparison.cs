using System.Collections.Generic;

namespace AppliedPi.Model;

public interface IComparison
{
    /// <summary>
    /// The set of variables that this comparison relies upon to function.
    /// </summary>
    public SortedSet<string> Variables { get; }

    public IComparison SubstituteTerms(IReadOnlyDictionary<string, string> subs);

    public PiType? ResolveType(TermResolver resolver);

    /// <summary>
    /// This method propagates through a tree of comparisons to remove all "not" operations.
    /// </summary>
    /// <param name="invert">
    /// Whether the logic needs to be inverted from this point forward.
    /// </param>
    /// <returns></returns>
    public IComparison Positivise(bool invert = false);

    // Though every object has a ToString method, explicitly specifying it here simplifies
    // the null linting in ComparisonParser.Node.
    public string ToString();
}
