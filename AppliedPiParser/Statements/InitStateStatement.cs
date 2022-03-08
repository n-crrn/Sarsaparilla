using System;
using AppliedPi.Model;

namespace AppliedPi.Statements;

public class InitStateStatement : IStatement
{

    public string Name { get; init; }

    public Term InitialValue { get; init; }

    public InitStateStatement(Term init)
    {
        Name = init.Name;
        InitialValue = init;
    }

    #region IStatement implementation.

    public string StatementType => "Init State";

    public void ApplyTo(Network nw)
    {
        if (nw.GetStateCell(Name) != null)
        {
            throw new ArgumentException($"Network already has a initial state declaration for {Name}.");
        }
        nw._InitialStates.Add(InitialValue);
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj) => obj is InitStateStatement ss && InitialValue.Equals(ss.InitialValue);

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(InitStateStatement? ss1, InitStateStatement? ss2) => Equals(ss1, ss2);

    public static bool operator !=(InitStateStatement? ss1, InitStateStatement? ss2) => !Equals(ss1, ss2);

    public override string ToString() => InitialValue.ToString();

    #endregion
    #region Parsing.

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "init-state" has been read and we need to read the rest of the clause.
        const string statementType = "State";
        Term initTerm = Term.ReadNamedTerm(p, statementType);
        p.ReadExpectedToken(".", statementType);
        return ParseResult.Success(new InitStateStatement(initTerm));
    }

    #endregion
}
