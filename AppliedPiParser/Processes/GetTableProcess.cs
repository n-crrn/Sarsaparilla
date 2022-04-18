using System;
using System.Collections.Generic;
using System.Linq;

using AppliedPi.Model;

namespace AppliedPi.Processes;

public class GetTableProcess : IProcess
{
    public GetTableProcess(string table, List<(bool, string)> maList)
    {
        TableName = table;
        MatchAssignList = maList;
    }

    public string TableName { get; init; }

    public List<(bool, string)> MatchAssignList;

    #region IProcess implementation.

    public IEnumerable<string> Terms() => from ma in MatchAssignList select ma.Item2;

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        List<(bool, string)> newMAList = new(from ma in MatchAssignList
                                             select (ma.Item1, subs.GetValueOrDefault(ma.Item2, ma.Item2)));
        return new GetTableProcess(TableName, newMAList);
    }

    public IEnumerable<string> VariablesDefined()
    {
        foreach ((bool match, string assign) in MatchAssignList)
        {
            if (!match)
            {
                yield return assign;
            }
        }
    }

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher) => Enumerable.Empty<IProcess>();

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        // Check that the table exists - as the table name cannot just be used as a normal term,
        // no action is taken to register the name with the TermResolver.
        if (!nw.Tables.TryGetValue(TableName, out Table? table))
        {
            errorMessage = $"Table {TableName} not defined";
            return false;
        }

        // Check that the column types match. As we do that, collect new declarations.
        int tableColCount = table!.Columns.Count;
        int maCount = MatchAssignList.Count;
        if (maCount != tableColCount)
        {
            errorMessage = $"Table {TableName} has {tableColCount} columns, attempt to match/assign on {maCount} columns.";
            return false;
        }
        List<(Term, TermRecord)> declarations = new();
        for (int i = 0; i < MatchAssignList.Count; i++)
        {
            (bool match, string assign) = MatchAssignList[i];
            Term givenTerm = new(assign);
            if (match)
            {
                // Matching.
                if (!termResolver.Resolve(givenTerm, out TermRecord? tr))
                {
                    errorMessage = $"Value '{assign}' not recognised.";
                    return false;
                }
                string tableType = table.Columns[i];
                if (tableType != tr!.Type.Name && !tr!.Type.IsComposite)
                {
                    errorMessage = $"Type mismatch, attempt to match type {tr!.Type.Name} with table type {tableType}";
                    return false;
                }
            }
            else
            {
                // Assigning.
                if (givenTerm.IsConstructed)
                {
                    errorMessage = $"Cannot assign value to constructed term '{assign}'.";
                    return false;
                }
                declarations.Add((givenTerm, new(TermSource.Table, new(table.Columns[i]))));
            }
        }

        // Given that everything passes, let's add assigned terms and move on.
        foreach ((Term t, TermRecord r) in declarations)
        {
            if (!termResolver.Register(t, r))
            {
                errorMessage = $"Term {t} declared multiple times.";
                return false;
            }
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        Table table = nw.Tables[TableName];
        for (int i = 0; i < MatchAssignList.Count; i++)
        {
            (bool match, string assign) = MatchAssignList[i];
            if (match)
            {
                resolver.ResolveOrThrow(new(assign));
            }
            else
            {
                resolver.Register(new(assign), new(TermSource.Table, new(table.Columns[i])));
            }
        }
        return this;
    }

    #endregion

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is GetTableProcess gtp &&
            TableName.Equals(gtp.TableName) &&
            MatchAssignList.SequenceEqual(gtp.MatchAssignList);
    }

    public override int GetHashCode() => TableName.GetHashCode();

    public static bool operator ==(GetTableProcess p1, GetTableProcess p2) => p1.Equals(p2);

    public static bool operator !=(GetTableProcess p1, GetTableProcess p2) => !p1.Equals(p2);

    public override string ToString()
    {
        List<string> formattedMatches = new();
        foreach ((bool isMatch, string name) in MatchAssignList)
        {
            formattedMatches.Add(isMatch ? "=" + name : name);
        }
        return $"get {TableName}(" + string.Join(", ", formattedMatches) + ")";
    }

    #endregion
}
