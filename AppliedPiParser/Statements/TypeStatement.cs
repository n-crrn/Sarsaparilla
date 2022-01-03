namespace AppliedPi.Statements;

public class TypeStatement : IStatement
{
    public string Name { get; }

    public TypeStatement(string name)
    {
        Name = name;
    }

    #region IStatement implementation.

    public string StatementType => "Type";

    public void ApplyTo(Network nw)
    {
        nw._PiTypes.Add(Name);
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null && obj is TypeStatement ts && Name.Equals(ts.Name);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(TypeStatement? ts1, TypeStatement? ts2) => Equals(ts1, ts2);

    public static bool operator !=(TypeStatement? ts1, TypeStatement? ts2) => !Equals(ts1, ts2);

    public override string ToString()
    {
        return $"type {Name}.";
    }

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "type" has been read and now we need to parse the rest.
        string typeName = p.ReadNameToken("type");
        p.ReadExpectedToken(".", "type");
        return ParseResult.Success(new TypeStatement(typeName));
    }
}
