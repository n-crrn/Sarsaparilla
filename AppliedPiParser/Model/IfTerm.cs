using System;
using System.Collections.Generic;

namespace AppliedPi.Model;

/// <summary>
/// Applied Pi Code has a ... wonderful ... feature whereby an if statement can be included
/// where a term statement would be. This overloading of "if" results in some differences
/// if an IfProcess that requires the creation of a completely different representation.
/// </summary>
public class IfTerm : ITermGenerator
{
    public IfTerm(IComparison comp, ITermGenerator trueVal, ITermGenerator falseVal)
    {
        Comparison = comp;
        TrueTermValue = trueVal;
        FalseTermValue = falseVal;
    }

    public IComparison Comparison { get; init; }

    public ITermGenerator TrueTermValue { get; init; }

    public ITermGenerator FalseTermValue { get; init; }

    #region ITermGenerator implementation.

    public Term ResolveTerm(IReadOnlyDictionary<string, string> varSubstitutions)
    {
        // FIXME: This is to be implemented when the IComparison interface is updated with the
        // logic for evaluating comparisons.
        throw new NotImplementedException();
    }

    public SortedSet<string> BasicSubTerms
    {
        get
        {
            SortedSet<string> variables = new();
            variables.UnionWith(Comparison.Variables);
            variables.UnionWith(TrueTermValue.BasicSubTerms);
            variables.UnionWith(FalseTermValue.BasicSubTerms);
            return variables;
        }
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is IfTerm it && Comparison.Equals(it.Comparison) && TrueTermValue.Equals(it.TrueTermValue) && FalseTermValue.Equals(it.FalseTermValue);
    }

    public override int GetHashCode() => Comparison.GetHashCode();

    public override string ToString()
    {
        return $"if {Comparison} then {TrueTermValue} else {FalseTermValue}";
    }

    #endregion

}
