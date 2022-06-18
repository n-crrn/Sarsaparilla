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
    /// <summary>
    /// Create an empty SigmaFactory.
    /// </summary>
    public SigmaFactory()
    {
        Forward = new();
        Backward = new();
    }

    /// <summary>
    /// Create a new SigmaFactory that is a copy of another SigmaFactory.
    /// </summary>
    /// <param name="toCopy">The SigmaFactory to copy.</param>
    public SigmaFactory(SigmaFactory toCopy)
    {
        Forward = new(toCopy.Forward);
        Backward = new(toCopy.Backward);
    }

    /// <summary>
    /// The "forward" direction mapping. In terms of method parameter orders, it is the mapping
    /// that converts messages from the first message toward the second message.
    /// </summary>
    private readonly Dictionary<VariableMessage, IMessage> Forward;

    /// <summary>
    /// The "backward" direction mapping. In terms of method parameter order, it is the mapping
    /// that coverts message from the second message toward the first message.
    /// </summary>
    private readonly Dictionary<VariableMessage, IMessage> Backward;

    /// <summary>
    /// True if there are have been no replacements found so far.
    /// </summary>
    public bool IsEmpty => Forward.Count == 0 && Backward.Count == 0;

    /// <summary>
    /// True if there are no replacements from second message parameters to first message
    /// parameters. Knowing this allows for shortcutting some operations.
    /// </summary>
    public bool NotBackward => Backward.Count == 0;

    #region SigmaMap generation

    /// <summary>
    /// Retrieves or creates a SigmaMap that can perform the forward replacements.
    /// </summary>
    /// <returns>SigmaMap to perform forward replacements.</returns>
    public SigmaMap CreateForwardMap()
    {
        if (Forward.Count == 0)
        {
            return SigmaMap.Empty;
        }
        return new(Forward);
    }

    /// <summary>
    /// Retrieves or creates a SigmaMap that can perform the backwards replacements.
    /// </summary>
    /// <returns>SigmaMap to perform backwards replacements.</returns>
    public SigmaMap CreateBackwardMap()
    {
        if (Backward.Count == 0)
        {
            return SigmaMap.Empty;
        }
        return new(Backward);
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

    /// <summary>
    /// Performs the insertion of a new msg1 to msg2 conversion. The parameters for aheadSubs and
    /// reverseSubs will be either the Forward or Backward of the SigmaFactory; which is which
    /// will depend on the direction of replacement. The point of this method is to ensure the
    /// consistency of an inserted variable, because the substitution of newVar with result
    /// must be reflected in the other messages as well as explicitly within Forward or Backward.
    /// This method is an auxiliary method to TryAdd(IMessage, IMessage).
    /// </summary>
    /// <param name="aheadSubs">
    /// The substitution dictionary in the direction of newVar to result.
    /// </param>
    /// <param name="reverseSubs">
    /// The substitution dictionary in the reverse direction to aheadSubs.
    /// </param>
    /// <param name="newVar">Variable message to be replaced.</param>
    /// <param name="result">Value message to replace the variable message with.</param>
    private static void InsertAndSettle(
        Dictionary<VariableMessage, IMessage> aheadSubs,
        Dictionary<VariableMessage, IMessage> reverseSubs, 
        VariableMessage newVar, 
        IMessage result)
    {
        result = result.PerformSubstitution(new(reverseSubs));

        List<IMessage> varList = new(reverseSubs.Keys);
        SigmaMap thisReplacement = new(newVar, result);
        for (int i = 0; i < varList.Count; i++)
        {
            VariableMessage varItem = (VariableMessage)varList[i];
            reverseSubs[varItem] = reverseSubs[varItem].PerformSubstitution(thisReplacement);
        }

        aheadSubs[newVar] = result;
    }

    /// <summary>
    /// Attempt to add a substitution from msg1 to or from msg2, with the directionality of 
    /// substitution depending on the BothWays property of the SigmaFactory. If BothWays is false,
    /// then msg1 must come to equal msg2 in the substitution; otherwise, both messages should
    /// have a substitution generated that allows them to come to an equivalent message. The 
    /// substitution will not be added if it contradicts one that was previously added, and false
    /// will be returned in that case.
    /// </summary>
    /// <param name="msg1">The forward message.</param>
    /// <param name="msg2">The backward message.</param>
    /// <param name="bothWays">
    /// Whether the substitution should only be from msg1 to msg2 ("unified to"), or msg1 and msg2
    /// to a common value ("unifiable").
    /// </param>
    /// <returns>
    /// True if the substitution is accepted as not contradicting existing substitutions.
    /// </returns>
    public bool TryAdd(IMessage msg1, IMessage msg2, bool bothWays)
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
        else if (bothWays && msg2 is VariableMessage vMsg2)
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

    /// <summary>
    /// Determine if a substitution exists that allows all messages in list1 to be converted to
    /// their same-indexed members in list2. This method will update this instance of 
    /// SigmaFactory as individual messages are checked. As a result, if false is returned, then
    /// the SigmaFactory should be considered invalidated.
    /// </summary>
    /// <param name="list1">The forward messages.</param>
    /// <param name="list2">The backward messages.</param>
    /// <param name="g">The guard for the forward messages.</param>
    /// <returns>
    /// True if a set of substitutions exists that convert the members in list1 to list.2
    /// </returns>
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

    /// <summary>
    /// Determine if two lists of messages can be unifiable to a third list of messages. This
    /// method will update this instance of SigmaFactory as individual messages are checked.
    /// As a result, if false is returned, then the SigmaFactory should be considered 
    /// invalidated.
    /// </summary>
    /// <param name="list1">The forward messages.</param>
    /// <param name="list2">The backward messages.</param>
    /// <param name="fwdGuard">The guard for the forward messages.</param>
    /// <param name="bwdGuard">The guard for the backward messages.</param>
    /// <returns></returns>
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

    /// <summary>
    /// Check that the currently held forward substitutions comply with the given guard.
    /// </summary>
    /// <param name="g">Guard to check.</param>
    /// <returns>True if the guard does not contradict the substitutions.</returns>
    public bool ForwardIsValidByGuard(Guard g) => g.CanUnifyAllMessages(Forward);

    /// <summary>
    /// Check that the currently held backward substitutions comply with the given guard.
    /// </summary>
    /// <param name="g">Guard to check</param>
    /// <returns>True if the guard does not contradict the substitutions.</returns>
    public bool BackwardIsValidByGuard(Guard g) => g.CanUnifyAllMessages(Backward);

    #endregion
    #region State variable operations.

    /// <summary>
    /// Determine if there are any contradictions between the substitutions held in this
    /// SigmaFactory, and the substitutions in the given dictionary where the dictionary does not
    /// indicate null for that message. This operation is used to ensure that query results 
    /// are consistent within a system, using the dictionary as the store of variables that held 
    /// in the states of the relevant nession.
    /// </summary>
    /// <param name="stateVariables">
    /// Dictionary of state variable replacements. Where the value message is set to null, the
    /// value message has not been set.
    /// </param>
    /// <returns>
    /// True if a contradiction exists in the substitutions held within this factory (Forward or
    /// Backward).
    /// </returns>
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

    /// <summary>
    /// Update a dictionary of state variable substitutions with any substitutions of state
    /// variables held within this factory, regardless of whether it is a forward transformation
    /// or a backward transformation.
    /// </summary>
    /// <param name="stateVariables">
    /// Dictionary of state variable replacements. Where the value message is set to null, the
    /// value message has not been set. This dictionary is modified by this method.
    /// </param>
    /// <returns>
    /// The stateVariables dictionary. This return is provided as a convenience.
    /// </returns>
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
