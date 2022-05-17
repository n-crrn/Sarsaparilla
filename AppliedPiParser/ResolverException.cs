using System;

using AppliedPi.Model;

namespace AppliedPi;

public class ResolverException : Exception
{
    public ResolverException(Term term) : base($"Could not resolve {term}.") { }

    public ResolverException(IComparison cmp, PiType? type) 
        : base($"Invalid type for comparison '{cmp}': " + (type?.ToString() ?? "<None>"))
    { }
}
