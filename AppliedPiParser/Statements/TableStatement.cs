using System.Collections.Generic;
using System.Linq;
using System.Text;

using AppliedPi.Model;

namespace AppliedPi.Statements;

public class TableStatement : IStatement
{

    public TableStatement(string n, List<string> cols, RowColumnPosition? definedAt)
    {
        Name = n;
        Columns = cols;
        DefinedAt = definedAt;
    }

    public string Name { get; init; }

    public List<string> Columns { get; init; }

    #region IStatement implementation.

    public string StatementType => "Table";

    public void ApplyTo(Network nw)
    {
        nw._Tables[Name] = new Table(Name, Columns);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is TableStatement ts &&
            Name.Equals(ts.Name) &&
            Columns.SequenceEqual(ts.Columns);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(TableStatement? ts1, TableStatement? ts2) => Equals(ts1, ts2);

    public static bool operator !=(TableStatement? ts1, TableStatement? ts2) => !Equals(ts1, ts2);

    public override string ToString()
    {
        StringBuilder buffer = new();
        buffer.Append("table ").Append(Name).Append('(').Append(string.Join(", ", Columns)).Append(").");
        return buffer.ToString();
    }

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "table" has been read and we need to read the rest of the clause.
        RowColumnPosition? pos = p.GetRowColumn();
        (string name, List<string> columnList) = p.ReadFlatTerm("table");
        p.ReadExpectedToken(".", "table");
        return ParseResult.Success(new TableStatement(name, columnList, pos));
    }
}
