using StatefulHorn;

namespace AppliedPi.Translate;

public interface IMutateRule
{

    public string Label { get; }

    public IfBranchConditions Conditions { get; set; }

    public Rule GenerateRule(RuleFactory factory);

    public int RecommendedDepth { get; }

}
