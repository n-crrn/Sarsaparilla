using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using AppliedPi.Model;

namespace AppliedPi.Statements;

public class DestructorStatement : IStatement
{
    public SortedList<string, string> ParameterTypes { get; init; }

    public Term LeftHandSide { get; init; }

    public string RightHandSide { get; init; }

    public DestructorStatement(Term lhs, string rhs, SortedList<string, string> paramTypes)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
        ParameterTypes = paramTypes;
    }

    #region IStatement implementation

    public string StatementType => "Destructor";

    public void ApplyTo(Network nw)
    {
        nw._Destructors.Add(new Destructor(LeftHandSide, RightHandSide, ParameterTypes));
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is DestructorStatement ds &&
            ParameterTypes.SequenceEqual(ds.ParameterTypes) &&
            LeftHandSide.Equals(ds.LeftHandSide) &&
            RightHandSide.Equals(ds.RightHandSide);
    }

    public override int GetHashCode() => Tuple.Create(LeftHandSide, RightHandSide).GetHashCode();

    public static bool operator ==(DestructorStatement? ds1, DestructorStatement? ds2) => Equals(ds1, ds2);

    public static bool operator !=(DestructorStatement? ds1, DestructorStatement? ds2) => !Equals(ds1, ds2);

    public override string ToString()
    {
        StringBuilder buffer = new();
        buffer.Append("reduc forall ");
        buffer.Append(StatementUtils.ParameterTypeListToString(ParameterTypes));
        buffer.Append("; ").Append(LeftHandSide.ToString()).Append(" = ").Append(RightHandSide).Append('.');
        return buffer.ToString();
    }

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "reduc" has been read and we need to read the rest of the clause.
        string stmtType = "destructor (reduc forall)";
        p.ReadExpectedToken("forall", stmtType);

        // Read a series of comma separated "name : type" pairs.
        (SortedList<string, string>? paramTypes, string? token, string? paramErrMsg) = p.ReadParameterTypeList(stmtType);
        if (paramErrMsg != null)
        {
            return ParseResult.Failure(p, paramErrMsg);
        }
        Debug.Assert(paramTypes != null && token != null);

        if (token != ";")
        {
            return ParseResult.Failure(p, $"Expected ';' token, instead found {token} while reading {stmtType}.");
        }

        // Read the left-hand-side term, and ensure we have the equals sign.
        (Term lhs, string? maybeNextToken) = Term.ReadNamedTermAndNextToken(p, stmtType);
        token = maybeNextToken ?? p.ReadNextToken();
        if (token != "=")
        {
            return ParseResult.Failure(p, $"Expected '=' token, instead found {token} while reading {stmtType}.");
        }

        // Read the right-hand-side and the final full-stop.
        string rhs = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(".", stmtType);
        return ParseResult.Success(new DestructorStatement(lhs, rhs, paramTypes));
    }

}
