using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Destructor : IStatement
{

    public Destructor(
        Term lhs, 
        string rhs, 
        SortedList<string, string> paramTypes,
        RowColumnPosition? definedAt)
    {
        LeftHandSide = lhs;
        RightHandSide = rhs;
        ParameterTypes = paramTypes;
        DefinedAt = definedAt;
    }

    public Term LeftHandSide { get; init; }

    public string RightHandSide { get; init; }

    public Term RightHandTerm => new(RightHandSide);

    public SortedList<string, string> ParameterTypes { get; init; }

    #region IStatement implementation.

    public void ApplyTo(Network nw)
    {
        nw._Destructors.Add(this);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides.

    public override string ToString()
    {
        return "reduce forall "
            + string.Join(", ", from p in ParameterTypes select $"{p.Key}: {p.Value}")
            + ";"
            + LeftHandSide.ToString()
            + " = "
            + RightHandSide;
    } 

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Destructor d &&
            LeftHandSide.Equals(d.LeftHandSide) &&
            RightHandSide.Equals(d.RightHandSide) &&
            ParameterTypes.SequenceEqual(d.ParameterTypes);
    }

    public override int GetHashCode() => LeftHandSide.GetHashCode();

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "reduc" has been read and we need to read the rest of the clause.
        string stmtType = "destructor (reduc forall)";
        RowColumnPosition? pos = p.GetRowColumn();
        p.ReadExpectedToken("forall", stmtType);

        // Read a series of comma separated "name : type" pairs.
        (SortedList<string, string>? paramTypes, string? token, string? paramErrMsg) = p.ReadParameterTypeList(stmtType);
        if (paramErrMsg != null)
        {
            return ParseResult.Failure(p, paramErrMsg);
        }
        // Note that paramTypes is guaranteed to not equal null.

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
        return ParseResult.Success(new Destructor(lhs, rhs, paramTypes!, pos));
    }

}
