using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StatefulHorn.Query;

public class Attack
{

    public Attack(
        IMessage query,
        IMessage actual,
        HornClause clause,
        SigmaFactory transform,
        IEnumerable<Attack> premiseAttacks,
        State? when)
    {
        Query = query;
        Actual = actual;
        Clause = clause;
        Transformation = transform;
        When = when;

        Dictionary<IMessage, Attack> pAttacks = new();
        foreach (Attack a in premiseAttacks)
        {
            pAttacks[a.Actual] = a;
        }
        Premises = pAttacks;
    }

    #region Properties.

    public IMessage Query { get; private init; }

    public IMessage Actual { get; private init; }

    public HornClause Clause { get; private init; }

    public SigmaFactory Transformation { get; private init; }

    public IDictionary<IMessage, Attack> Premises { get; private init; }

    public State? When { get; private init; }

    #endregion
    #region String descriptions.

    public override string ToString() => $"Attack found for {Query} ({Actual})";

    public string DescribeSources()
    {
        StringWriter writer = new();
        DescribeSources(writer, 0);
        return writer.ToString();
    }

    private void DescribeSources(TextWriter writer, int indent = 0)
    {
        WriteLine(writer, indent, $"{Query} as {Actual} by transform set {Transformation}.");
        WriteLine(writer, indent, $"Based on clause {Clause}.");
        if (Premises.Count == 0)
        {
            WriteLine(writer, indent + 1, "No premises.");
        }
        else
        {
            WriteLine(writer, indent + 1, "Premises: " + string.Join(",", Premises.Keys));
            foreach (Attack premAttack in Premises.Values)
            {
                premAttack.DescribeSources(writer, indent + 2);
            }
        }
    }

    private static void WriteLine(TextWriter writer, int indent, string text)
    {
        for (int i = 0; i < indent; i++)
        {
            writer.Write("  ");
        }
        writer.WriteLine(text);
    }

    #endregion
}
