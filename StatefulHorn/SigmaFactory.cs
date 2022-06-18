using StatefulHorn.Messages;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// An object used to generate SigmaMaps while ensuring that the mapping will be consistent with
/// the previous ones added. This class is distinct from SigmaMap as, when computing unifications,
/// the variables in each rule have to be treated distinctly in spite of their names and so you
/// typically end up with two SigmaMaps: one transforming one rule forward, and the other
/// transforming the other backwards.
/// </summary>
public class SigmaFactory
{
    public SigmaFactory(bool bothDirection = true)
    {
        Forward = new();
        Backward = new();
        BothWays = bothDirection;
    }

    public SigmaFactory(SigmaFactory toCopy)
    {
        Forward = new(toCopy.Forward);
        Backward = new(toCopy.Backward);
        BothWays = toCopy.BothWays;
    }

    private readonly bool BothWays;

    private readonly Dictionary<VariableMessage, IMessage> Forward;

    private readonly Dictionary<VariableMessage, IMessage> Backward;

    public bool IsEmpty => Forward.Count == 0 && Backward.Count == 0;

    public bool NotBackward => Backward.Count == 0;

    #region SigmaMap generation

    public SigmaMap CreateForwardMap()
    {
        if (Forward.Count == 0)
        {
            return SigmaMap.Empty;
        }
        return new(Forward);
    }

    public SigmaMap CreateBackwardMap()
    {
        if (Backward.Count == 0)
        {
            return SigmaMap.Empty;
        }
        return new(Backward);
    }

    public SigmaMap? TryCreateMergeMap()
    {
        if (Forward.Count == 0 && Backward.Count == 0)
        {
            return SigmaMap.Empty;
        }

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
        return new(merged);
    }

    /// <summary>
    /// Occasionally, a set of substitutions will be created that lead to 'dangling variables' 
    /// that eclipse variables in the broader context. This method allows those variables to 
    /// be moved out of the way before application to a HornClause.
    /// </summary>
    /// <param name="sm">A set of substitutions.</param>
    public void ForwardSubstitute(SigmaMap sm)
    {
        List<VariableMessage> keys = new(Forward.Keys);
        foreach (VariableMessage vm in keys)
        {
            Forward[vm] = Forward[vm].PerformSubstitution(sm);
        }
    }

    #endregion
    #region Adding new substitutions

    private static void InsertAndSettle(
        Dictionary<VariableMessage, IMessage> aheadSubs,
        Dictionary<VariableMessage, IMessage> reverseSubs, 
        VariableMessage newVar, 
        IMessage result)
    {
        result = result.PerformSubstitution(new(reverseSubs));

        List<IMessage> varList = new(reverseSubs.Keys);
        SigmaMap thisReplacement = new(newVar, result);
        foreach (VariableMessage varItem in varList)
        {
            reverseSubs[varItem] = reverseSubs[varItem].PerformSubstitution(thisReplacement);
        }

        aheadSubs[newVar] = result;
    }

    public bool TryAdd(IMessage msg1, IMessage msg2)
    {
        Debug.Assert(msg1 is VariableMessage || msg2 is VariableMessage, "One of the messages must be a variable.");
        bool acceptable = false;

        if (msg1 is VariableMessage vMsg1)
        {
            if (!Forward.TryGetValue(vMsg1, out IMessage? repl1))
            {
                InsertAndSettle(Forward, Backward, vMsg1, msg2);
                acceptable = true;
            } 
            else
            {
                acceptable = msg2.Equals(repl1);
            }
        }
        else if (BothWays && msg2 is VariableMessage vMsg2)
        {
            if (!Backward.TryGetValue(vMsg2, out IMessage? repl2))
            {
                InsertAndSettle(Backward, Forward, vMsg2, msg1);
                acceptable = true;
            }
            else
            {
                acceptable = msg1.Equals(repl2);
            }
        }
        
        return acceptable;
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

    public bool CanUnifyMessagesBothWays(List<IMessage> list1, List<IMessage> list2, Guard fwdGuard, Guard bwdGuard)
    {
        if (list1.Count != list2.Count)
        {
            return false;
        }
        for (int i = 0; i < list1.Count; i++)
        {
            if (!list1[i].DetermineUnifiableSubstitution(list2[i], fwdGuard, bwdGuard, this))
            {
                return false;
            }
            SigmaMap fwdSm = new(list1[i], list2[i]);
            fwdGuard = fwdGuard.Substitute(fwdSm);
            SigmaMap bwdSm = new(list2[i], list1[i]);
            bwdGuard = bwdGuard.Substitute(bwdSm);
        }
        return true;
    }

    #endregion
    #region Guard checks.

    public bool ForwardIsValidByGuard(Guard g) => g.CanUnifyAllMessages(Forward);

    public bool BackwardIsValidByGuard(Guard g) => g.CanUnifyAllMessages(Backward);

    public bool AnyContradictionsWithState(IDictionary<IMessage, IMessage?> stateVariables)
    {
        foreach ((VariableMessage vm, IMessage sm) in Forward.Concat(Backward))
        {
            if (stateVariables.TryGetValue(vm, out IMessage? setValue) && setValue != null && !Equals(sm, setValue))
            {
                return true;
            }
        }
        return false;
    }

    public Dictionary<IMessage, IMessage?> UpdateStateReplacements(Dictionary<IMessage, IMessage?> stateVariables)
    {
        foreach ((VariableMessage vm, IMessage sm) in Forward.Concat(Backward))
        {
            if (stateVariables.ContainsKey(vm))
            {
                stateVariables[vm] = sm;
            }
        }
        return stateVariables;
    }

    #endregion
    #region Basic object overrides.

    public override string ToString()
    {
        if (IsEmpty)
        {
            return "Factory <EMPTY>";
        }
        return "Factory Fwd: " + CreateForwardMap().ToString() + " Bwd: " + CreateBackwardMap();
    }

    public override bool Equals(object? obj)
    {
        return obj is SigmaFactory sf
            && Forward.ToHashSet().SetEquals(sf.Forward) 
            && Backward.ToHashSet().SetEquals(sf.Backward);
    }

    public override int GetHashCode() => Forward.Count * 7 + Backward.Count;

    #endregion

}
