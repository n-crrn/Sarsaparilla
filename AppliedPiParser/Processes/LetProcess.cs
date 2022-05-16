using System;
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

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        foreach (TuplePattern.Element e in LeftHandSide.Elements)
        {
            if (e.IsMatcher)
            {
                // Cannot check matching types properly - let allows for some exotic
                // occurrences. But we can ensure it exists.
                if (!termResolver.Resolve(new(e.Name), out TermRecord? _))
                {
                    errorMessage = $"Term {e.Name} does not exist.";
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
                if (!termResolver.Register(new(e.Name), new(TermSource.Let, new(e.Type))))
                {
                    errorMessage = $"Term {e.Name} already exists.";
                    return false;
                }
            }
        }
        foreach (string rhsSubTermStr in RightHandSide.BasicSubTerms)
        {
            if (!termResolver.Resolve(new(rhsSubTermStr), out TermRecord? _))
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
                resolver.ResolveOrThrow(new(e.Name));
            }
            else
            {
                Debug.Assert(e.Type != null);
                resolver.Register(new(e.Name), new(TermSource.Let, new(e.Type)));
            }
        }
        return new LetProcess(LeftHandSide, RightHandSide, GuardedProcess.Resolve(nw, resolver), ElseProcess?.Resolve(nw, resolver));
    }

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
