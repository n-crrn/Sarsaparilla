using System;

namespace AppliedPi.Statements;

public class ProcessStatement : IStatement
{
    public ProcessStatement(ProcessGroup pg)
    {
        SubProcesses = pg;
    }

    public ProcessGroup SubProcesses { get; init; }

    #region IStatement implementation.

    public string StatementType => "Process";

    public void ApplyTo(Network nw)
    {
        if (nw.MainProcess != null)
        {
            throw new ArgumentException("Network already has a main process.");
        }
        nw.MainProcess = SubProcesses;
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is ProcessStatement ps &&
            SubProcesses.Equals(ps.SubProcesses);
    }

    public override int GetHashCode() => SubProcesses.First.GetHashCode();

    public static bool operator ==(ProcessStatement? ps1, ProcessStatement? ps2) => Equals(ps1, ps2);

    public static bool operator !=(ProcessStatement? ps1, ProcessStatement? ps2) => !Equals(ps1, ps2);

    // FIXME: Add the ToString() implementation.

    #endregion

    public static ParseResult CreateFromStatement(Parser p)
    {
        (ProcessGroup? sub, string? subErrMsg) = ProcessGroup.ReadFromParser(p);
        if (subErrMsg != null)
        {
            return ParseResult.Failure(p, subErrMsg);
        }
        return ParseResult.Success(new ProcessStatement(sub!));
    }
}
