using System;
using System.Collections.Generic;
using System.Linq;

using AppliedPi.Model;

namespace AppliedPi.Processes;

public class InsertTableProcess : IProcess
{
    public InsertTableProcess(Term tableDesc)
    {
        TableTerm = tableDesc;
    }

    private readonly Term TableTerm;

    public string TableName => TableTerm.Name;

    public IReadOnlyList<Term> WriteTerms => TableTerm.Parameters;

    #region IProcess implementation.

    public IEnumerable<string> Terms() => TableTerm.BasicSubTerms;

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new InsertTableProcess(TableTerm.ResolveTerm(subs));
    }

    public IEnumerable<string> VariablesDefined() => Enumerable.Empty<string>();

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        if (!nw.Tables.TryGetValue(TableName, out Table? table))
        {
            errorMessage = $"Table {TableName} not defined.";
            return false;
        }

        int tableColCount = table!.Columns.Count;
        int paramCount = TableTerm.Parameters.Count;
        if (tableColCount != paramCount)
        {
            errorMessage = $"Insert has {paramCount} parameters, table has {tableColCount} columns.";
            return false;
        }
        for (int i = 0; i < paramCount; i++)
        {
            Term writeTerm = TableTerm.Parameters[i];
            if (!termResolver.Resolve(writeTerm, out TermRecord? tr))
            {
                errorMessage = $"Could not resolve term {writeTerm}.";
                return false;
            }
            if (!tr!.Type.IsBasicType(table!.Columns[i]))
            {
                errorMessage = $"Term {writeTerm} has type {tr!.Type}, instead of column type {table!.Columns[i]}.";
                return false;
            }
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        foreach (Term writeTerm in TableTerm.Parameters)
        {
            resolver.ResolveOrThrow(writeTerm);
        }
        return this;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is InsertTableProcess itp && TableTerm == itp.TableTerm;
    }

    public override int GetHashCode() => TableTerm.GetHashCode();

    public static bool operator ==(InsertTableProcess itp1, InsertTableProcess itp2) => Equals(itp1, itp2);

    public static bool operator !=(InsertTableProcess itp1, InsertTableProcess itp2) => !Equals(itp1, itp2);

    public override string ToString() => $"insert {TableTerm}";

    #endregion
}
