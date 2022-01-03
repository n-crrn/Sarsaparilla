using System.Collections.Generic;

using AppliedPi.Model;

namespace AppliedPi.Processes;

public class InsertTableProcess : IProcess
{
    public InsertTableProcess(Term tableDesc)
    {
        TableTerm = tableDesc;
    }

    public override bool Equals(object? obj)
    {
        return obj is InsertTableProcess itp && TableTerm == itp.TableTerm;
    }

    public override int GetHashCode() => TableTerm.GetHashCode();

    public static bool operator ==(InsertTableProcess itp1, InsertTableProcess itp2) => Equals(itp1, itp2);

    public static bool operator !=(InsertTableProcess itp1, InsertTableProcess itp2) => !Equals(itp1, itp2);

    public override string ToString() => $"insert {TableTerm}";

    private readonly Term TableTerm;

    public string TableName => TableTerm.Name;

    public IReadOnlyList<Term> WriteTerms => TableTerm.Parameters;

    public IProcess? Next { get; set; }
}
