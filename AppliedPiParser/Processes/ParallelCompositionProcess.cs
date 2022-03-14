using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Processes;

public class ParallelCompositionProcess : IProcess
{
    public ParallelCompositionProcess(IProcess proc, bool replicated)
    {
        Processes = new() { (proc, replicated) };
    }

    public List<(IProcess Process, bool Replicated)> Processes { get; init; }

    public void Add(IProcess proc, bool replicated)
    {
        Processes.Add((proc, replicated));
    }

    #region IProcess implementation.

    public IProcess? Next { get; set; }

    public IEnumerable<string> Terms()
    {
        IEnumerable<string> terms = Processes[0].Process.Terms();
        for (int i = 1; i < Processes.Count; i++)
        {
            terms = terms.Concat(Processes[i].Process.Terms());
        }
        return terms;
    }

    public IProcess ResolveTerms(SortedList<string, string> subs)
    {
        ParallelCompositionProcess pcp = new(Processes[0].Process, Processes[0].Replicated);
        for (int i = 1; i < Processes.Count; i++)
        {
            (IProcess p, bool repl) = Processes[i];
            pcp.Add(p, repl);
        }
        return pcp;
    }

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
            List<(IProcess, bool)> unaccounted = new(pcp.Processes);
            foreach ((IProcess thisProcess, bool thisReplicated) in Processes)
            {
                if (!unaccounted.Remove((thisProcess, thisReplicated)))
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
