using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StatefulHorn;

public class Attack
{

    public Attack(IEnumerable<IMessage> facts, IEnumerable<HornClause> knowledge)
    {
        Facts = new HashSet<IMessage>(facts);
        Rules = new List<HornClause>(knowledge);
    }

    #region Properties.

    public IReadOnlySet<IMessage> Facts { get; init; }

    public IReadOnlyList<HornClause> Rules { get; init; }

    #endregion
    #region Composition.

    public static Attack Compose(IEnumerable<Attack> subAttacks)
    {
        IEnumerable<IMessage> allFacts = new List<IMessage>();
        IEnumerable<HornClause> allClauses = new List<HornClause>();

        foreach (Attack a in subAttacks)
        {
            allFacts = allFacts.Concat(a.Facts);
            allClauses = allClauses.Concat(a.Rules);
        }

        return new(allFacts, allClauses);
    }

    #endregion
    #region String descriptions.

    public override string ToString() => $"Attack based on {Facts.Count} fact(s) and {Rules.Count} rule(s).";

    public string DescribeSources()
    {
        StringWriter writer = new();
        DescribeSources(writer);
        return writer.ToString();
    }

    public void DescribeSources(TextWriter writer)
    {
        writer.WriteLine(ToString());
        writer.WriteLine("=== Facts ===");
        writer.WriteLine(string.Join('\n', Facts));
        writer.WriteLine("=== Rules and their sources ===");
        foreach (HornClause rule in Rules)
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
