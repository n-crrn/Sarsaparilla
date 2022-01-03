using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AppliedPi.Model;

namespace AppliedPi.Statements;

public class QueryStatement : IStatement
{
    public SortedList<string, string> ParameterTypes { get; init; }

    public Term LeftHandSide { get; init; }

    public Term RightHandSide { get; init; }

    public QueryStatement(SortedList<string, string> paramTypes, Term lhs, Term rhs)
    {
        ParameterTypes = paramTypes;
        LeftHandSide = lhs;
        RightHandSide = rhs;
    }

    #region IStatement implementation.

    public string StatementType => "Query";

    public void ApplyTo(Network nw)
    {
        nw._Queries.Add(new Query(LeftHandSide, RightHandSide, ParameterTypes));
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is QueryStatement qs &&
            ParameterTypes.SequenceEqual(qs.ParameterTypes) &&
            LeftHandSide.Equals(qs.LeftHandSide) &&
            RightHandSide.Equals(qs.RightHandSide);
    }

    public override int GetHashCode() => Tuple.Create(LeftHandSide, RightHandSide).GetHashCode();

    public static bool operator ==(QueryStatement? qs1, QueryStatement? qs2) => Equals(qs1, qs2);

    public static bool operator !=(QueryStatement? qs1, QueryStatement? qs2) => !Equals(qs1, qs2);

    public override string ToString()
    {
        StringBuilder buffer = new();
        buffer.Append("query ");
        buffer.Append(StatementUtils.ParameterTypeListToString(ParameterTypes));
        buffer.Append("; ").Append(LeftHandSide.ToString()).Append(" ==> ").Append(RightHandSide.ToString());
        return buffer.ToString();
    }

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "query" has been read and we need to read the rest of the clause.
        string stmtType = "query";
        (SortedList<string, string>? paramTypes, string? token, string? paramErrMsg) = p.ReadParameterTypeList(stmtType);
        if (paramErrMsg != null)
        {
            return ParseResult.Failure(p, paramErrMsg);
        }
        Debug.Assert(paramTypes != null && token != null);

        if (token != ";")
        {
            return ParseResult.Failure(p, $"Expected ';' token, instead found '{token}' while reading {stmtType}.");
        }

        (Term lhs, string? maybeNextToken) = Term.ReadNamedTermAndNextToken(p, stmtType);
        token = maybeNextToken ?? p.ReadNextToken();
        if (token != "==>")
        {
            return ParseResult.Failure(p, $"Expected '==>' token, instead found '{token}' while reading {stmtType}.");
        }

        Term rhs;
        (rhs, maybeNextToken) = Term.ReadNamedTermAndNextToken(p, stmtType);
        token = maybeNextToken ?? p.ReadNextToken();
        if (token != ".")
        {
            return ParseResult.Failure(p, $"Expected '.' token, instead found '{token}' while reading {stmtType}.");
        }

        return ParseResult.Success(new QueryStatement(paramTypes, lhs, rhs));
    }
}
