﻿using System.Collections.Generic;
using System.Linq;
using System.Text;

using AppliedPi.Model;

namespace AppliedPi.Statements;

/// <summary>
/// Represents the "fun" clause in Applied Pi Code.
/// </summary>
public class ConstructorStatement : IStatement
{

    public ConstructorStatement(
        string n, 
        List<string> paramTypeList, 
        string type, 
        bool isPrivate,
        RowColumnPosition? definedAt)
    {
        Name = n;
        ParameterTypes = paramTypeList;
        PiType = type;
        DeclaredPrivate = isPrivate;
        DefinedAt = definedAt;
    }

    public string Name { get; init; }

    public List<string> ParameterTypes { get; init; }

    public string PiType { get; init; }

    public bool DeclaredPrivate { get; init; }

    #region IStatement implementation.

    public string StatementType => "Constructor";

    public void ApplyTo(Network nw)
    {
        nw._Constructors[Name] = new Constructor(Name, ParameterTypes, PiType, DeclaredPrivate);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is ConstructorStatement cs &&
            Name.Equals(cs.Name) &&
            ParameterTypes.SequenceEqual(cs.ParameterTypes) &&
            PiType.Equals(cs.PiType) &&
            DeclaredPrivate == cs.DeclaredPrivate;
    }

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(ConstructorStatement? cs1, ConstructorStatement? cs2) => Equals(cs1, cs2);

    public static bool operator !=(ConstructorStatement? cs1, ConstructorStatement? cs2) => !Equals(cs1, cs2);

    public override string ToString()
    {
        StringBuilder buffer = new();
        buffer.Append("fun ").Append(Name).Append('(');
        buffer.Append(string.Join(", ", ParameterTypes)).Append("): ");
        buffer.Append(PiType).Append('.');
        return buffer.ToString();
    }

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
        return ParseResult.Success(new ConstructorStatement(name, paramTypeList, piType, isPrivate, pos));
    }
}
