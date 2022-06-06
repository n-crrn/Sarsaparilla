using System.Collections.Generic;

namespace StatefulHorn.Origin;

/// <summary>
/// Represents a rule source where there has been a single operation on a rule.
/// </summary>
public class OperationRuleSource : IRuleSource
{

    public enum Op
    {
        Anify,
        Detuple,
        Scrub
    };

    public OperationRuleSource(HornClause original, Op operation)
    {
        OriginalRule = original;
        Operation = operation;
    }

    public HornClause OriginalRule { get; init; }

    public Op Operation { get; init; }

    public string Describe() => $"Operation {Operation} upon {OriginalRule}";

    public List<IRuleSource> Dependencies
    {
        get
        {
            List<IRuleSource> srcList = new();
            if (OriginalRule.Source != null)
            {
                srcList.Add(OriginalRule.Source);
            }
            return srcList;
        }
    }

}
