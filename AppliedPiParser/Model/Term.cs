using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AppliedPi.Model;

/// <summary>
/// Represents a term, where there may be a nesting of other terms. This is a rare class in
/// AppliedPi in that it is not only involved in the Model, but it also provides for the 
/// parsing of instances of its type.
/// </summary>
public class Term : ITermGenerator
{

    #region Term creation.

    /// <summary>
    /// Create a new Term with the given name and no parameters.
    /// </summary>
    /// <param name="name">The full text of the term.</param>
    public Term(string name) : this(name, new()) { }

    /// <summary>
    /// Create a new Term with the given name and parameters.
    /// </summary>
    /// <param name="name">Name of the term (e.g. the 'h' in 'h(a)').</param>
    /// <param name="parameters">
    /// Parameters of the term (e.g. the 'a' and 'b' in 'h(a, b)').
    /// </param>
    public Term(string name, List<Term> parameters)
    {
        Name = name;
        _Parameters = parameters;
    }

    /// <summary>
    /// Create a new tuple Term with the given parameters.
    /// </summary>
    /// <param name="parameters">Inner Terms of the tuple.</param>
    /// <returns>New tuple Term with the given inner Terms.</returns>
    public static Term Tuple(List<Term> parameters)
    {
        return new Term("", parameters);
    }

    /// <summary>
    /// Converts a text representation of a Term into a Term. Note that this method has no error
    /// checking, and will parse Terms on a best-effort basis.
    /// </summary>
    /// <param name="representation">Text representation.</param>
    /// <returns>An in-memory representation of the Term.</returns>
    public static Term From(string representation)
    {
        (Term t, int _) = InnerParse(representation, 0);
        return t;
    }

    /// <summary>
    /// Convenience method for Term.From(string). This method will return a Term from within the
    /// textual representation of a larger term, as well as the position from which to either
    /// read the next term or the closing bracket of the larger term.
    /// </summary>
    /// <param name="repr">Full textual representation of the overall term.</param>
    /// <param name="startPos">Offset to start reading the inner term.</param>
    /// <returns>The inner read term.</returns>
    private static (Term, int) InnerParse(string repr, int startPos)
    {
        StringBuilder nameToken = new();
        int pos = startPos;

        // Read in name.
        while (pos < repr.Length)
        {
            char c = repr[pos];
            if (c != '(' && c != ',' && c != ')')
            {
                nameToken.Append(repr[pos]);
            }
            else
            {
                break;
            }
            pos++;
        }

        List<Term> parameters = new();
        if (pos < repr.Length && repr[pos] == '(')
        {
            pos++;
            // Read any inner terms.
            while (pos < repr.Length && repr[pos] != ')')
            {
                (Term nextTerm, pos) = InnerParse(repr, pos);
                parameters.Add(nextTerm);
                if (repr[pos] == ',')
                {
                    pos++;
                }
            }
            pos++; // Move past the ')'.
            if (pos < repr.Length && repr[pos] == ',')
            {
                pos++;
            }
        }

        string name = nameToken.ToString().Trim();
        if (name == string.Empty)
        {
            return (Term.Tuple(parameters), pos);
        }
        else if (parameters.Count > 0)
        {
            return (new Term(name, parameters), pos);
        }
        return (new Term(name), pos);
    }

    #endregion
    #region Properties.

    public string Name { get; init; }

    public bool IsTuple => Name == string.Empty;

    public bool IsConstructed => Name != string.Empty && _Parameters.Count > 0;

    private readonly List<Term> _Parameters;
    public IReadOnlyList<Term> Parameters
    {
        get => _Parameters.AsReadOnly();
    }

    #endregion
    #region ITermGenerator implementation.

    public Term Substitute(IReadOnlyDictionary<Term, Term> subs)
    {
        if (_Parameters.Count > 0)
        {
            return new(Name, new(from p in _Parameters select p.Substitute(subs)));
        }
        return subs.GetValueOrDefault(this, this);
    }

