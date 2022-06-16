using System.Collections.Generic;

namespace StatefulHorn.Query.Origin;

public class SubstitutionRuleSource : IRuleSource
{

    public SubstitutionRuleSource(HornClause original, SigmaMap subs)
    {
        OriginalRule = original;
        Substitution = subs;
    }

    public HornClause OriginalRule { get; init; }

    public SigmaMap Substitution { get; init; }

    public string Describe() => $"Substitution of {Substitution} upon {OriginalRule}";

    public List<IRuleSource> Dependencies => OriginalRule.Source != null ? new() { OriginalRule.Source } : new();

}
