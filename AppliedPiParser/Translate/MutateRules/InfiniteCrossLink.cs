using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteCrossLink : IMutateRule
{

    public InfiniteCrossLink(
        WriteSocket fromSocket, 
        ReadSocket toSocket, 
        IDictionary<Socket, int> finActionCounts,
        HashSet<Event> premises, 
        IMessage sent,
        string varName)
    {
        From = fromSocket;
        To = toSocket;
        FiniteActionCounts = finActionCounts;
        Premises = premises;
        Result = Event.Know(new FunctionMessage($"{varName}@cell", new() { sent }));
        Debug.Assert(From.IsInfinite && To.IsInfinite);
    }

    public WriteSocket From { get; init; }

    public ReadSocket To { get; init; }

    public IDictionary<Socket, int> FiniteActionCounts { get; init; }

    public HashSet<Event> Premises { get; init; }

    public Event Result { get; init; }

    public static IEnumerable<IMutateRule> GenerateRulesForReadReceivePatterns(
        WriteSocket from,
        ReadSocket to,
        IDictionary<Socket, int> finActionCounts,
        HashSet<Event> premises,
        IMessage written)
    {
        foreach (List<(string, string)> rxPattern in to.ReceivePatterns)
        {
            // Does the pattern match the given term?
            if (written is TupleMessage tm)
            {
                if (tm.Members.Count == rxPattern.Count)
                {
                    foreach ((string varName, string _) in rxPattern)
                    {
                        yield return new InfiniteCrossLink(from, to, finActionCounts, premises, written, varName);
                    }
                }
            }
            else
            {
                if (rxPattern.Count == 1)
                {
                    yield return new InfiniteCrossLink(from, to, finActionCounts, premises, written, rxPattern[0].Item1);
                }
            }
        }
    }

    #region IMutateRule implementation.

    public string Label => $"InfXLink:{From}-{To}({Result})";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        foreach ((Socket s, int ic) in FiniteActionCounts)
        {
            s.RegisterHistory(factory, ic);
        }
        Snapshot fromWait = factory.RegisterState(From.WaitingState());
        factory.RegisterPremises(fromWait, Premises);
        factory.RegisterState(From.WaitingState());
        factory.RegisterState(To.WaitingState());
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateConsistentRule(Result));
    }

    public int RecommendedDepth => 0;

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Infinite cross-link between {From} and {To}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteCrossLink icl &&
            From.Equals(icl.From) &&
            To.Equals(icl.To) &&
            FiniteActionCounts.ToHashSet().SetEquals(icl.FiniteActionCounts) &&
            Premises.SetEquals(icl.Premises) &&
            Result.Equals(icl.Result) &&
            Equals(Conditions, icl.Conditions);
    }

    public override int GetHashCode() => From.GetHashCode() + To.GetHashCode();

    #endregion

}
