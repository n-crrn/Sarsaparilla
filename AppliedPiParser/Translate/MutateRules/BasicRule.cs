using System.Collections.Generic;
using System.Linq;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

/// <summary>
/// There are circumstances, such as those with the "ProVerif-style" rules, where only a Horn
/// Clause is required. A Basic Rule is used as a type of Mutate Rule where that Horn Clause
/// can be passed directly through to the Query Engine.
/// </summary>
public class BasicRule : MutateRule
{

    public BasicRule(HashSet<Event> premises, IMessage result, string lbl)
    {
        Premises = new HashSet<Event>(premises);
        Result = Event.Know(result);
        Label = lbl;
    }

    public IReadOnlySet<Event> Premises { get; init; }

    public Event Result { get; init; }

    #region IMutateRules implementation.

    public override Rule GenerateRule(RuleFactory factory)
    {
        factory.RegisterPremises(Premises.ToArray());
        return GenerateStateConsistentRule(factory, Result);
    }

    #endregion
    #region Basic object override.

    public override string ToString() => $"Basic: {Label}";

    public override bool Equals(object? obj)
    {
        return obj is BasicRule br &&
            Premises.SetEquals(br.Premises) &&
            Result.Equals(br.Result);
    }

    public override int GetHashCode() => Result.GetHashCode();

    #endregion

}
