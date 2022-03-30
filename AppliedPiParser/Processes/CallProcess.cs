using System.Collections.Generic;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Processes;

public class CallProcess : IProcess
{
    public CallProcess(Term cs)
    {
        CallSpecification = cs;
    }

    public Term CallSpecification { get; init; }

    public string Name => CallSpecification.Name;

    #region IProcess implementation.

    public IEnumerable<string> Terms() => CallSpecification.BasicSubTerms;

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs) => new CallProcess(CallSpecification.ResolveTerm(subs));

    public IEnumerable<string> VariablesDefined() => Enumerable.Empty<string>();

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is CallProcess cp && CallSpecification == cp.CallSpecification;
    }

    public override int GetHashCode() => CallSpecification.GetHashCode();

    public static bool operator ==(CallProcess cp1, CallProcess cp2) => Equals(cp1, cp2);

    public static bool operator !=(CallProcess cp1, CallProcess cp2) => !Equals(cp1, cp2);

    public override string ToString() => CallSpecification.ToString();

    #endregion
}
