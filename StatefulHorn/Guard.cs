using System.Collections.Generic;
using System.Linq;

using StatefulHorn.Messages;

namespace StatefulHorn;

/// <summary>
/// Guards are applied to rules to indicate what values variables within the rule cannot take.
/// In the broader context of Stateful Horn Clauses, these Guards provide the means by which
/// conditional statements can be enforced within a system. Instances of this class are
/// intended to be immutable.
/// </summary>
public class Guard
{
    /// <summary>
    /// Most rules in most systems will not have a guard applied. To save creating many, many 
    /// identical empty Guards, this field exists in the same way that string.Empty does.
    /// </summary>
    public static readonly Guard Empty = new();

    #region Constructors.

    /// <summary>
    /// The empty constructor. This is private to enforce the use of Guard.Empty where possible.
    /// </summary>
    private Guard()
    {
        Ununified = new Dictionary<IAssignableMessage, HashSet<IMessage>>();
    }

    /// <summary>
    /// Create a new guard with only one entry. This constructor tends to be used for unit tests.
    /// </summary>
    /// <param name="aMsg">The assignable message.</param>
    /// <param name="vMsg">The banned value for the assignable message.</param>
    public Guard(IAssignableMessage aMsg, IMessage vMsg)
    {
        Ununified = new Dictionary<IAssignableMessage, HashSet<IMessage>>() 
        { 
            { aMsg, new() { vMsg } } 
        };
    }

    /// <summary>
    /// Create a new guard based on the provided dictionary, which associated assignable values
    /// with values that they cannot take.
    /// </summary>
    /// <param name="ununifedCombOrig">
    /// Dictionary indexed by assignable messages, pointing to sets of values that they cannot
    /// take.
    /// </param>
    public Guard(IReadOnlyDictionary<IAssignableMessage, HashSet<IMessage>> ununifedCombOrig)
    {
        Ununified = new Dictionary<IAssignableMessage, HashSet<IMessage>>(ununifedCombOrig);
    }

    /// <summary>
    /// A method for constructing a Guard based upon a set of tuples rather than a Dictionary. 
    /// This method is used by the RuleParser.
    /// </summary>
    /// <param name="ununifiedInput">
    /// Set of tuples of assignable messages with their banned values.
    /// </param>
    /// <returns>A Guard reflecting the bans.</returns>
    public static Guard CreateFromSets(HashSet<(IAssignableMessage, IMessage)> ununifiedInput)
    {
        if (ununifiedInput.Count == 0)
        {
            return Empty;
        }

        Dictionary<IAssignableMessage, HashSet<IMessage>> ununifiedComb = new();
        foreach ((IAssignableMessage, IMessage) row in ununifiedInput)
        {
            if (ununifiedComb.TryGetValue(row.Item1, out HashSet<IMessage>? values))
            {
                values.Add(row.Item2);
            }
            else
            {
                ununifiedComb[row.Item1] = new() { row.Item2 };
            }
        }
        return new(ununifiedComb);
    }

    #endregion
    #region Properties.

    /// <summary>
    /// The central dictionary of banned values.
    /// </summary>
    private IReadOnlyDictionary<IAssignableMessage, HashSet<IMessage>> Ununified { get; init; }

    /// <summary>
    /// True if there are no variable replacements blocked.
    /// </summary>
    public bool IsEmpty => Ununified.Count == 0;

    /// <summary>
    /// Cached calculated hash code.
    /// </summary>
    private int HashCode = 0;

    #endregion
    #region Message application.

    /// <summary>
    /// Determines if the given messages can be unified from msg1 to msg2 according to this Guard.
    /// </summary>
    /// <param name="msg1">The assignable message desired to be set.</param>
    /// <param name="msg2">The value to be set to.</param>
    /// <returns>True if </returns>
    public bool CanUnifyMessages(IAssignableMessage msg1, IMessage msg2)
    {
        if (Ununified.TryGetValue(msg1, out HashSet<IMessage>? bannedSet))
        {
            return !bannedSet.Contains(msg2);
        }
        return true;
    }

