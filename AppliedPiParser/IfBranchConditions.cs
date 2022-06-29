using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AppliedPi.Translate;
using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi;

// Ignore "unnecessary" suppressions ... like the one that follows.
#pragma warning disable IDE0079
// Ignore that there is no Object.GetHashCode(), as it would be too computationally intensive.
#pragma warning disable CS0659 

/// <summary>
/// A class for reasoning about the conditions for a set of If-Then-Else branches.
/// </summary>
public class IfBranchConditions
{
    public static readonly IfBranchConditions Empty = new();

    public IfBranchConditions() : this(new(), new()) { }

    public IfBranchConditions(
        Dictionary<IAssignableMessage, IMessage> repl,
        BucketSet<IAssignableMessage, IMessage> ban)
    {
        Replacements = new(repl);
        Bans = new(ban);
    }

    public IfBranchConditions(IfBranchConditions ifc)
    {
        Replacements = new(ifc.Replacements);
        Bans = new(ifc.Bans);
    }

    private readonly Dictionary<IAssignableMessage, IMessage> Replacements;

    private readonly BucketSet<IAssignableMessage, IMessage> Bans;

    public bool IsEmpty => Replacements.Count == 0 && Bans.Count == 0;

    public static IfBranchConditions IfThen(GroupSet<IMessage> msgSets) => new(GroupToReplacements(msgSets), new());

    public static IfBranchConditions Else(GroupSet<IMessage> msgSets) => new(new(), GroupToBans(msgSets));

    public IfBranchConditions Not(IAssignableMessage aMsg, IMessage vMsg)
    {
        // Ensure that the banned value is not already in the replacement list.
        if (Replacements.TryGetValue(aMsg, out IMessage? replaceVMsg) && replaceVMsg.Equals(vMsg))
        {
            throw new InvalidOperationException($"Attempt to place guard {aMsg} ~/> {vMsg} when {aMsg} = {replaceVMsg}.");
        }
        IfBranchConditions cond = new(Replacements, Bans);
        cond.Bans.Add(aMsg, vMsg);
        return cond;
    }

    public IfBranchConditions And(IfBranchConditions other)
    {
        IfBranchConditions newBC = new(this);
        newBC.AndWith(other);
        return newBC;
    }

    private void AndWith(IfBranchConditions ibc)
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

    public static Rule ApplyReplacements(IfBranchConditions? cond, Rule r)
    {
        if (cond != null)
        {
            return r.Substitute(cond.CreateSigmaMap());
        }
        return r;
    }

    public Guard CreateGuard() => new(Bans.Buckets);

    #region Basic object overrides.

    public override bool Equals(object? other)
    {
        if (other is IfBranchConditions cond)
        {
            if (!Bans.Equals(cond.Bans))
            {
                return false;
            }
            if (Replacements.Count != cond.Replacements.Count)
            {
                return false;
            }
            foreach ((IAssignableMessage aMsg, IMessage msg) in Replacements)
            {
                if (!(cond.Replacements.TryGetValue(aMsg, out IMessage? otherMsg) && msg.Equals(otherMsg)))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    public override string ToString()
    {
        SigmaMap sm = CreateSigmaMap();
        Guard g = CreateGuard();
        return $"Replacements {sm}, bans {g}";
    }

    #endregion

}
