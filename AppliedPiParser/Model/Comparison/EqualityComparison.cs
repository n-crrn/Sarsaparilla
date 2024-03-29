﻿using System.Collections.Generic;

namespace AppliedPi.Model.Comparison;

/// <summary>
/// Fundamentally, AppliedPi works through comparing names. Names are either equal to one
/// another, or not-equal.
/// </summary>
public class EqualityComparison : IComparison
{
    public EqualityComparison(bool isEq, string v1, string v2) :
        this(isEq, new IsComparison(v1), new IsComparison(v2))
    { }

    public EqualityComparison(bool isEq, IComparison lhs, IComparison rhs)
    {
        IsEquals = isEq;
        LeftComparison = lhs;
        RightComparison = rhs;
    }

    public bool IsEquals { get; init; }

    public IComparison LeftComparison { get; init; }

    public IComparison RightComparison { get; init; }

    #region IComparison implementation.

    public SortedSet<string> Variables
    {
        get
        {
            SortedSet<string> vs = LeftComparison.Variables;
            vs.UnionWith(RightComparison.Variables);
            return vs;
        }
    }

    public IComparison SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new EqualityComparison(IsEquals, LeftComparison.SubstituteTerms(subs), RightComparison.SubstituteTerms(subs));
    }

    public PiType? ResolveType(TermResolver tr)
    {
        PiType? lhsType = LeftComparison.ResolveType(tr);
        PiType? rhsType = RightComparison.ResolveType(tr);
        return lhsType == rhsType ? PiType.Bool : null;
    }

    public IComparison Positivise(bool invert = false)
    {
        if (invert)
        {
            return new EqualityComparison(!IsEquals, LeftComparison, RightComparison);
        }
        return this;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is EqualityComparison nc &&
            IsEquals == nc.IsEquals &&
            LeftComparison.Equals(nc.LeftComparison) &&
            RightComparison.Equals(nc.RightComparison);
    }

    public override int GetHashCode() => (5099 * 5101 + LeftComparison.GetHashCode()) * 5101 + RightComparison.GetHashCode();

    public override string ToString()
    {
        return IsEquals ? $"{LeftComparison} = {RightComparison}" : $"{LeftComparison} <> {RightComparison}";
    }

    #endregion
}
