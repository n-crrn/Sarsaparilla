using System.Collections.Generic;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Processes;

public class MutateProcess : IProcess
{

    public MutateProcess(string name, Term newValue)
    {
        StateCellName = name;
        NewValue = newValue;
    }

    public string StateCellName { get; init; }

    public Term NewValue { get; init; }

    #region IProcess implementation.

    public IEnumerable<string> Terms() => NewValue.BasicSubTerms;

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new MutateProcess(StateCellName, NewValue.ResolveTerm(subs));
    }

    public IEnumerable<string> VariablesDefined() => Enumerable.Empty<string>();

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is MutateProcess mp && StateCellName.Equals(mp.StateCellName) && NewValue.Equals(mp.NewValue);
    }

    public override int GetHashCode() => (StateCellName, NewValue).GetHashCode();

    public static bool operator ==(MutateProcess mp1, MutateProcess mp2) => Equals(mp1, mp2);

    public static bool operator !=(MutateProcess mp1, MutateProcess mp2) => !Equals(mp1, mp2);

    public override string ToString() => $"mutate({StateCellName}, {NewValue})";

    #endregion

}
