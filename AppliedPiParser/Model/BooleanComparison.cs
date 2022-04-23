﻿using System;
using System.Collections.Generic;

namespace AppliedPi.Model;

/// <summary>
/// Once a name comparison is conducted, it is usually ANDed or ORed with other name
/// comparisons to form the final comparison.
/// </summary>
public class BooleanComparison : IComparison
{
    public enum Type
    {
        Or,
        And
    }

    public BooleanComparison(Type op, string in1, string in2) :
        this(op, new IsComparison(in1), new IsComparison(in2))
    { }

    public BooleanComparison(Type op, IComparison in1, IComparison in2)
    {
        Operator = op;
        LeftInput = in1;
        RightInput = in2;
    }

    public Type Operator { get; init; }

    public IComparison LeftInput { get; init; }

    public IComparison RightInput { get; init; }

    public static bool IsValidTypeString(string token)
    {
        return token == "&&" || token == "||";
    }

    public static BooleanComparison.Type TypeFromString(string token)
    {
        if (token == "&&")
        {
            return Type.And;
        }
        else if (token == "||")
        {
            return Type.Or;
        }
        throw new ArgumentException($"Token not recognised, should be '&&' or '||', not '{token}'.");
    }

    #region IComparison implementation.

    public SortedSet<string> Variables
    {
        get
        {
            SortedSet<string> inVars = new();
            inVars.UnionWith(LeftInput.Variables);
            inVars.UnionWith(RightInput.Variables);
            return inVars;
        }
    }

    public IComparison ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new BooleanComparison(Operator, LeftInput.ResolveTerms(subs), RightInput.ResolveTerms(subs));
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is BooleanComparison bc && Operator == bc.Operator && LeftInput.Equals(bc.LeftInput) && RightInput.Equals(bc.RightInput);
    }

    public override int GetHashCode() => LeftInput.GetHashCode();

    public override string ToString()
    {
        return LeftInput.ToString() + " " + Operator.ToString() + " " + RightInput.ToString();
    }

    #endregion
}
