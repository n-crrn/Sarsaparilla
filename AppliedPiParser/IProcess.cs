using System.Collections.Generic;
using AppliedPi.Model;

namespace AppliedPi;

public interface IProcess
{
    public string ToString();

    public IEnumerable<string> Terms();

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs);

    public IEnumerable<string> VariablesDefined();
}
