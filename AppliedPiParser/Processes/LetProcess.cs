﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Processes;

/// <summary>
/// This is effectively an ordering clause, where a variable is temmporarily set for the
/// benefit of the encapsulated process.
/// </summary>
public class LetProcess : IProcess
{
    public LetProcess(
        TuplePattern lhs, 
        ITermGenerator rhs, 
        IProcess guardedProcess, 
        IProcess? elseProcess,
        RowColumnPosition? definedAt)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
        GuardedProcess = guardedProcess;
        ElseProcess = elseProcess;
        DefinedAt = definedAt;
    }

    public TuplePattern LeftHandSide { get; init; }

    public ITermGenerator RightHandSide { get; init; }

    public IProcess GuardedProcess { get; init; }

    public IProcess? ElseProcess { get; init; }

    #region IProcess implementation.

    public IProcess SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new LetProcess(
            LeftHandSide.ResolveTerms(subs),
            RightHandSide.ResolveTerm(subs),
            GuardedProcess.SubstituteTerms(subs),
            ElseProcess?.SubstituteTerms(subs),
            DefinedAt);
    }

    public IEnumerable<string> VariablesDefined()
    {
        IEnumerable<string> en = LeftHandSide.AssignedVariables.Concat(GuardedProcess.VariablesDefined());
        return ElseProcess == null ? en : en.Concat(ElseProcess.VariablesDefined());
    }

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        foreach (TuplePattern.Element e in LeftHandSide.Elements)
        {
            if (e.IsMatcher)
            {
                // Cannot check matching types properly - let allows for some exotic
                // occurrences. But we can ensure it exists.
                if (!termResolver.Resolve(e.Term, out TermOriginRecord? _))
                {
                    errorMessage = $"Term {e.Term} does not exist.";
                    return false;
                }
            }
            else
            {
                // Newly defined type. ProVerif allows type inferrence, we don't because
                // we're a little bit more basic here.
                if (e.Type == null)
                {
                    errorMessage = $"Type for term {e.Type} must be given.";
                    return false;
                }
                if (!termResolver.Register(e.Term, new(TermSource.Let, new(e.Type))))
                {
                    errorMessage = $"Term {e.Term} already exists.";
                    return false;
                }
            }
        }
        foreach (string rhsSubTermStr in RightHandSide.BasicSubTerms)
        {
            if (!termResolver.Resolve(new(rhsSubTermStr), out TermOriginRecord? _))
            {
                errorMessage = $"Could not resolve term {rhsSubTermStr}.";
                return false;
            }
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        foreach (TuplePattern.Element e in LeftHandSide.Elements)
        {
            if (e.IsMatcher)
            {
                resolver.ResolveOrThrow(e.Term);
            }
            else
            {
                Debug.Assert(e.Type != null);
                resolver.Register(e.Term, new(TermSource.Let, new(e.Type)));
            }
        }
        return new LetProcess(
            LeftHandSide, 
            RightHandSide,
            GuardedProcess.Resolve(nw, resolver),
            ElseProcess?.Resolve(nw, resolver),
            DefinedAt);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

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
