using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;
using StatefulHorn.Messages;
using AppliedPi.Model;

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

    public static (StatefulHornTranslation?, string?) Translate(Network nw)
    {
        StatefulHornTranslation trans = new();
        RuleFactory factory = new();
        HashSet<string> names = new();
        HashSet<string> stateCellNames = new();

        // Translate free names and constants to rules without premises.
        IEnumerable<string> rawNames = (from fd in nw.FreeDeclarations
                                        where !fd.Value.IsPrivate
                                        select fd.Key).Concat(from cd in nw.Constants
                                                              select cd.Name);
        names.UnionWith(rawNames);
        foreach (string name in names)
        {
            trans.Rules.Add(factory.CreateStateConsistentRule(K(new NameMessage(name))));
        }

        // Translate constructors.
        foreach ((string _, Constructor c) in nw.Constructors)
        {
            List<IMessage> premises = new(from p in VariableNamesForTypesList(c.ParameterTypes)
                                          select new VariableMessage(p));
            factory.RegisterPremises((from pMsg in premises select K(pMsg)).ToArray());
            trans.Rules.Add(factory.CreateStateConsistentRule(K(new FunctionMessage(c.Name, premises))));
        }

        // Translate destructors.
        foreach (Destructor d in nw.Destructors)
        {
            IMessage lhsMsg = TermToMessage(d.LeftHandSide, names);
            // FIXME: More complex logic may be required to handle functions, tuples and names.
            IMessage rhsMsg = new VariableMessage(d.RightHandSide);
            factory.RegisterPremise(K(lhsMsg));
            trans.Rules.Add(factory.CreateStateConsistentRule(K(rhsMsg)));
        }

        if (nw.MainProcess != null)
        {
            // The network needs to be resolved so that it's visible where state operations are needed.
            ResolvedNetwork resNw;
            try
            {
                resNw = ResolvedNetwork.From(nw);
            }
            catch (Exception ex)
            {
                return (null, $"Unable to resolve network: {ex}");
            }

            // FIXME: Complete translation here.
        }

        return (trans, null);
    }

    private static StatefulHorn.Event K(IMessage msg) => StatefulHorn.Event.Know(msg);

    /// <summary>
    /// In typed Applied Pi Calculus, the arguments for a constructor are provided as a list of
    /// types. Given that it is possible for a constructor to have multiple inputs requiring 
    /// the same 
    /// </summary>
    /// <param name="types"></param>
    /// <returns></returns>
    private static List<string> VariableNamesForTypesList(List<string> types)
    {
        List<string> names = new(types.Count);
        Dictionary<string, int> typeCounter = new();
        foreach (string t in types)
        {
            typeCounter.TryGetValue(t, out int tCount);
            if (tCount == 0)
            {
                names.Add(t);
                tCount = 1;
            }
            else
            {
                names.Add($"{t}-{tCount}");
            }
            typeCounter[t] = tCount + 1;

        }
        return names;
    }

    private static IMessage TermToMessage(Term t, HashSet<string> knownNames)
    {
        if (t.Parameters.Count == 0)
        {
            return VarOrNameMessage(t.Name, knownNames);
        }
        IEnumerable<IMessage> subMsgs = from st in t.Parameters select TermToMessage(st, knownNames);
        return t.IsTuple ? new TupleMessage(subMsgs) : new FunctionMessage(t.Name, new(subMsgs));
    }

    private static IMessage VarOrNameMessage(string term, HashSet<string> knownNames)
    {
        return knownNames.Contains(term) ? new NameMessage(term) : new VariableMessage(term);
    }

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
