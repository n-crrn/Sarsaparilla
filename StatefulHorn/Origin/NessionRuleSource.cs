using System.Collections.Generic;

namespace StatefulHorn.Origin;

public class NessionRuleSource : IRuleSource
{

    public NessionRuleSource(Nession n, int frameIndex, List<StateTransferringRule> leadup, Rule rule)
    {
        Run = n;
        FrameIndex = frameIndex;
        StateTransfers = leadup;
        OriginalRule = rule;
    }

    public Nession Run { get; init; }

    public int FrameIndex { get; init; }

    public List<StateTransferringRule> StateTransfers { get; init; }

    public Rule OriginalRule { get; init; }

    public string Describe()
    {
        if (StateTransfers.Count == 0)
        {
            return $"From initial nession frame, rule {OriginalRule}";
        }
        else
        {
            string msg = $"From rule at nession frame {FrameIndex}, rule being {OriginalRule}, leadup to frame:\n  ";
            return msg + string.Join("\n  ", StateTransfers);
        }
    }

    public List<IRuleSource> Dependencies => new();

}
