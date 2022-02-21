using System.Collections.Generic;
using System.Diagnostics;

namespace StatefulHorn.Origin;

public class KnowledgeRuleSource : IRuleSource
{

    public KnowledgeRuleSource(StateConsistentRule scr)
    {
        Debug.Assert(scr.Snapshots.IsEmpty);
        Source = scr;
    }

    public StateConsistentRule Source { get; init; }

    public string Describe() => $"From knowledge rule {Source}";

    public List<IRuleSource> Dependencies => new();

}