    /// <summary>
    /// Determine if the provided replacements are valid by the guard. This method will do an 
    /// "overall" check, checking cross-references (where one variable refers to another) to
    /// ensure correctness.
    /// </summary>
    /// <param name="subs">Dictionary of message substitutions.</param>
    /// <returns>True if the proposed substitutions comply with this guard.</returns>
    public bool CanUnifyAllMessages(IDictionary<VariableMessage, IMessage> subs)
    {
        foreach ((VariableMessage vMsg, IMessage otherMsg) in subs)
        {
            if (Ununified.TryGetValue(vMsg, out HashSet<IMessage>? bannedSet))
            {
                if (bannedSet.Contains(otherMsg))
                {
                    return false;
                }
                IEnumerable<VariableMessage> crossRef = from m in bannedSet
                                                        where m is VariableMessage
                                                        select (VariableMessage)m;
                foreach (VariableMessage vm in crossRef)
                {
                    if (subs.TryGetValue(vm, out IMessage? nextValue) && otherMsg.Equals(nextValue))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    #endregion
    #region Guard operations.

    /// <summary>
    /// Merge the entries of two Guards into a new third one. The assumption of this rule is that
    /// the variables involved at the end of the union will be the same variables in use as at the
    /// start.
    /// </summary>
    /// <param name="other">Guard to merge with.</param>
    /// <returns>A new guard containing restrictions from both Guards.</returns>
    public Guard Union(Guard other)
    {
        if (IsEmpty && other.IsEmpty)
        {
            return Guard.Empty;
        }
        Dictionary<IAssignableMessage, HashSet<IMessage>> comb = new();
        ImportUnunifiedList(comb, Ununified);
        ImportUnunifiedList(comb, other.Ununified);
        return new(comb);
    }

    /// <summary>
    /// Conducts the merging of two dictionaries of replacements. This is used within the 
    /// Union(...) method call.
    /// </summary>
    /// <param name="newDict">The dictionary that will be kept.</param>
    /// <param name="oldDict">The dictionary items are being sorced from.</param>
    private static void ImportUnunifiedList(
        IDictionary<IAssignableMessage, HashSet<IMessage>> newDict,
        IReadOnlyDictionary<IAssignableMessage, HashSet<IMessage>> oldDict)
    {
        foreach ((IAssignableMessage vMsg, HashSet<IMessage>? oldSet) in oldDict)
        {
            if (newDict.TryGetValue(vMsg, out HashSet<IMessage>? newSet))
            {
                newSet!.UnionWith(oldSet);
            }
            else
            {
                newDict[vMsg] = new(oldSet);
            }
        }
    }

    /// <summary>
    /// Return a Guard that only contains the assignables defined in the given set.
    /// </summary>
    /// <param name="varMsgs">Set of variable messages.</param>
    /// <returns>
    /// If the varMsgs are empty, return this Guard. Otherwise, return a new Guard where only
    /// the assignables in the set are retained.
    /// </returns>
    public Guard Filter(IReadOnlySet<IMessage> varMsgs)
    {
        if (varMsgs.Count == 0 || IsEmpty)
        {
            return Guard.Empty;
        }
        Dictionary<IAssignableMessage, HashSet<IMessage>> newDict = new();
        foreach (IMessage vMsg in varMsgs)
        {
            if (vMsg is IAssignableMessage aMsg 
                && Ununified.TryGetValue(aMsg, out HashSet<IMessage>? collection))
            {
                newDict[aMsg] = collection;
            }
        }
        return new(newDict);
    }

    /// <summary>
    /// Return a guard where the variable mentions within the Guard have been substituted in 
    /// accordance with the given SigmaMap. Where a left-hand-side variable is substituted, it is
    /// removed entirely from the Guard.
    /// </summary>
    /// <param name="sigma">The map of replacements.</param>
    /// <returns>
    /// If an empty SigmaMap is provided, then this Guard is returned. Otherwise, a new
    /// Guard object is created with the appropriate substitutions made.
    /// </returns>
    public Guard Substitute(SigmaMap sigma)
    {
        return Ununified.Count == 0 
            || sigma.IsEmpty ? this : new(SubstituteDictionary(Ununified, sigma));
    }

    /// <summary>
    /// Replace occurrences of variables in the dictionary input with values as specified by
    /// sigma. If a left-hand-side variable is replaced (i.e. instanced), then remove it 
    /// completely from the set.
    /// </summary>
    /// <param name="input">Dictionary with values to replace.</param>
    /// <param name="sigma">Sigma map.</param>
    /// <returns>
    /// A new dictionary with variables replaced as specified.
    /// </returns>
    private static Dictionary<IAssignableMessage, HashSet<IMessage>> SubstituteDictionary(
        IReadOnlyDictionary<IAssignableMessage, HashSet<IMessage>> input, 
        SigmaMap sigma)
    {
        Dictionary<IAssignableMessage, HashSet<IMessage>> updated = new();
        foreach ((IAssignableMessage vMsg, HashSet<IMessage> set) in input)
        {
            IMessage possRepl = vMsg.Substitute(sigma);
            // Skip the value if it is now defined.
            if (possRepl is IAssignableMessage setMsg)
            {
                updated[setMsg] = new HashSet<IMessage>(from s in set select s.Substitute(sigma));
            }
        }
        return updated;
    }

    /// <summary>
    /// Executes the IMessage.CollectVariables(...) method on all messages held within the Guard.
    /// </summary>
    /// <param name="varSet">Set to collect the variables.</param>
    public void CollectVariables(HashSet<IMessage> varSet)
    {
        foreach ((IAssignableMessage assigner, HashSet<IMessage> banned) in Ununified)
        {
            assigner.CollectVariables(varSet);
            foreach (IMessage b in banned)
            {
                b.CollectVariables(varSet);
            }
        }
    }

    /// <summary>
    /// Expresses the central dictionary of terms as an enumeration of tuples, with each tuple
    /// containing an assignable value and a value that it cannot take.
    /// </summary>
    /// <returns>Guard as a list of assignable and banned value tuples.</returns>
    public IEnumerable<(IMessage, IMessage)> ToTuples()
    {
        foreach ((IAssignableMessage from, HashSet<IMessage> toSet) in Ununified)
        {
            foreach (IMessage to in toSet)
            {
                yield return (from, to);
            }
        }
    }

    #endregion
    #region Basic object overrides.

    public override string ToString()
    {
        if (Ununified.Count == 0)
        {
            return "<EMPTY>";
        }

        List<string> nonunif = new();
        foreach ((IAssignableMessage vMsg, HashSet<IMessage> set) in Ununified)
        {
            foreach (IMessage msg in set)
            {
                nonunif.Add($"{vMsg} ~/⤳ {msg}");
            }
        }
        return string.Join(", ", nonunif);
    }

    public override bool Equals(object? obj)
    {
        if (obj is Guard og)
        {
            if (Ununified.Count != og.Ununified.Count)
            {
                return false;
            }
            foreach ((IAssignableMessage thisVMsg, HashSet<IMessage> thisSet) in Ununified)
            {
                if (!og.Ununified.TryGetValue(thisVMsg, out HashSet<IMessage>? otherSet))
                {
                    return false;
                }
                if (!thisSet!.SetEquals(otherSet))
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

    public override int GetHashCode()
    {
        if (Ununified.Count == 0)
        {
            return 0;
        }
        if (HashCode == 0)
        {
            HashCode = 7727 * 7741;
            foreach ((IAssignableMessage vMsg, HashSet<IMessage> set) in Ununified)
            {
                unchecked
                {
                    HashCode = HashCode * 7741 + vMsg.GetHashCode();
                    foreach (IMessage m in set)
                    {
                        HashCode = HashCode * 7741 + m.GetHashCode();
                    }
                }
            }
        }
        return HashCode;
    }

    #endregion
}
