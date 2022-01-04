﻿using System.Text;
using AppliedPi.Model;

namespace AppliedPi.Statements;

public class ConstantStatement : IStatement
{

    public ConstantStatement(string n, string pt, string tg)
    {
        Name = n;
        PiType = pt;
        Tag = tg;
    }

    public string Name { get; init; }

    public string PiType { get; init; }

    public string Tag { get; init; }

    #region IStatement implementation.

    public string StatementType { get; } = "Constant";

    public void ApplyTo(Network nw)
    {
        nw._Constants.Add(new Constant(Name, PiType, Tag));
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is ConstantStatement cs &&
            Name.Equals(cs.Name) &&
            PiType.Equals(cs.PiType) &&
            Tag.Equals(cs.Tag);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(ConstantStatement? cs1, ConstantStatement? cs2) => Equals(cs1, cs2);

    public static bool operator !=(ConstantStatement? cs1, ConstantStatement? cs2) => !Equals(cs1, cs2);

    public override string ToString()
    {
        StringBuilder buffer = new();
        buffer.Append("const ").Append(Name).Append(": ").Append(PiType).Append(" [").Append(Tag).Append("].");
        return buffer.ToString();
    }

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "const" has been read and now we need to read the rest of the clause.
        string termType = "constant";
        string name = p.ReadNameToken(termType);
        p.ReadExpectedToken(":", termType);
        string type = p.ReadNameToken(termType);
        p.ReadExpectedToken("[", termType);
        string tag = p.ReadNameToken(termType);
        p.ReadExpectedToken("]", termType);
        p.ReadExpectedToken(".", termType);
        return ParseResult.Success(new ConstantStatement(name, type, tag));
    }

}