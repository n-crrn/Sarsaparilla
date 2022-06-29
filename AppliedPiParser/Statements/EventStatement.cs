using System.Collections.Generic;
using System.Linq;
using System.Text;

using AppliedPi.Model;

namespace AppliedPi.Statements;

/// <summary>
/// A statement declaring the existance of an event (within the Pi model).
/// </summary>
public class PiEventStatement : IStatement
{

    public PiEventStatement(string n, List<string> paramList, RowColumnPosition? definedAt)
    {
        Name = n;
        Parameters = paramList;
        DefinedAt = definedAt;
    }

    public string Name { get; init; }

    public List<string> Parameters { get; init; }

    #region IStatement implementation.

    public string StatementType => "Event";

    public void ApplyTo(Network nw)
    {
        nw._Events[Name] = new Event(Name, Parameters);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is PiEventStatement es &&
            Name.Equals(es.Name) &&
            Parameters.SequenceEqual(es.Parameters);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(PiEventStatement? es1, PiEventStatement? es2) => Equals(es1, es2);

    public static bool operator !=(PiEventStatement? es1, PiEventStatement? es2) => !Equals(es1, es2);

    public override string ToString()
    {
        StringBuilder buffer = new();
        buffer.Append("event ").Append(Name).Append('(').Append(string.Join(", ", Parameters)).Append(").");
        return buffer.ToString();
    }

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "event" has been read and we need to read the rest of the clause.
        RowColumnPosition? pos = p.GetRowColumn();
        (string name, List<string> paramList) = p.ReadFlatTerm("event");
        p.ReadExpectedToken(".", "event");
        return ParseResult.Success(new PiEventStatement(name, paramList, pos));
    }

}
