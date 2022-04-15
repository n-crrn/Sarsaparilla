﻿using System;
using System.Collections.Generic;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Processes;

/// <summary>
/// This is effectively an ordering clause, where a variable is temmporarily set for the
/// benefit of the encapsulated process.
/// </summary>
public class LetProcess : IProcess
{
    public LetProcess(TuplePattern lhs, ITermGenerator rhs, IProcess guardedProcess, IProcess? elseProcess = null)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
        GuardedProcess = guardedProcess;
        ElseProcess = elseProcess;
    }

    public TuplePattern LeftHandSide { get; init; }

    public ITermGenerator RightHandSide { get; init; }

    public IProcess GuardedProcess { get; init; }

    public IProcess? ElseProcess { get; init; }

    #region IProcess implementation.

    public IEnumerable<string> Terms()
    {
        foreach (TuplePattern.Element ele in LeftHandSide.Elements)
        {
            yield return ele.Name;
        }
        foreach (string t in RightHandSide.BasicSubTerms)
        {
            yield return t;
        }
    }

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new LetProcess(
            LeftHandSide.ResolveTerms(subs),
            RightHandSide.ResolveTerm(subs),
            GuardedProcess.ResolveTerms(subs),
            ElseProcess?.ResolveTerms(subs));
    }

    public IEnumerable<string> VariablesDefined() => LeftHandSide.AssignedVariables;

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is LetProcess lp &&
            LeftHandSide.Equals(lp.LeftHandSide) &&
            RightHandSide.Equals(lp.RightHandSide) &&
            GuardedProcess.Equals(lp.GuardedProcess) &&
            ((ElseProcess == null && lp.ElseProcess == null) ||
             (ElseProcess != null && ElseProcess.Equals(lp.ElseProcess)));
    }

    public override int GetHashCode() => LeftHandSide.GetHashCode();

    public static bool operator ==(LetProcess lp1, LetProcess lp2) => Equals(lp1, lp2);

    public static bool operator !=(LetProcess lp1, LetProcess lp2) => !Equals(lp1, lp2);

    public override string ToString() => $"let {LeftHandSide} = {RightHandSide} in";

    #endregion
}
