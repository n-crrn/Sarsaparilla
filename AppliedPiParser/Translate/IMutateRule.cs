using StatefulHorn;

namespace AppliedPi.Translate;

public interface IMutateRule
{

    public Rule GenerateRule(RuleFactory factory);

}
