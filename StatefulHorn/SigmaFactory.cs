using StatefulHorn.Messages;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// An object used to generate SigmaMaps while ensuring that the mapping will be consistent with
/// the previous ones added. This class is distinct from SigmaMap as, when computing unifications,
/// the variables in each rule have to be treated distinctly in spite of their names and so you
/// typically end up with two SigmaMaps: one transforming one rule, and the other transforming
/// the other.
/// </summary>
public class SigmaFactory
{
    public SigmaFactory(bool bothDirection = true)
    {
        Forward = new();
        Backward = new();
        BothWays = bothDirection;
    }

    private readonly bool BothWays;

    private readonly Dictionary<VariableMessage, IMessage> Forward;

    private readonly Dictionary<VariableMessage, IMessage> Backward;

    #region SigmaMap generation

    public SigmaMap CreateForwardMap()
    {
        return new(new List<(IMessage Variable, IMessage Value)>(from f in Forward select ((IMessage)f.Key, f.Value)));
    }

    public SigmaMap CreateBackwardMap()
    {
        return new(new List<(IMessage Variable, IMessage Value)>(from b in Backward select ((IMessage)b.Key, b.Value)));
    }

    public SigmaMap? TryCreateMergeMap()
    {
        Dictionary<VariableMessage, IMessage> merged = new(Forward);
        foreach ((VariableMessage key, IMessage value) in Backward)
        {
            if (merged.TryGetValue(key, out IMessage? checkValue))
            {
                if (!value.Equals(checkValue))
                {
                    return null;
                }
            }
            else
            {
                merged[key] = value;
            }
        }
        return new(new List<(IMessage Variable, IMessage Value)>(from m in merged select ((IMessage)m.Key, m.Value)));
    }

    #endregion
    #region Adding new substitutions

    private static bool ContainsContradictingValue(Dictionary<VariableMessage, IMessage> oneWay, Dictionary<VariableMessage, IMessage> otherWay, VariableMessage vMsg, IMessage sub)
    {
        if (oneWay.TryGetValue(vMsg, out IMessage? existing))
        {
            if(!sub.Equals(existing))
            {
                return true;
            }
        }

        if (otherWay.ContainsValue(vMsg))
        {
            return true;
        }

        // Need to check vMsg is not within otherWay.Value.
        HashSet<IMessage> heldVariables = new();
        foreach (IMessage heldValue in otherWay.Values)
        {
            heldValue.CollectVariables(heldVariables);
        }
        if (heldVariables.Contains(vMsg))
        {
            return true;
        }

        HashSet<IMessage> subVariables = new();
        sub.CollectVariables(subVariables);
        foreach (IMessage varValue in subVariables)
        {
            if (otherWay.ContainsKey((VariableMessage)varValue))
            {
                return true;
            }
            if (oneWay.ContainsValue((VariableMessage)varValue))
            {
                return true;
            }
        }
        return false;
    }

    public bool TryAdd(IMessage msg1, IMessage msg2)
    {
        Debug.Assert(msg1 is VariableMessage || msg2 is VariableMessage, "One of the messages must be a variable.");
        bool wasAdded = false;

        if (msg1 is VariableMessage vMsg1 &&
            !ContainsContradictingValue(Forward, Backward, vMsg1, msg2))
        {
            Forward[vMsg1] = msg2;
            wasAdded = true;
        }
        else if (BothWays && msg2 is VariableMessage vMsg2 &&
                 !ContainsContradictingValue(Backward, Forward, vMsg2, msg1))
        {
            Backward[vMsg2] = msg1;
            wasAdded = true;
        }
        
        return wasAdded;
    }

    #endregion
    #region Multi-message correspondence testing.

    public bool CanUnifyMessagesOneWay(List<IMessage> list1, List<IMessage> list2, Guard g)
    {
        if (list1.Count != list2.Count)
        {
            return false;
        }
        for (int i = 0; i < list1.Count; i++)
        {
            if (!list1[i].DetermineUnifiedToSubstitution(list2[i], g, this))
            {
                return false;
            }
        }
        return true;
    }

    public bool CanUnifyMessagesBothWays(List<IMessage> list1, List<IMessage> list2, Guard g)
    {
        if (list1.Count != list2.Count)
        {
            return false;
        }
        for (int i = 0; i < list1.Count; i++)
        {
            if (!list1[i].DetermineUnifiableSubstitution(list2[i], g, this))
            {
                return false;
            }
            // A copy of the guard needs to be updated to protect var1 =/= var2 situations.
            SigmaMap sm = new(list1[i], list2[i]);
            g = g.PerformSubstitution(sm);
        }
        return true;
    }

    #endregion
    #region Guard checks.

    private static bool IsValidByGuard(Dictionary<VariableMessage, IMessage> dir, Guard g)
    {
        foreach ((VariableMessage vMsg, IMessage otherMsg) in dir)
        {
            if (!g.CanUnifyMessages(vMsg, otherMsg))
            {
                return false;
            }
            if (g.Ununified.TryGetValue(vMsg, out HashSet<IMessage>? fullBanSet))
            {
                IEnumerable<VariableMessage> crossRef = from m in fullBanSet where m is VariableMessage select (VariableMessage)m;
                foreach (VariableMessage nextVMsg in crossRef)
                {
                    // Double check that the two variables are not set to the same replacement.
                    if (dir.TryGetValue(nextVMsg, out IMessage? nextValue))
                    {
                        if (otherMsg.Equals(nextValue))
                        {
                            return false;
                        }
                    }
                }
            }
        }
        return true;
    }

    public bool ForwardIsValidByGuard(Guard g) => IsValidByGuard(Forward, g);

    public bool BackwardIsValidByGuard(Guard g) => IsValidByGuard(Backward, g);

    #endregion

}
