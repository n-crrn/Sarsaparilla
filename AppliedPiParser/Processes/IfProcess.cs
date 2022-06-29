using System;
using System.Collections.Generic;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Processes;

public class IfProcess : IProcess
{
    public IfProcess(
        IComparison comp, 
        IProcess guardedProc, 
        IProcess? elseProc, 
        RowColumnPosition? definedAt)
    {
        Comparison = comp;
        GuardedProcess = guardedProc;
        ElseProcess = elseProc;
        DefinedAt = definedAt;
    }

    public IComparison Comparison { get; init; }

    public IProcess GuardedProcess { get; init; }

    public IProcess? ElseProcess { get; init; }

    #region IProcess Implementation.

    public IProcess SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        IComparison newComparison = Comparison.SubstituteTerms(subs);
        IProcess newGProc = GuardedProcess.SubstituteTerms(subs);
        IProcess? newEProc = ElseProcess?.SubstituteTerms(subs);
        return new IfProcess(newComparison, newGProc, newEProc, DefinedAt);
    }

    public IEnumerable<string> VariablesDefined()
    {
        IEnumerable<string> vd = GuardedProcess.VariablesDefined();
        return ElseProcess == null ? vd : vd.Concat(ElseProcess.VariablesDefined());
    }

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher)
    {
        List<IProcess> found = new();

        if (matcher(GuardedProcess))
        {
            found.Add(GuardedProcess);
        }
        else
        {
            found.AddRange(GuardedProcess.MatchingSubProcesses(matcher));
        }

        if (ElseProcess != null)
        {
            if (matcher(ElseProcess))
            {
                found.Add(ElseProcess);
            }
            else
            {
                found.AddRange(ElseProcess.MatchingSubProcesses(matcher));
            }
        }

        return found;
    }

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        if (Comparison.ResolveType(termResolver) != PiType.Bool)
        {
            errorMessage = $"Please check your types for comparison '{Comparison}'";
            return false;
        }
        if (!GuardedProcess.Check(nw, termResolver, out errorMessage))
        {
            return false;
        }
        if (ElseProcess != null && !ElseProcess.Check(nw, termResolver, out errorMessage))
        {
            return false;
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        PiType? cmpType = Comparison.ResolveType(resolver);
        if (cmpType != PiType.Bool)
        {
            throw new ResolverException(Comparison, cmpType);
        }
        return new IfProcess(
            Comparison, 
            GuardedProcess.Resolve(nw, resolver), 
            ElseProcess?.Resolve(nw, resolver), 
            DefinedAt);
    }

    public RowColumnPosition? DefinedAt { get; init; }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        if (obj is IfProcess ip)
        {
            return Comparison.Equals(ip.Comparison) &&
                   GuardedProcess.Equals(ip.GuardedProcess) &&
                   ((ElseProcess == null && ip.ElseProcess == null) ||
                    (ElseProcess != null && ElseProcess.Equals(ip.ElseProcess)));
        }
        return false;
    }

    public override int GetHashCode() => Comparison.GetHashCode();

    public static bool operator ==(IfProcess p1, IfProcess p2) => Equals(p1, p2);

    public static bool operator !=(IfProcess p1, IfProcess p2) => !Equals(p1, p2);

    public override string ToString()
    {
        string guardedDesc = $"if {Comparison} then {GuardedProcess};";
        return ElseProcess == null ? guardedDesc : guardedDesc + $" else {ElseProcess}";
    }

    #endregion
}
