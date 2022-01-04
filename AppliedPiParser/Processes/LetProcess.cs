﻿using AppliedPi.Model;

namespace AppliedPi.Processes;

/// <summary>
/// This is effectively an ordering clause, where a variable is temmporarily set for the
/// benefit of the encapsulated process.
/// </summary>
public class LetProcess : IProcess
{
    public LetProcess(TuplePattern lhs, ITermGenerator rhs)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
    }

    public TuplePattern LeftHandSide { get; init; }

    public ITermGenerator RightHandSide { get; init; }

    public IProcess? Next { get; set; }

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is LetProcess lp && LeftHandSide.Equals(lp.LeftHandSide) && RightHandSide.Equals(lp.RightHandSide);
    }

    public override int GetHashCode() => LeftHandSide.GetHashCode();

    public static bool operator ==(LetProcess lp1, LetProcess lp2) => Equals(lp1, lp2);

    public static bool operator !=(LetProcess lp1, LetProcess lp2) => !Equals(lp1, lp2);

    public override string ToString() => $"let {LeftHandSide} = {RightHandSide} in";

    #endregion
}