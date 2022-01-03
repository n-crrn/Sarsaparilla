using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Query
{

    public Query(Term lhs, Term rhs, SortedList<string, string> paramTypes)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
        ParameterTypes = paramTypes;
    }

    public Term LeftHandSide;

    public Term RightHandSide;

    public SortedList<string, string> ParameterTypes;

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Query q &&
            LeftHandSide.Equals(q.LeftHandSide) &&
            RightHandSide.Equals(q.RightHandSide) &&
            ParameterTypes.SequenceEqual(q.ParameterTypes);
    }

    public override int GetHashCode() => (LeftHandSide, RightHandSide).GetHashCode();

    public override string ToString()
    {
        string typesStr = string.Join(", ", from nt in ParameterTypes select $"{nt.Key}: {nt.Value}");
        return $"query {typesStr}; {LeftHandSide} = {RightHandSide}";
    }

}
