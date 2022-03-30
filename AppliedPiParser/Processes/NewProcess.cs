using System.Collections.Generic;

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

    public IEnumerable<string> Terms()
    {
        yield return Variable;
    }

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new NewProcess(subs.GetValueOrDefault(Variable, Variable), PiType);
    }

    public IEnumerable<string> VariablesDefined()
    {
        yield return Variable;
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
