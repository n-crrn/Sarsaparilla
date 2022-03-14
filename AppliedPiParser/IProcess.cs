using System.Collections.Generic;
using AppliedPi.Model;

namespace AppliedPi;

public interface IProcess
{
    public IProcess? Next { get; set; }

    public string ToString();

    public IEnumerable<string> Terms();

    public IProcess ResolveTerms(SortedList<string, string> subs);

    // FIXME: Add a facility to provide a formatted string in the Pi calculus.
}
