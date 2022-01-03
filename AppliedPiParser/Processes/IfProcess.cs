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
            bool elseEquals = (ElseProcess == null && ip.ElseProcess == null) || (ElseProcess != null && ElseProcess.Equals(ip.ElseProcess));
            return Comparison.Equals(ip.Comparison) && GuardedProcess.Equals(ip.GuardedProcess) && elseEquals;
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
}