    public Term ResolveTerm(IReadOnlyDictionary<string, string> varSubstitutions)
    {
        // If this is not a leaf node, then we conduct the substitutions on the child
        // parameters and return a Term with the otherwise same name.
        if (_Parameters.Count > 0)
        {
            List<Term> newParams = new();
            foreach (Term p in Parameters)
            {
                newParams.Add(p.ResolveTerm(varSubstitutions));
            }
            return new Term(Name, newParams);
        }
        if (varSubstitutions.TryGetValue(Name, out string? replacement))
        {
            return new Term(replacement!);
        }
        return this; // No substitution, nothing gained from copying self.
    }

    public SortedSet<string> BasicSubTerms
    {
        get
        {
            SortedSet<string> vars = new();
            if (_Parameters.Count > 0)
            {
                foreach (Term p in Parameters)
                {
                    vars.UnionWith(p.BasicSubTerms);
                }
            }
            else
            {
                if (Name != "")
                {
                    vars.Add(Name);
                }
            }
            return vars;
        }
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj) => obj is Term t && Name.Equals(t.Name) && _Parameters.SequenceEqual(t._Parameters);

    public override int GetHashCode()
    {
        // Randomly selected prime numbers.
        int hc = 7901 * 7907 + Name.GetHashCode();
        foreach (Term p in _Parameters)
        {
            hc = hc * 7907 + p.GetHashCode();
        }
        return hc;
    }

    public static bool operator ==(Term? t1, Term? t2) => Equals(t1, t2);

    public static bool operator !=(Term? t1, Term? t2) => !Equals(t1, t2);

    public override string ToString()
    {
        if (_Parameters.Count == 0)
        {
            return Name;
        }
        string innerParams = string.Join(", ", _Parameters);
        return $"{Name}({innerParams})";
    }

    #endregion
    #region Applied Pi Code parsing.

    internal static (Term, string?) ReadNamedTermAndNextToken(Parser p, string stmtType)
    {
        // We are at the point where we have read part of a statement, and the higher-level
        // parser expects a term here.
        string name = p.ReadNameToken(stmtType);
        string token = p.ReadNextToken();
        // If there is no opening bracket, then we have loaded the whole term.
        if (token != "(")
        {
            return (new Term(name, new()), token);
        }

        return ReadTermAndNextTokenInternals(name, p, stmtType);
    }

    private static (Term, string?) ReadTermAndNextTokenInternals(string name, Parser p, string stmtType)
    {
        // Now deal with parameter terms recursively.
        List<Term> parameters = new();
        Term? subTerm;
        string? maybeToken;
        do
        {
            (subTerm, maybeToken) = ReadNamedTermAndNextToken(p, stmtType);
            parameters.Add(subTerm);
            if (maybeToken == null)
            {
                maybeToken = p.ReadNextToken();
            }
        } while (string.Equals(",", maybeToken));

        if (!string.Equals(")", maybeToken))
        {
            throw new UnexpectedTokenException(")", maybeToken, stmtType);
        }

        return (new Term(name, parameters), null);
    }

    internal static Term ReadTermParameters(Parser p, string termName, string stmtType)
    {
        string token = p.PeekNextToken();
        List<Term> parameters;
        if (token == "(")
        {
            _ = p.ReadNextToken();
            parameters = ReadTermInternals(p, stmtType);
        }
        else
        {
            parameters = new();
        }
        return new Term(termName, parameters);
    }

    internal static Term ReadTerm(Parser p, string stmtType)
    {
        string name = "";
        List<Term> parameters;

        string token = p.ReadNextToken();
        if (token == "(")
        {
            parameters = ReadTermInternals(p, stmtType);
        }
        else
        {
            if (!Parser.IsValidName(token))
            {
                throw new UnexpectedTokenException("<Name> or '('", token, stmtType);
            }
            name = token;

            token = p.PeekNextToken();
            if (token == "(")
            {
                // Skip past and grab parameters.
                _ = p.ReadNextToken();
                parameters = ReadTermInternals(p, stmtType);
            }
            else
            {
                parameters = new();
            }
        }

        return new Term(name, parameters);
    }

    private static List<Term> ReadTermInternals(Parser p, string stmtType)
    {
        List<Term> parameters = new();
        string token;
        Term subTerm;
        do
        {
            subTerm = ReadTerm(p, stmtType);
            parameters.Add(subTerm);
            token = p.ReadNextToken();
        } while (token == ",");
        UnexpectedTokenException.Check(")", token, stmtType);
        return parameters;
    }

    #endregion
}
