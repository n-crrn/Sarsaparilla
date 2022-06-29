using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Processes;

/// <summary>
/// Represents a process where the sub-processes are operating concurrently.
/// </summary>
public class ParallelCompositionProcess : IProcess
{
    public ParallelCompositionProcess(IProcess proc, RowColumnPosition? definedAt)
    {
        Processes = new() { proc };
        DefinedAt = definedAt;
    }

    public ParallelCompositionProcess(IEnumerable<IProcess> newProcesses, RowColumnPosition? definedAt)
    {
        Processes = new(newProcesses);
        if (Processes.Count == 0)
        {
            throw new ArgumentException("Parallel composition process must have at least once process.");
        }
        DefinedAt = definedAt;
    }

    public List<IProcess> Processes { get; init; }

    public void Add(IProcess proc)
    {
        Processes.Add(proc);
    }

    #region IProcess implementation.

    public IProcess SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new ParallelCompositionProcess(from p in Processes select p.SubstituteTerms(subs), DefinedAt);
    }

    public IEnumerable<string> VariablesDefined()
    {
        IEnumerable<string> vars = Enumerable.Empty<string>();
        foreach (IProcess p in Processes)
        {
            vars = vars.Concat(p.VariablesDefined());
        }
        return vars;
    }

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher)
    {
        List<IProcess> found = new();
        foreach (IProcess p in Processes)
        {
            if (matcher(p))
            {
                found.Add(p);
            }
            else
            {
                found.AddRange(p.MatchingSubProcesses(matcher));
            }
        }
        return found;
    }

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        foreach (IProcess p in Processes)
        {
            if (!p.Check(nw, termResolver, out errorMessage))
            {
                return false;
            }
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        return new ParallelCompositionProcess(from p in Processes select p.Resolve(nw, resolver), DefinedAt);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        if (obj is ParallelCompositionProcess pcp)
        {
            // With our comparison, we cannot simply use SequenceEqual as two equivalent 
            // parallel process compositions may have their processes in a different
            // order in memory. There is no global ordering between processes.
            if (Processes.Count != pcp.Processes.Count)
            {
                return false;
            }
            // A whole extra list is created as there may be instances where multiple
            // matching processes are included.
            List<IProcess> unaccounted = new(pcp.Processes);
            foreach (IProcess thisProcess in Processes)
            {
                if (!unaccounted.Remove(thisProcess))
                {
                    return false;
                }
            }
            // If all items are accounted for, indicate true.
            if (unaccounted.Count == 0)
            {
                return true;
            }
        }
        return false;
    }

    public override int GetHashCode() => Processes.GetHashCode();

    public static bool operator ==(ParallelCompositionProcess? p1, ParallelCompositionProcess? p2) => Equals(p1, p2);

    public static bool operator !=(ParallelCompositionProcess? p1, ParallelCompositionProcess? p2) => !Equals(p1, p2);

    public override string ToString()
    {
        List<string> procAsString = new(from p in Processes select p.ToString());
        return "(" + string.Join(" | ", procAsString) + ")";
    }

    #endregion
}
