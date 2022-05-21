using System.Collections.Generic;
using System.Linq;
using System.Text;

using AppliedPi.Model;

namespace AppliedPi.Statements;

public class FreeStatement : IStatement
{
    public List<string> Names { get; init; }

    public string Type { get; init; }

    public bool DeclaredPrivate { get; init; }

    public FreeStatement(List<string> n, string t, bool priv)
    {
        Names = n;
        Type = t;
        DeclaredPrivate = priv;
    }

    #region IStatement implementation.

    public string StatementType => "Free";

    public void ApplyTo(Network nw)
    {
        foreach (string n in Names)
        {
            nw._FreeDeclarations[n] = new FreeDeclaration(n, Type, DeclaredPrivate);
        }
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is FreeStatement fs &&
            Names.SequenceEqual(fs.Names) &&
            Type.Equals(fs.Type) &&
            DeclaredPrivate == fs.DeclaredPrivate;
    }

    public override int GetHashCode()
    {
        return Names.Count > 0 ? Names[0].GetHashCode() : Type.GetHashCode();
    }

    public static bool operator ==(FreeStatement? fs1, FreeStatement? fs2) => Equals(fs1, fs2);

    public static bool operator !=(FreeStatement? fs1, FreeStatement? fs2) => !Equals(fs1, fs2);

    public override string ToString()
    {
        StringBuilder buffer = new();
        buffer.Append("free ").Append(string.Join(", ", Names)).Append(": ").Append(Type);
        if (DeclaredPrivate)
        {
            buffer.Append("[private]");
        }
        buffer.Append('.');
        return buffer.ToString();
    }

    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "free" has been read and now we need to read the rest of the clause.

        // Read the names.
        string stmtType = "free";
        List<string> names = new();
        string token;
        do
        {
            names.Add(p.ReadNameToken(stmtType));
            token = p.ReadNextToken();
        } while (token == ",");

        // Check the existance of the type separation character, and grab the type name.
        if (token != ":")
        {
            return ParseResult.Failure(p, $"Expected ':', instead found '{token}'.");
        }
        string typeName = p.ReadNameToken(stmtType);

        // Do we have a modifier (e.g. private)?
        List<string> tags = p.TryReadTag(stmtType);
        bool declPrivate = tags.Contains("private");
        token = p.ReadNextToken();
        if (token != ".")
        {
            return ParseResult.Failure(p, $"Expected '.' indicating end of statement, instead found '{token}'.");
        }

        return ParseResult.Success(new FreeStatement(names, typeName, declPrivate));
    }
}
