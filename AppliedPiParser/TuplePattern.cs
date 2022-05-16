using System;
using System.Collections.Generic;
using System.Linq;

using AppliedPi.Model;

namespace AppliedPi;

public class TuplePattern
{
    public struct Element
    {
        public bool IsMatcher { get; init; }

        public Term Term { get; init; }

        public string? Type { get; init; }

        public Element(bool match, string t, string? tpe) : this(match, new Term(t), tpe) { }

        public Element(bool match, Term t, string? tpe)
        {
            IsMatcher = match;
            if (!IsMatcher && t.IsConstructed)
            {
                throw new InvalidTermUsageException(t, "Attempted use as variable to be assigned to.");
            }
            Term = t;
            Type = tpe;
        }

        //public Element Resolve(IReadOnlyDictionary<string, string> subs) => new(IsMatcher, subs.GetValueOrDefault(Term, Term), Type);
        public Element Resolve(IReadOnlyDictionary<string, string> sub) => new(IsMatcher, Term.ResolveTerm(sub), Type);

        public override bool Equals(object? obj)
        {
            return obj is Element e && IsMatcher == e.IsMatcher && Term == e.Term && Type == e.Type;
        }

        public override int GetHashCode() => Term.GetHashCode();

        public static bool operator ==(Element e1, Element e2) => Equals(e1, e2);

        public static bool operator !=(Element e1, Element e2) => !Equals(e1, e2);

        public override string ToString()
        {
            string matcherStr = IsMatcher ? "=" : "";
            string typeStr = Type == null ? "" : $": {Type}";
            return $"{matcherStr}{Term}{typeStr}";
        }
    }

    public TuplePattern(List<Element> elems)
    {
        Elements = elems;
    }

    public static TuplePattern CreateBasic(List<string> simple)
    {
        List<Element> asElements = new(from s in simple select new Element(false, s, null));
        return new TuplePattern(asElements);
    }

    public static TuplePattern CreateSingle(string soleElement, string? piType = null)
    {
        return new(new List<Element>() { new(false, soleElement, piType) });
    }

    public List<Element> Elements { get; init; }

    public TuplePattern ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new(new(from e in Elements select e.Resolve(subs)));
    }

    public IEnumerable<Term> MatchTerms => from e in Elements where e.IsMatcher select e.Term;

    public IEnumerable<string> AssignedVariables => from e in Elements where !e.IsMatcher select e.Term.Name;

    public IEnumerable<(Term, string?)> AssignedTerms => from e in Elements where !e.IsMatcher select (e.Term, e.Type);

    #region Basic object overrides - required for unit testing.

    public override bool Equals(object? obj)
    {
        return obj is TuplePattern tp && Elements.SequenceEqual(tp.Elements);
    }

    public override int GetHashCode() => Elements.GetHashCode();

    public static bool operator ==(TuplePattern tp1, TuplePattern tp2) => Equals(tp1, tp2);

    public static bool operator !=(TuplePattern tp1, TuplePattern tp2) => !Equals(tp1, tp2);

    public override string ToString()
    {
        List<string> parts = new(from e in Elements select e.ToString());
        return "(" + string.Join(", ", parts) + ")";
    }

    #endregion
    #region Pattern parsing from Applied Pi Code.

    internal static TuplePattern ReadPatternAndNextToken(Parser p, string stmtType)
    {
        List<Element> elements = new();
        string token = p.PeekNextToken();
        if (token == "(")
        {
            p.ReadNextToken(); // Jump past the "(".
            do
            {
                token = p.PeekNextToken();
                bool isMatcher = false;
                if (token == "=")
                {
                    isMatcher = true;
                    p.ReadNextToken();
                }

                Term subTerm = Term.ReadTerm(p, stmtType);
                token = p.PeekNextToken();
                if (token == ":")
                {
                    if (isMatcher)
                    {
                        throw new UnexpectedTokenException(new string[] { ",", ")" }, token, stmtType);
                    }
                    p.ReadNextToken();
                    token = p.ReadNameToken(stmtType);
                    elements.Add(new(isMatcher, subTerm, token));
                }
                else
                {
                    elements.Add(new(isMatcher, subTerm, null));
                }
                token = p.ReadNextToken();
                if (token != "," && token != ")")
                {
                    throw new UnexpectedTokenException(new string[] { ",", ")" }, token, stmtType);
                }
            } while (token != ")");
        }
        else
        {
            token = p.ReadNameToken(stmtType);
            p.ReadExpectedToken(":", stmtType);
            string typeToken = p.ReadNameToken(stmtType);
            elements.Add(new(false, token, typeToken));
        }
        return new(elements);
    }

    #endregion

}
