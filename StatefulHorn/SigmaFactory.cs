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
    public SigmaFactory()
    {
        Forward = new();
        Backward = new();
    }

    public SigmaFactory(IMessage from, IMessage to) : this()
    {
        TryAdd(from, to);
    }

    private readonly Dictionary<VariableMessage, IMessage> Forward;

    private readonly Dictionary<VariableMessage, IMessage> Backward;

    #region SigmaMap generation

    public SigmaMap CreateFowardMap()
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

    private static bool ContainsContradictingValue(Dictionary<VariableMessage, IMessage> d, VariableMessage vMsg, IMessage sub)
    {
        if (d.TryGetValue(vMsg, out IMessage? existing))
        {
            return !sub.Equals(existing);
        }
        return false;
    }

    public bool CanAdd(IMessage msg1, IMessage msg2)
    {
        Debug.Assert(msg1 is VariableMessage || msg2 is VariableMessage, "One of the messages must be a variable.");
        if (msg1 is VariableMessage vMsg1 && ContainsContradictingValue(Forward, vMsg1, msg2))
        {
            return false;
        }
        else if (msg2 is VariableMessage vMsg2 && ContainsContradictingValue(Backward, vMsg2, msg1))
        {
            return false;
        }
        return true;
    }

    public bool CanAdd(List<(IMessage, IMessage)> newSubs)
    {
        foreach ((IMessage, IMessage) sub in newSubs)
        {
            if (!CanAdd(sub.Item1, sub.Item2))
            {
                return false;
            }
        }
        return true;
    }

    public bool TryAdd(IMessage msg1, IMessage msg2)
    {
        Debug.Assert(msg1 is VariableMessage || msg2 is VariableMessage, "One of the messages must be a variable.");
        if (msg1 is VariableMessage vMsg1 && !ContainsContradictingValue(Forward, vMsg1, msg2))
        {
            Forward[vMsg1] = msg2;
            return true;
        }
        else if (msg2 is VariableMessage vMsg2 && !ContainsContradictingValue(Backward, vMsg2, msg1))
        {
            Backward[vMsg2] = msg1;
            return true;
        }
        return false;
    }

    public bool TryAdd(IEnumerable<(IMessage, IMessage)>? newSubs)
    {
        if (newSubs == null)
        {
            return false;
        }
        // Check if can add.
        List<(IMessage, IMessage)> accepted = new();
        foreach ((IMessage, IMessage) sub in newSubs)
        {
            Debug.Assert(sub.Item1 is VariableMessage || sub.Item2 is VariableMessage, "One of the messages must be a variable.");
            if (!CanAdd(sub.Item1, sub.Item2))
            {
                return false;
            }
            accepted.Add(sub);
        }
        // Just add them all.
        foreach ((IMessage, IMessage) sub in newSubs)
        {
            if (sub.Item1 is VariableMessage vMsg1)
            {
                Forward[vMsg1] = sub.Item2;
            }
            else if (sub.Item2 is VariableMessage vMsg2)
            {
                Backward[vMsg2] = sub.Item1;
            }
        }
        return true;
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
        }
        return true;
    }

    #endregion

}
