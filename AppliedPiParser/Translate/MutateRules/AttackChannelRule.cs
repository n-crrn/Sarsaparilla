using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate.MutateRules;

public class AttackChannelRule : IMutateRule
{

    public AttackChannelRule(string readVarName)
    {
        VariableName = readVarName;
    }

    public string VariableName { get; init; }

    public static IEnumerable<IMutateRule> GenerateRulesForReceivePattern(
        List<(string, string)> rxPattern,
        IfBranchConditions conditions)
    {
        return from rx in rxPattern select new AttackChannelRule(rx.Item1) { Conditions = conditions };
    }

    #region IMutateRule implementation.

    public string Label => $"Attack:{VariableName}";

    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    public Rule GenerateRule(RuleFactory factory)
    {
        factory.GuardStatements = Conditions?.CreateGuard();
        factory.RegisterPremise(Event.Know(new VariableMessage(VariableName)));
        Rule r = factory.CreateStateConsistentRule(FiniteReadRule.VariableCellAsPremise(VariableName));
        return IfBranchConditions.ApplyReplacements(Conditions, r);
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => $"Attack read rule to variable {VariableName}";

    public override bool Equals(object? obj)
    {
        return obj is AttackChannelRule acr && VariableName == acr.VariableName;
    }

    public override int GetHashCode() => VariableName.GetHashCode();

    #endregion

}
