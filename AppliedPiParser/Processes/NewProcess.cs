using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Processes;

/// <summary>
/// Creates a nonce value set in a variable.
/// </summary>
public class NewProcess : IProcess
{
    public NewProcess(string varName, string type)
    {
        Variable = varName;
        PiType = type;
    }

    public string Variable { get; init; }

    public string PiType { get; init; }

    #region IProcess implementation.

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new NewProcess(subs.GetValueOrDefault(Variable, Variable), PiType);
    }

    public IEnumerable<string> VariablesDefined()
    {
        yield return Variable;
    }

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        bool couldRegister = termResolver.Register(new(Variable), new(TermSource.Nonce, new(PiType)));
        errorMessage = couldRegister ? null : $"Term {Variable} already defined.";
        return couldRegister;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        resolver.Register(new(Variable), new(TermSource.Nonce, new(PiType)));
        return this;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is NewProcess np && Variable == np.Variable && PiType == np.PiType;
    }

    public override int GetHashCode() => Variable.GetHashCode();

    public static bool operator ==(NewProcess proc1, NewProcess proc2) => Equals(proc1, proc2);

    public static bool operator !=(NewProcess proc1, NewProcess proc2) => !Equals(proc1, proc2);

    public override string ToString() => $"new {Variable}: {PiType}";

    #endregion
}
