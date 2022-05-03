using StatefulHorn;

namespace AppliedPi.Translate;

public interface IMutateRule
{

    public string Label { get; }

    public Rule GenerateRule(RuleFactory factory);

}
