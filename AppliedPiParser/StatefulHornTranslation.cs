using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;

namespace AppliedPi;

/// <summary>
/// Represents a completed translation from an Applied Pi source to a series of Stateful Horn
/// clauses. Not every Applied Pi source will result in a valid set of clauses. Therefore,
/// this class allows for holding a partial translation in a way that, for instance, 
/// StatefulHorn.QueryEngine does not.
/// </summary>
public class StatefulHornTranslation
{

    public HashSet<State> InitialState { get; } = new();

    public IMessage? QueryMessage { get; internal set; }

    public State? QueryWhen { get; internal set; }

    public HashSet<Rule> Rules { get; } = new();

    #region Basic object overrides.

    private static bool IMessageEquals(IMessage? msg1, IMessage? msg2) => msg1 == null ? msg2 == null : msg1.Equals(msg2);

    public override bool Equals(object? other)
    {
        return other is StatefulHornTranslation sht &&
            InitialState.SetEquals(sht.InitialState) &&
            IMessageEquals(QueryMessage, sht.QueryMessage) &&
            State.Equals(QueryWhen, sht.QueryWhen) &&
            Rules.SetEquals(sht.Rules);
    }

    public static bool operator ==(StatefulHornTranslation sht1, StatefulHornTranslation sht2) => Equals(sht1, sht2);

    public static bool operator !=(StatefulHornTranslation sht1, StatefulHornTranslation sht2) => !Equals(sht1, sht2);

    public override int GetHashCode()
    {
        int hc = 31;
        foreach (State s in InitialState)
        {
            hc = hc * 41 + s.GetHashCode();
        }
        foreach (Rule r in Rules)
        {
            hc = hc * 41 + r.GetHashCode();
        }
        return hc;
    }

    #endregion
    #region Describe translation.

    public void Describe(TextWriter writer)
    {
        writer.WriteLine("Initial state: " + string.Join(", ", InitialState));
        writer.WriteLine($"Query: {QueryMessage}");
        if (QueryWhen != null)
        {
            writer.WriteLine($"When: {QueryWhen}");
        }
        writer.WriteLine("Rules:");
        foreach (Rule r in Rules)
        {
            writer.WriteLine("  " + r.ToString());
        }
    }

    #endregion

}
