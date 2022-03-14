using System.Collections.Generic;

namespace AppliedPi.Model;

/// <summary>
/// Fundamentally, AppliedPi works through comparing names. Names are either equal to one
/// another, or not-equal.
/// </summary>
public class NameComparison : IComparison
{
    public NameComparison(bool isEq, string v1, string v2)
    {
        IsEquals = isEq;
        Variable1 = v1;
        Variable2 = v2;
    }

    public bool IsEquals { get; init; }

    public string Variable1 { get; init; }

    public string Variable2 { get; init; }

    #region IComparison implementation.

    public SortedSet<string> Variables { get => new() { Variable1, Variable2 }; }

    public IComparison ResolveTerms(SortedList<string, string> subs)
    {
        string v1 = subs.GetValueOrDefault(Variable1, Variable1);
        string v2 = subs.GetValueOrDefault(Variable2, Variable2);
        return new NameComparison(IsEquals, v1, v2);
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is NameComparison nc && IsEquals == nc.IsEquals && Variable1 == nc.Variable1 && Variable2 == nc.Variable2;
    }

    public override int GetHashCode() => Variable1.GetHashCode();

    public override string ToString() => IsEquals ? $"{Variable1} == {Variable2}" : $"{Variable1} <> {Variable2}";

    #endregion
}
