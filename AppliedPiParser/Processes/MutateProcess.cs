using System;
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

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new MutateProcess(StateCellName, NewValue.ResolveTerm(subs));
    }

    public IEnumerable<string> VariablesDefined() => Enumerable.Empty<string>();

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        if (!termResolver.Resolve(new(StateCellName), out TermRecord? cellTr))
        {
            errorMessage = $"No state cell with name {StateCellName}.";
            return false;
        }
        if (cellTr!.Source != TermSource.StateCell)
        {
            errorMessage = $"Term {StateCellName} is not a state cell.";
            return false;
        }
        if (!termResolver.Resolve(NewValue, out TermRecord? newTr))
        {
            errorMessage = $"Could not resolve term {NewValue}.";
            return false;
        }
        if (!cellTr!.Type.Equals(newTr!.Type))
        {
            errorMessage = $"Type mismatch, attempt to assign term of type {newTr!.Type} to type {cellTr!.Type}.";
            return false;
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        resolver.ResolveOrThrow(new(StateCellName));
        resolver.ResolveOrThrow(NewValue);
        return this;
    }

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
