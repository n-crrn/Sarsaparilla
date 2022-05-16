using System;
using System.Collections.Generic;

namespace AppliedPi;

public interface IProcess
{
    public string ToString();

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs);

    public IEnumerable<string> VariablesDefined();

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher);

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage);

    public IProcess Resolve(Network nw, TermResolver resolver);
}
