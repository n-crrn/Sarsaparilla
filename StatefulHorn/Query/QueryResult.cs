using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

using StatefulHorn.Messages;

namespace StatefulHorn.Query;

/// <summary>
/// This is the original class for reporting the results of the QueryEngine.
/// </summary>
internal class QueryResult
{
    internal QueryResult(IMessage query, IMessage? actual, int rank, State? when)
    {
        Query = query;
        Actual = actual;
        When = when;
        Rank = rank;
        Transformation = new();
    }

    public QueryResult(
        IMessage query,
        IMessage? actual,
        int rank,
        State? when,
        SigmaFactory transformation,
        IEnumerable<IMessage>? facts,
        IEnumerable<HornClause>? knowledge,
        IEnumerable<Nession>? foundNessions)
    {
        Query = query;
        Actual = actual;
        Rank = rank;
        When = when;
        Transformation = transformation;
        Facts = facts != null ? new(facts) : null;
        Knowledge = knowledge != null ? new(knowledge) : null;
        FoundSessions = foundNessions != null ? new(foundNessions) : null;
    }

    private static readonly int InfiniteRank = -1; // FIXME: Centralise the "infinite" values.

    public static QueryResult BasicFact(
        IMessage query,
        IMessage actual,
        SigmaFactory transformation,
        State? when = null)
    {
        return new(query, actual, InfiniteRank, when, transformation, new List<IMessage>() { query }, new List<HornClause>(), new List<Nession>());
    }

    public static QueryResult ResolvedKnowledge(
        IMessage query,
        IMessage actual,
        HornClause kRule,
        SigmaFactory transformation,
        State? when = null)
    {
        return new(query, actual, kRule.Rank, when, transformation, new List<IMessage>(), new List<HornClause>() { kRule }, new List<Nession>());
    }

    public static QueryResult Unresolved(VariableMessage query, int rank, State? when = null)
    {
        return new(query, query, rank, when, new(), null, null, null);
    }

    public static QueryResult Failed(IMessage query, int rank, State? when) => new(query, null, rank, when);

    public static QueryResult Compose(
        IMessage query,
        IMessage actual,
        State? when,
        SigmaFactory transformation,
        IEnumerable<QueryResult> combinedResults)
    {
        List<IMessage> fullFactList = new();
        List<HornClause> knowledgeList = new();
        List<Nession> nessions = new();

        HashSet<IMessage> emptyList = new();
        HashSet<HornClause> emptyKnowledge = new();
        List<Nession> emptyNession = new();

        int rank = -1;

        foreach (QueryResult qr in combinedResults)
        {
            Debug.Assert(qr.Found);
            rank = HornClause.RatchetRank(rank, qr.Rank);
            fullFactList.AddRange(qr.Facts ?? emptyList);
            knowledgeList.AddRange(qr.Knowledge ?? emptyKnowledge);
            nessions.AddRange(qr.FoundSessions ?? emptyNession);
        }

        return new(query, actual, rank, when, transformation, fullFactList, knowledgeList, nessions);
    }

    public QueryResult Transform(SigmaFactory transformation)
    {
        return new(
            Query,
            Actual!.Substitute(transformation.CreateBackwardMap()),
            Rank,
            When,
            transformation,
            Facts,
            Knowledge,
            FoundSessions);
    }

    #region Properties.

    public IMessage Query { get; init; }

    public IMessage? Actual { get; init; }

    public SigmaFactory Transformation { get; init; }

    public State? When { get; init; }

    public int Rank { get; init; }

    public bool Found => Facts != null || Actual != null && Actual is VariableMessage;

    public bool Resolved => Actual != null && !Actual.ContainsVariables;

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
