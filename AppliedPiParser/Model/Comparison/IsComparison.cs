﻿using System;
using System.Collections.Generic;

namespace AppliedPi.Model.Comparison;

/// <summary>
/// Provides a straight comparison with the value of a boolean. This comparison tends to only be
/// used when a single boolean name is provided for an entire comparison block.
/// </summary>
public class IsComparison : IComparison
{

    public IsComparison(string name)
    {
        Name = name;
        AsTerm = Term.From(name);
    }

    private IsComparison(Term t)
    {
        Name = t.ToString();
        AsTerm = t;
    }

    public string Name { get; init; }

    public Term AsTerm { get; init; }

    #region IComparison implementation.

    public SortedSet<string> Variables => new() { Name };

    public IComparison SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new IsComparison(AsTerm.ResolveTerm(subs));
    }

    public PiType? ResolveType(TermResolver resolver)
    {
        return resolver.Resolve(AsTerm, out TermOriginRecord? tr) ? tr!.Type : null;
    }

    public IComparison Positivise(bool invert = false)
    {
        if (invert)
        {
            // The assumption is that term "Name[]" is a boolean.
            return new NotComparison(this);
        }
        return this;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj) => obj is IsComparison ic && Name.Equals(ic.Name);

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => Name;

    #endregion

}
