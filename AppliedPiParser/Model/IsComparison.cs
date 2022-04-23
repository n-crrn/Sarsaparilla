using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppliedPi.Model;

/// <summary>
/// Provides a straight comparison with the value of a boolean. This comparison tends to only be
/// used when a single boolean name is provided for an entire comparison block.
/// </summary>
public class IsComparison : IComparison
{

    public IsComparison(string boolName)
    {
        BooleanName = boolName;
    }

    public string BooleanName { get; init; }

    #region IComparison implementation.

    public SortedSet<string> Variables => new() { BooleanName };

    public IComparison ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new IsComparison(subs.GetValueOrDefault(BooleanName, BooleanName));
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj) => obj is IsComparison ic && BooleanName.Equals(ic.BooleanName);

    public override int GetHashCode() => BooleanName.GetHashCode();

    public override string ToString() => BooleanName;

    #endregion
}
