﻿using System.Collections.Generic;
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
