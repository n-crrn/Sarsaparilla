using System.Collections.Generic;

namespace AppliedPi.Model.Comparison;

public class NotComparison : IComparison
{

    public NotComparison(IComparison inner)
    {
        InnerComparison = inner;
    }

    public NotComparison(string boolName) :
        this(new IsComparison(boolName))
    { }

    public IComparison InnerComparison { get; init; }

    #region IComparison implementation.

    public SortedSet<string> Variables => InnerComparison.Variables;

    public IComparison SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new NotComparison((BooleanComparison)InnerComparison.SubstituteTerms(subs));
    }

    public PiType? ResolveType(TermResolver tr)
    {
        return InnerComparison.ResolveType(tr) == PiType.Bool ? PiType.Bool : null;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is NotComparison nc && InnerComparison.Equals(nc.InnerComparison);
    }

    public override int GetHashCode() => ~InnerComparison.GetHashCode();

    public override string ToString() => $"not({InnerComparison})";

    #endregion

}
