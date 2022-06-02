using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StatefulHorn;

/// <summary>
/// This is the original class for reporting the results of the QueryEngine.
/// </summary>
internal class QueryResult
{
    internal QueryResult(IMessage query, IMessage? actual, State? when)
    {
        Query = query;
        Actual = actual;
        When = when;
        Transformation = new();
    }

    public QueryResult(
        IMessage query,
        IMessage? actual,
        State? when,
        SigmaFactory transformation,
        IEnumerable<IMessage> facts,
        IEnumerable<HornClause> knowledge,
        IEnumerable<Nession> foundNessions)
    {
        Query = query;
        When = when;
        Actual = actual;
        Transformation = transformation;
        Facts = new(facts);
        Knowledge = new(knowledge);
        FoundSessions = new(foundNessions);
    }

    public static QueryResult BasicFact(IMessage query, IMessage actual, SigmaFactory transformation, State? when = null)
    {
        return new(query, actual, when, transformation, new List<IMessage>() { query }, new List<HornClause>(), new List<Nession>());
    }

    public static QueryResult ResolvedKnowledge(IMessage query, IMessage actual, HornClause kRule, SigmaFactory transformation)
    {
        return new(query, actual, null, transformation, new List<IMessage>(), new List<HornClause>() { kRule }, new List<Nession>());
    }

    public static QueryResult Failed(IMessage query, State? when) => new(query, null, when);

    public static QueryResult Compose(IMessage query, State? when, IEnumerable<QueryResult> combinedResults)
    {
        List<IMessage> fullFactList = new();
        List<HornClause> knowledgeList = new();
        List<Nession> nessions = new();

        HashSet<IMessage> emptyList = new();
        HashSet<HornClause> emptyKnowledge = new();
        List<Nession> emptyNession = new();

        foreach (QueryResult qr in combinedResults)
        {
            Debug.Assert(qr.Found);
            fullFactList.AddRange(qr.Facts ?? emptyList);
            knowledgeList.AddRange(qr.Knowledge ?? emptyKnowledge);
            nessions.AddRange(qr.FoundSessions ?? emptyNession);
        }

        return new(query, query, when, new(), fullFactList, knowledgeList, nessions);
    }
     
    #region Properties.

    public IMessage Query { get; init; }

    public IMessage? Actual { get; init; }

    public SigmaFactory Transformation { get; init; }

    public State? When { get; init; }

    public bool Found => Facts != null;

    public HashSet<IMessage>? Facts { get; init; }

    public HashSet<HornClause>? Knowledge { get; init; }

    public List<Nession>? FoundSessions { get; private set; }

    internal void AddSession(Nession n)
    {
        if (FoundSessions == null)
        {
            FoundSessions = new();
        }
        FoundSessions.Add(n);
    }

    #endregion
    #region Description (including ToString()).

    public override string ToString()
    {
        string whenStr = When != null ? $"when {When}" : "";
        if (Found)
        {
            Debug.Assert(Facts != null && Knowledge != null && FoundSessions != null);
            string facts = $"{Facts.Count} facts";
            string knowledge = $"{Knowledge.Count} knowledge rules";
            string sessions = $"{FoundSessions.Count} sessions";
            return $"Query {Query} {whenStr} found based on {facts}, {knowledge} and {sessions}.";
        }
        else
        {
            return $"Query {Query} {whenStr} not found.";
        }
    }

    public void Describe(TextWriter writer)
    {
        writer.WriteLine(ToString()); // Found or not found description.
        if (Found)
        {
            Debug.Assert(Facts != null && Knowledge != null && FoundSessions != null);
            writer.WriteLine("=== Facts ===");
            writer.WriteLine(string.Join("\n", Facts!));
            writer.WriteLine("=== Knowledge Rules ===");
            writer.WriteLine(string.Join('\n', Knowledge!));
            writer.WriteLine("=== Found Sessions ===");
            writer.WriteLine(string.Join("------", FoundSessions!));
        }
    }

    public void DescribeWithSources(TextWriter writer)
    {
        writer.WriteLine(ToString());
        if (Found)
        {
            Debug.Assert(Facts != null && Knowledge != null && FoundSessions != null);
            writer.WriteLine("=== Facts ===");
            writer.WriteLine(string.Join("\n", Facts!));
            writer.WriteLine("=== Rules and their sources ===");
            foreach (HornClause rule in Knowledge!)
            {
                if (rule.Source == null)
                {
                    writer.WriteLine($"{rule}, provided a priori.");
                } 
                else
                {
                    writer.WriteLine($"{rule}, sourced from:");
                    DescribeRuleSources(writer, rule.Source, 1);
                }
            }
            writer.WriteLine("=== Found Sessions ===");
            writer.WriteLine(string.Join("------", FoundSessions!));
        }
    }

    private void DescribeRuleSources(TextWriter writer, IRuleSource src, int indent)
    {
        const int indentSpaceCount = 2;
        writer.Write(IndentLines(src.Describe(), indentSpaceCount * indent));
        List<IRuleSource> furtherSources = src.Dependencies;
        if (furtherSources.Count > 0)
        {
            for (int i = 0; i < indentSpaceCount * indent; i++)
            {
                writer.Write(' ');
            }
            writer.WriteLine("...based on...");
            foreach (IRuleSource innerRuleSrc in furtherSources)
            {
                DescribeRuleSources(writer, innerRuleSrc, indent + 1);
            }
        }
    }

    private static string IndentLines(string input, int spaceCount)
    {
        StringBuilder builder = new();
        string[] lines = input.Split('\n');
        foreach (string l in lines)
        {
            for (int i = 0; i < spaceCount; i++)
            {
                builder.Append(' ');
            }
            builder.Append(l);
            builder.Append('\n');
        }
        return builder.ToString();
    }

    #endregion
}
