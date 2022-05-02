using System.Collections.Generic;
using System.Diagnostics;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

public class InfiniteCrossLink : IMutateRule
{

    public InfiniteCrossLink(WriteSocket fromSocket, ReadSocket toSocket, HashSet<Event> premises, Event result)
    {
        From = fromSocket;
        To = toSocket;
        Premises = premises;
        Result = result;
        Debug.Assert(From.IsInfinite && To.IsInfinite);
    }

    public WriteSocket From { get; init; }

    public ReadSocket To { get; init; }

    public HashSet<Event> Premises { get; init; }

    public Event Result { get; init; }

    public Rule GenerateRule(RuleFactory factory)
    {
        Snapshot fromWait = factory.RegisterState(From.WaitingState());
        factory.RegisterPremises(fromWait, Premises);
        factory.RegisterState(To.WaitingState());
        return factory.CreateStateConsistentRule(Result);
    }

    #region Basic object overrides.

    public override string ToString() => $"Infinite cross-link between {From} and {To}.";

    public override bool Equals(object? obj)
    {
        return obj is InfiniteCrossLink icl &&
            From.Equals(icl.From) &&
            To.Equals(icl.To) &&
            Premises.SetEquals(icl.Premises) &&
            Result.Equals(icl.Result);
    }

    public override int GetHashCode() => From.GetHashCode() + To.GetHashCode();

    #endregion

}
