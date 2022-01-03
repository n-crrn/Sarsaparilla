namespace AppliedPi;

public class ParseResult
{
    public IStatement? Statement { get; init; }

    public string ErrorMessage { get; init; }

    /// <summary>
    /// The position of interest for the error message. This will not be set if the statement
    /// has been parsed successfully.
    /// </summary>
    public RowColumnPosition? ErrorPosition { get; init; }

    /// <summary>
    /// A new statement has been read and is available for analysis.
    /// </summary>
    public bool Successful => Statement != null;

    /// <summary>
    /// The end of the code has been encountered, and not in the middle of a statement.
    /// </summary>
    public bool AtEnd { get; init; }

    private ParseResult(IStatement? s, string? errMsg, RowColumnPosition? errPosn, bool end)
    {
        Statement = s;
        ErrorMessage = errMsg ?? "No error";
        ErrorPosition = errPosn;
        AtEnd = end;
    }

    public static ParseResult Success(IStatement s) => new(s, null, null, false);

    public static ParseResult Failure(Parser p, string errMsg) => new(null, errMsg, p.GetRowColumn(), false);

    public static ParseResult Finished() => new(null, null, null, true);
}
