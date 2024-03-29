﻿namespace AppliedPi.Statements;

public class TypeStatement : IStatement
{
    public string Name { get; }

    public TypeStatement(string name, RowColumnPosition? definedAt)
    {
        Name = name;
        DefinedAt = definedAt;
    }

    #region IStatement implementation.

    public void ApplyTo(Network nw)
    {
        nw._PiTypes.Add(Name);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

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
        RowColumnPosition? pos = p.GetRowColumn();
        string typeName = p.ReadNameToken("type");
        p.ReadExpectedToken(".", "type");
        return ParseResult.Success(new TypeStatement(typeName, pos));
    }
}
