using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Processes;

public class ReplicateProcess : IProcess
{
    public ReplicateProcess(IProcess p)
    {
        Process = p;
    }

    public IProcess Process { get; init; }

    #region IProcess implementation.

    public IEnumerable<string> Terms() => Process.Terms();

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new ReplicateProcess(Process.ResolveTerms(subs));
    }

    public IEnumerable<string> VariablesDefined() => Process.VariablesDefined();

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        return Process.Check(nw, termResolver, out errorMessage);
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        return new ReplicateProcess(Process.Resolve(nw, resolver));
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj) => obj is ReplicateProcess r && Process.Equals(r.Process);

    public override int GetHashCode() => Process.GetHashCode();

    public static bool operator ==(ReplicateProcess rp1, ReplicateProcess rp2) => Equals(rp1, rp2);

    public static bool operator !=(ReplicateProcess rp1, ReplicateProcess rp2) => !Equals(rp1, rp2);

    public override string ToString() => $"! {Process}";

    #endregion
}
