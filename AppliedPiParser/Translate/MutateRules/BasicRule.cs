using System.Collections.Generic;
using System.Linq;

using StatefulHorn;

namespace AppliedPi.Translate.MutateRules;

/// <summary>
/// This rule allows for pass through from the translation method.
/// </summary>
public class BasicRule : IMutateRule
{

    public BasicRule(HashSet<Event> premises, IMessage result, string lbl = "")
    {
        Premises = new HashSet<Event>(premises);
        Result = Event.Know(result);
        Label = lbl;
    }

    public IReadOnlySet<Event> Premises { get; init; }

    public Event Result { get; init; }

    #region IMutateRules implementation.

    public string Label { get; init; }

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        factory.SetNextLabel(Label);
        factory.RegisterPremises(Premises.ToArray());
        factory.GuardStatements = Conditions?.CreateGuard();
        return IfBranchConditions.ApplyReplacements(Conditions, factory.CreateStateConsistentRule(Result));
    }

    public int RecommendedDepth => 0;

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
