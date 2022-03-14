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

        public string Name { get; init; }

        public string? Type { get; init; }

        public Element(bool match, string n, string? tpe)
        {
            IsMatcher = match;
            Name = n;
            Type = tpe;
        }

        public Element Resolve(SortedList<string, string> subs) => new(IsMatcher, subs.GetValueOrDefault(Name, Name), Type);

        public override bool Equals(object? obj)
        {
            return obj is Element e && IsMatcher == e.IsMatcher && Name == e.Name && Type == e.Type;
        }

        public override int GetHashCode() => Name.GetHashCode();

        public static bool operator ==(Element e1, Element e2) => Equals(e1, e2);

        public static bool operator !=(Element e1, Element e2) => !Equals(e1, e2);

        public override string ToString()
        {
            string matcherStr = IsMatcher ? "=" : "";
            string typeStr = Type ?? "";
            return $"{matcherStr}{Name}{typeStr}";
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

    public static TuplePattern CreateSingle(string soleElement)
    {
        return new(new List<Element>() { new(false, soleElement, null) });
    }

    public List<Element> Elements { get; init; }

    public TuplePattern ResolveTerms(SortedList<string, string> subs) => new(new(from e in Elements select e.Resolve(subs)));

    public IEnumerable<Term> MatchTerms => from e in Elements where e.IsMatcher select new Term(e.Name);

    public IEnumerable<(Term, string?)> AssignedTerms => from e in Elements where !e.IsMatcher select (new Term(e.Name), e.Type);

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

    internal static (TuplePattern ptn, string? nextToken) ReadPatternAndNextToken(Parser p, string stmtType)
    {
        // Are we reading a tuple, or just a single values?
        string token = p.ReadNextToken();
        List<Element> elements = new();
        string? nextToken = null;
        if (token == "(")
        {
            do
            {
                Element e;
                (e, token) = ReadElement(p, null, stmtType);
                elements.Add(e);
            } while (token == ",");
            UnexpectedTokenException.Check(")", token, stmtType);
        }
        else
        {
            Element singleElement;
            (singleElement, nextToken) = ReadElement(p, token, stmtType);
            elements.Add(singleElement);
        }
        return (new TuplePattern(elements), nextToken);
    }

    private static (Element e, string nextToken) ReadElement(Parser p, string? preToken, string stmtType)
    {
        string token = preToken ?? p.ReadNextToken();
        bool isMatcher = token == "=";
        if (isMatcher)
        {
            token = p.ReadNextToken();
        }
        InvalidNameTokenException.Check(token, stmtType);
        string name = token;
        string? piType = null;
        token = p.ReadNextToken();
        if (token == ":")
        {
            piType = p.ReadNameToken(stmtType);
            token = p.ReadNextToken();
        }
        return (new Element(isMatcher, name, piType), token);
    }

    #endregion

}
