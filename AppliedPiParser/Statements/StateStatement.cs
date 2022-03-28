using System;
using AppliedPi.Model;

namespace AppliedPi.Statements;

public class StateStatement : IStatement
{

    public string Name { get; init; }

    public string? PiType { get; init; }

    public Term InitialValue { get; init; }

    public StateStatement(string name, Term init, string? piType = null)
    {
        Name = name;
        PiType = piType;
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
        nw._StateCells.Add(new StateCell(Name, InitialValue, PiType));
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj) => obj is StateStatement ss && InitialValue.Equals(ss.InitialValue);

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(StateStatement? ss1, StateStatement? ss2) => Equals(ss1, ss2);

    public static bool operator !=(StateStatement? ss1, StateStatement? ss2) => !Equals(ss1, ss2);

    public override string ToString() => InitialValue.ToString();

    #endregion
    #region Parsing.

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "state" has been read and we need to read the rest of the clause.
        const string statementType = "State";
        string stateName = p.ReadNextToken();
        p.ReadExpectedToken(":", statementType);
        string piType = p.ReadNextToken();
        p.ReadExpectedToken("=", statementType);
        Term initTerm = Term.ReadNamedTerm(p, statementType);
        p.ReadExpectedToken(".", statementType);
        return ParseResult.Success(new StateStatement(stateName, initTerm, piType));
    }

    #endregion
}
