using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AppliedPi.Model;

namespace AppliedPi.Statements;

public class QueryStatement : IStatement
{
    
    public QueryStatement(Term t) : this(new List<Term>() { t }, null) { }

    public QueryStatement(IEnumerable<Term> ts, RowColumnPosition? definedAt)
    {
        Terms = new(ts);
        Debug.Assert(0 != Terms.Count);
        DefinedAt = definedAt;
    }

    public HashSet<Term> Terms;

    #region IStatement implementation.

    public void ApplyTo(Network nw)
    {
        foreach (Term t in Terms)
        {
            nw._Queries.Add(new AttackerQuery(t));
        }
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj) => obj is QueryStatement qs && Terms.SetEquals(qs.Terms);

    public override int GetHashCode() => (from t in Terms select t.GetHashCode()).Sum();

    public static bool operator ==(QueryStatement? qs1, QueryStatement? qs2) => Equals(qs1, qs2);

    public static bool operator !=(QueryStatement? qs1, QueryStatement? qs2) => !Equals(qs1, qs2);

    public override string ToString() => "query " + string.Join("; ", Terms);

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "query" has been read and we need to read the rest of the clause.
        string stmtType = "query";
        RowColumnPosition pos = p.GetRowColumn();

        string? nextToken = null;
        List<Term> terms = new();
        while (nextToken != ".")
        {
            p.ReadExpectedToken("attacker", stmtType);
            p.ReadExpectedToken("(", stmtType);
            nextToken = p.PeekNextToken();
            if (nextToken == "new")
            {
                // Ignore 'new' - this is superficial information.
                p.ReadNextToken();
            }
            terms.Add(Term.ReadTerm(p, stmtType));
            p.ReadExpectedToken(")", stmtType);

            nextToken = p.ReadNextToken();
            if (nextToken != "." && nextToken != ";")
            {
                return ParseResult.Failure(p, $"Expected ';' or '.', not {nextToken}.");
            }
        }

        return ParseResult.Success(new QueryStatement(terms, pos));
    }
}
