using System.Collections.Generic;
using System.Linq;
using System.Text;

using AppliedPi.Model;

namespace AppliedPi.Statements;

public class EventStatement : IStatement
{
    public string Name { get; init; }

    public List<string> Parameters { get; init; }

    public EventStatement(string n, List<string> paramList)
    {
        Name = n;
        Parameters = paramList;
    }

    #region IStatement implementation.

    public string StatementType => "Event";

    public void ApplyTo(Network nw)
    {
        nw._Events.Add(new Event(Name, Parameters));
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is EventStatement es &&
            Name.Equals(es.Name) &&
            Parameters.SequenceEqual(es.Parameters);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(EventStatement? es1, EventStatement? es2) => Equals(es1, es2);

    public static bool operator !=(EventStatement? es1, EventStatement? es2) => !Equals(es1, es2);

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
        (string name, List<string> paramList) = p.ReadFlatTerm("event");
        p.ReadExpectedToken(".", "event");
        return ParseResult.Success(new EventStatement(name, paramList));
    }
}
