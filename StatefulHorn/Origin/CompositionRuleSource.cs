using System.Collections.Generic;

namespace StatefulHorn.Origin;

public class CompositionRuleSource : IRuleSource
{

    public CompositionRuleSource(HornClause hc1, HornClause hc2)
    {
        Composer = hc1;
        ComposedUpon = hc2;
    }

    public HornClause Composer { get; init; }

    public HornClause ComposedUpon { get; init; }

    public string Describe() => $"Composition of {Composer} upon {ComposedUpon}";

    public List<IRuleSource> Dependencies
    {
        get
        {
            List<IRuleSource> srcList = new();
            if (Composer.Source != null)
            {
                srcList.Add(Composer.Source);
            }
            if (ComposedUpon.Source != null)
            {
                srcList.Add(ComposedUpon.Source);
            }
            return srcList;
        }
    }
        

}
