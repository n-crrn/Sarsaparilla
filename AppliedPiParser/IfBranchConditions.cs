using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AppliedPi.Translate;
using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi;

/// <summary>
/// A class for reasoning about the conditions for a set of If-Then-Else branches.
/// </summary>
public class IfBranchConditions
{

    public IfBranchConditions() : this(new(), new()) { }

    private IfBranchConditions(
        Dictionary<IAssignableMessage, IMessage> repl,
        BucketSet<IAssignableMessage, IMessage> ban)
    {
        Replacements = repl;
        Bans = ban;
    }

    public IfBranchConditions(IfBranchConditions ifc)
    {
        Replacements = new(ifc.Replacements);
        Bans = new(ifc.Bans);
    }

    private readonly Dictionary<IAssignableMessage, IMessage> Replacements;

    private readonly BucketSet<IAssignableMessage, IMessage> Bans;

    public static IfBranchConditions IfThen(GroupSet<IMessage> msgSets) => new(GroupToReplacements(msgSets), new());

    public static IfBranchConditions Else(GroupSet<IMessage> msgSets) => new(new(), GroupToBans(msgSets));

    public bool IsEmpty => Replacements.Count == 0 && Bans.Count == 0;

    public void AndWith(IfBranchConditions ibc)
    {
        foreach ((IAssignableMessage aMsg, IMessage vMsg) in ibc.Replacements)
        {
            // Check that the given replacement conditions are compatible with the current 
            // replacements.
            if (Replacements.TryGetValue(aMsg, out IMessage? replaceMsg))
            {
                if (!vMsg.Equals(replaceMsg))
                {
                    throw new InvalidComparisonException(aMsg, "Cannot be set to unequal values simultaneously.");
                }
            }

            // Check that the given replacement conditions do not contradict a ban value.
            // The check is not a unified-to check as there may be variable cross-references.
            if (Bans.ContainsKey(aMsg) && Bans[aMsg].Contains(vMsg))
            {
                throw new InvalidComparisonException(aMsg, $"Value not allowed as replacement as value banned.");
            }
        }

        // Check that the existing replacements aren't contradicted by a given ban value.
        foreach ((IAssignableMessage thisAMsg, IMessage thisVMsg) in Replacements)
        {
            if (ibc.Bans.ContainsKey(thisAMsg) && ibc.Bans[thisAMsg].Contains(thisVMsg))
            {
                throw new InvalidComparisonException(thisAMsg, $"Value not allowed as value banned by other conditions.");
            }
        }

        // Combine everything.
        foreach ((IAssignableMessage otherAMsg, IMessage otherVMsg) in ibc.Replacements)
        {
            Replacements[otherAMsg] = otherVMsg;
        }
        Bans.UnionWith(ibc.Bans);
    }

    private static Dictionary<IAssignableMessage, IMessage> GroupToReplacements(GroupSet<IMessage> msgSets)
    {
        // Extract each group to a variable and its matches. It doesn't 
        // matter which variable is selected in a group, as long as
        // everything in the group points to the one it is fine.
        Dictionary<IAssignableMessage, IMessage> replacements = new();
        foreach (IReadOnlySet<IMessage> g in msgSets)
        {
            IMessage? anchor = null;
            foreach (IMessage msg in g)
            {
                if (msg is not IAssignableMessage)
                {
                    anchor = msg;
                    break;
                }
            }
            if (anchor != null)
            {
                foreach (IMessage msg in g)
                {
                    if (!anchor.Equals(msg) && msg is IAssignableMessage aMsg)
                    {
                        replacements[aMsg] = anchor;
                    }
                }
            }
            else
            {
                IEnumerator<IMessage> iter = g.GetEnumerator();
                iter.MoveNext();
                IMessage first = iter.Current;
                while (iter.MoveNext())
                {
                    replacements[(IAssignableMessage)iter.Current] = first;
                }
            }
        }
        return replacements;
    }

    private static (List<IAssignableMessage>, List<IMessage>) SplitOutAssignables(IEnumerable<IMessage> allMsgs)
    {
        List<IAssignableMessage> assignable = new();
        List<IMessage> assigned = new();
        foreach (IMessage msg in allMsgs)
        {
            if (msg is IAssignableMessage aMsg)
            {
                assignable.Add(aMsg);
            }
            else
            {
                assigned.Add(msg);
            }
        }
        return (assignable, assigned);
    }

    private static BucketSet<IAssignableMessage, IMessage> GroupToBans(GroupSet<IMessage> msgSets)
    {
        BucketSet<IAssignableMessage, IMessage> bans = new();
        foreach (IReadOnlySet<IMessage> g in msgSets)
        {
            // Split the group into assignables and "assigneds".
            (List<IAssignableMessage> assignable, List<IMessage> assigned) = SplitOutAssignables(g);
            if (assignable.Count == 0)
            {
                continue;
            }

            // Ensure all other assignables are pointing at the first assignable.
            for (int i = 0; i < assignable.Count; i++)
            {
                for (int j = 0; j < assignable.Count; j++)
                {
                    if (i != j)
                    {
                        bans.Add(assignable[i], assignable[j]);
                    }    
                }
                foreach (IMessage toMsg in assigned)
                {
                    bans.Add(assignable[i], toMsg);
                }
            }
        }
        return bans;
    }

    public SigmaMap CreateSigmaMap()
    {
        List<(IMessage Variable, IMessage Value)> sigRepl = new();
        foreach ((IAssignableMessage aMsg, IMessage vMsg) in Replacements)
        {
            sigRepl.Add((aMsg, vMsg));
        }
        return new(sigRepl);
    }

    public Guard CreateGuard() => new(Bans.Buckets);

}
