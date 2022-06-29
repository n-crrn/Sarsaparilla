using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AppliedPi.Processes;

namespace AppliedPi.Statements;

/// <summary>
/// A global Let statement (as versus one that is used within a process or another let) is 
/// a function declaration. By function, I mean that it will provide a process for a given
/// set of parameters.
/// </summary>
public class LetStatement : IStatement
{
    public LetStatement(
        string n, 
        List<(string, string)> paramList, 
        ProcessGroup sub,
        RowColumnPosition? definedAt)
    {
        Name = n;
        Parameters = paramList;
        SubProcesses = sub;
        DefinedAt = definedAt;
    }

    /// <summary>
    /// The name of the function process.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The set of parameters accepted by this function process, stored as a two string tuple.
    /// The first item of the tuple is the variable name, and the second is its type.
    /// </summary>
    public List<(string Name, string PiType)> Parameters { get; init; }

    public ProcessGroup SubProcesses { get; init; }

    #region IStatement implementation.

    public string StatementType => "Let";

    public void ApplyTo(Network nw)
    {
        if (nw.LetDefinitions.ContainsKey(Name))
        {
            throw new ArgumentException($"Network already has a let declaration for {Name}");
        }
        nw.AddUserDefinedProcess(new(Name, Parameters, SubProcesses));
    }

    public RowColumnPosition? DefinedAt { get; private init; }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is LetStatement ls &&
            Name.Equals(ls.Name) &&
            Parameters.SequenceEqual(ls.Parameters) &&
            SubProcesses.Equals(ls.SubProcesses);
    }

    public override int GetHashCode() => Name.GetHashCode();

    public static bool operator ==(LetStatement? ls1, LetStatement? ls2) => Equals(ls1, ls2);

    public static bool operator !=(LetStatement? ls1, LetStatement? ls2) => !Equals(ls1, ls2);

    public override string ToString()
    {
        string allParams = string.Join(", ", from pm in Parameters select $"{pm.Name} {pm.PiType}");
        return $"{Name} ({allParams})";
    }
    #endregion

    internal static ParseResult CreateFromStatement(Parser p)
    {
        // At this point, "let" has been read and now we need to read the rest of the clause.
        // Read the name and parameter list.
        string stmtType = "let";
        RowColumnPosition? pos = p.GetRowColumn();
        string name = p.ReadNameToken(stmtType);
        List<(string, string)> paramList = new();
        string paramToken = p.ReadNextToken();
        if (paramToken == "(")
        {
            do
            {
                List<string> namesThisType = new();
                do
                {
                    namesThisType.Add(p.ReadNameToken(stmtType));
                    paramToken = p.ReadNextToken();
                } while (paramToken == ",");
                if (paramToken != ":")
                {
                    return ParseResult.Failure(p, $"Expected ':' token, instead found {paramToken} while reading {stmtType}");
                }
                string typeName = p.ReadNameToken(stmtType);
                foreach (string paramName in namesThisType)
                {
                    paramList.Add((paramName, typeName));
                }
                paramToken = p.ReadNextToken();
            } while (paramToken == ",");
            if (paramToken != ")")
            {
                return ParseResult.Failure(p, $"Expected ')' token, instead found {paramToken} while reading {stmtType}");
            }
            paramToken = p.ReadNextToken();
        }

        if (paramToken != "=")
        {
            string errMsg = $"Expected '(' or '=' token, instead found {paramToken} while reading {stmtType}";
            return ParseResult.Failure(p, errMsg);
        }

        (ProcessGroup? sub, string? subErrMsg) = ProcessGroup.ReadFromParser(p);
        if (subErrMsg != null)
        {
            return ParseResult.Failure(p, subErrMsg);
        }
        return ParseResult.Success(new LetStatement(name, paramList, sub!, pos));
    }
}
