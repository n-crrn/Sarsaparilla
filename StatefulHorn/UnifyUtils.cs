using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// Provides a namespace for algorithms that operate on ISigmaUnifiable items.
/// </summary>
public static class UnifyUtils
{

    public static bool IsUnifiedToSubset(
        List<ISigmaUnifiable> fromList,
        List<ISigmaUnifiable> toList,
        Guard g,
        SigmaFactory subFactory)
    {
        // Bucket the lists into constant and variable lists.
        BucketList(fromList, out HashSet<ISigmaUnifiable> fromConstSet, out List<ISigmaUnifiable> fromVarList);
        BucketList(toList, out HashSet<ISigmaUnifiable> toConstSet, out List<ISigmaUnifiable> toVarList);

        // Get rid of constants that don't match. As they are constants, there is no need to 
        // track substitutions.
        HashSet<ISigmaUnifiable> intersect = new(fromConstSet);
        intersect.IntersectWith(toConstSet);
        fromConstSet.ExceptWith(intersect);
        toConstSet.ExceptWith(intersect);

        // If there are any fromList constants left, then fromList is simply not a subset of toList.
        if (fromConstSet.Count > 0)
        {
            return false;
        }

        // Next priority is to match items that are variable in the fromList to constants in the toList.
        List<ISigmaUnifiable> toConstList = toConstSet.ToList();
        AttemptUnifiedToList(fromVarList, toConstList, g, subFactory);

        // Finally see if we can match variables to variables.
        AttemptUnifiedToList(fromVarList, toVarList, g, subFactory);

        // Have we achieved the aim of accounting for all items in fromList?
        return fromVarList.Count == 0;
    }

    private static void BucketList(List<ISigmaUnifiable> all, out HashSet<ISigmaUnifiable> constList, out List<ISigmaUnifiable> varList)
    {
        constList = new();
        varList = new();
        foreach(ISigmaUnifiable u in all)
        {
            if (u.ContainsVariables)
            {
                varList.Add(u);
            }
            else
            {
                _ = constList.Add(u);
            }
        }
    }

    private static void AttemptUnifiedToList(List<ISigmaUnifiable> fromList, List<ISigmaUnifiable> toList, Guard g, SigmaFactory fact)
    {
        for (int i = 0; i < fromList.Count; i++)
        {
            ISigmaUnifiable u = fromList[i];
            for (int j = 0; j < toList.Count; j++)
            {
                ISigmaUnifiable v = toList[j];
                if (u.CanBeUnifiedTo(v, g, fact))
                {
                    fromList.RemoveAt(i);
                    toList.RemoveAt(j);
                    i--;
                    break; // Force variable u to be refreshed.
                }
            }
        }
    }

}
