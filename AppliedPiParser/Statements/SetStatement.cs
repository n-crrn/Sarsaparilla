namespace AppliedPi.Statements;

/// <summary>
/// A set statement allows the user to adjust global properties for a model's evaluation.
/// Note that unrecognised properties are simply ignored, rather than regarded as an error.
/// </summary>
public class SetStatement : IStatement
{

    public SetStatement(string property, string value, RowColumnPosition? definedAt)
    {
        Property = property;
        Value = value;
        DefinedAt = definedAt;
    }

    public string Property { get; }

    public string Value { get; }

    #region IStatement implementation.

    public void ApplyTo(Network nw)
    {
        nw._Properties[Property] = Value;
    }

    public RowColumnPosition? DefinedAt { get; }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is SetStatement ss && Property.Equals(ss.Property) && Value.Equals(ss.Value);
    }

    public override int GetHashCode() => Property.GetHashCode();

    public override string ToString() => $"set {Property} = {Value}.";

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "set" has been read and now we need to read the rest of the clause.
        RowColumnPosition? pos = p.GetRowColumn();
        const string stmtType = "set";

        string propertyName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken("=", stmtType);
        string value = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(".", stmtType);

        return ParseResult.Success(new SetStatement(propertyName, value, pos));
    }

}
