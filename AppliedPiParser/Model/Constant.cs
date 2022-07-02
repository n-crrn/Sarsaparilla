namespace AppliedPi.Model;

public class Constant : IStatement
{
    public Constant(string n, string t, string tag, RowColumnPosition? definedAt)
    {
        Name = n;
        PiType = t;
        Tag = tag;
        DefinedAt = definedAt;
    }

    #region Properties.

    public string Name { get; init; }

    public string PiType { get; init; }

    public string Tag { get; init; }

    #endregion
    #region IStatement implementation.

    public void ApplyTo(Network nw)
    {
        nw._Constants.Add(this);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Constant c &&
            Name.Equals(c.Name) &&
            PiType.Equals(c.PiType) &&
            Tag.Equals(c.Tag);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => $"const {Name}: {PiType}";

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "const" has been read and now we need to read the rest of the clause.
        string termType = "constant";
        RowColumnPosition pos = p.GetRowColumn();
        string name = p.ReadNameToken(termType);
        p.ReadExpectedToken(":", termType);
        string type = p.ReadNameToken(termType);

        string nextToken = p.ReadNextToken();
        string? tag = null;
        if (nextToken != ".")
        {
            if (nextToken == "[")
            {
                tag = p.ReadNameToken(termType);
                p.ReadExpectedToken("]", termType);
                p.ReadExpectedToken(".", termType);
            }
            else
            {
                ParseResult.Failure(p, $"Expected '.' or '[', instead found '{nextToken}'.");
            }
        }
        return ParseResult.Success(new Constant(name, type, tag ?? "", pos));
    }

}
