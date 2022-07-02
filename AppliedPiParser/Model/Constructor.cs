using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

public class Constructor : IStatement
{

    public Constructor(
        string nm, 
        List<string> paramTypes, 
        string tpe, 
        bool isPrivate,
        RowColumnPosition? definedAt)
    {
        Name = nm;
        ParameterTypes = paramTypes;
        PiType = tpe;
        DeclaredPrivate = isPrivate;
        DefinedAt = definedAt;
    }

    public string Name { get; init; }

    public List<string> ParameterTypes { get; init; }

    public string PiType { get; init; }

    public bool DeclaredPrivate { get; init; }

    #region IStatement implementation.

    public void ApplyTo(Network nw)
    {
        nw._Constructors[Name] = this;
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides.

    public override string ToString()
    {
        string text = $"fun {Name}(" + string.Join(", ", ParameterTypes) + ") : " + PiType;
        if (DeclaredPrivate)
        {
            text += " [private]";
        }
        return text;
    }

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Constructor c &&
            Name.Equals(c.Name) &&
            ParameterTypes.SequenceEqual(c.ParameterTypes) &&
            PiType.Equals(c.PiType) &&
            DeclaredPrivate == c.DeclaredPrivate;
    }

    public override int GetHashCode() => Name.GetHashCode();

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "fun" has been read and now we need to read the rest of the clause.
        // Read the term part of the function first.
        string termType = "constructor (fun)";
        RowColumnPosition? pos = p.GetRowColumn();
        (string name, List<string> paramTypeList) = p.ReadFlatTerm(termType);
        p.ReadExpectedToken(":", termType);
        string piType = p.ReadNameToken(termType);
        List<string> tags = p.TryReadTag(termType);
        bool isPrivate = tags.Contains("private");
        p.ReadExpectedToken(".", termType);
        return ParseResult.Success(new Constructor(name, paramTypeList, piType, isPrivate, pos));
    }

}
