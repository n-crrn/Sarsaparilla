using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Destructor
{
    public Destructor(Term lhs, string rhs, SortedList<string, string> paramTypes)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
        ParameterTypes = paramTypes;
    }

    public Term LeftHandSide { get; init; }

    public string RightHandSide { get; init; }

    public SortedList<string, string> ParameterTypes { get; init; }

    public override string ToString() => $"reduc to {RightHandSide}";

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Destructor d &&
            LeftHandSide.Equals(d.LeftHandSide) &&
            RightHandSide.Equals(d.RightHandSide) &&
            ParameterTypes.SequenceEqual(d.ParameterTypes);
    }

    public override int GetHashCode() => LeftHandSide.GetHashCode();
}
