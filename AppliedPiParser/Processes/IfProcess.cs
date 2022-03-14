using System.Collections.Generic;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Processes;

public class IfProcess : IProcess
{
    public IfProcess(IComparison comp, IProcess guardedProc, IProcess? elseProc)
    {
        Comparison = comp;
        GuardedProcess = guardedProc;
        ElseProcess = elseProc;
    }

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

    public IComparison Comparison { get; init; }

    public IProcess GuardedProcess { get; init; }

    public IProcess? ElseProcess { get; init; }

    #region IProcess Implementation.

    private IProcess? _Next;
    public IProcess? Next
    {
        get => _Next;
        set
        {
            _Next = value;
            if (value != null)
            {
                FillNextOnProcess(GuardedProcess, value);
                if (ElseProcess != null)
                {
                    FillNextOnProcess(ElseProcess, value);
                }
            }
        }
    }

    private static void FillNextOnProcess(IProcess p, IProcess newNext)
    {
        while (p.Next != null)
        {
            p = p.Next;
        }
        p.Next = newNext;
    }

    public IEnumerable<string> Terms()
    {
        IEnumerable<string> terms = Comparison.Variables.Concat(GuardedProcess.Terms());
        if (ElseProcess != null)
        {
            terms = terms.Concat(ElseProcess.Terms());
        }
        return terms;
    }

    public IProcess ResolveTerms(SortedList<string, string> subs)
    {
        IComparison newComparison = Comparison.ResolveTerms(subs);
        IProcess newGProc = GuardedProcess.ResolveTerms(subs);
        IProcess? newEProc = ElseProcess?.ResolveTerms(subs);
        return new IfProcess(newComparison, newGProc, newEProc);
    }

    #endregion
}
