using System;
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

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs) => new CallProcess(CallSpecification.ResolveTerm(subs));

    public IEnumerable<string> VariablesDefined() => Enumerable.Empty<string>();

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        // Check that the called macro exists.
        if (!nw.LetDefinitions.TryGetValue(Name, out UserDefinedProcess? udp))
        {
            errorMessage = $"No message with name {Name} exists.";
            return false;
        }

        // Check macro arguments.
        int paramCount = udp!.Parameters.Count;
        int callSpecCount = CallSpecification.Parameters.Count;
        if (udp!.Parameters.Count != CallSpecification.Parameters.Count)
        {
            errorMessage = $"Call specifies {callSpecCount} parameters, {paramCount} parameters required.";
            return false;
        }
        for (int i = 0; i < paramCount; i++)
        {
            Term paramSpec = CallSpecification.Parameters[i];
            if (!termResolver.Resolve(paramSpec, out TermRecord? tr))
            {
                errorMessage = $"Term {paramSpec} could not be resolved.";
                return false;
            }

            (string paramName, string piType) = udp.Parameters[i];
            if (!tr!.Type.IsBasicType(piType))
            {
                errorMessage = $"Term {paramSpec} for parameter {paramName} is type {tr!.Type} instead of {piType}";
                return false;
            }
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        return resolver.ResolveMacroCall(Name, new(from p in CallSpecification.Parameters select p.ToString()));
    }

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
