using StatefulHorn.Messages;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// Represents the σ mapping, which can be considered as a list of substitutions between
/// IMessages. A σ mapping is generated for UnifiedTo and UnificiableWith operations,
/// to allow new rules to be generated and reasoned about. Previously, the type
/// List<(IMessage Variable, IMessage Value)> was used in its place, but it became 
/// apparent that there was a need to do reasoning on the type.
/// </summary>
/// <para>
/// Note that experiments have been run trying to change the internal Map entry to 
/// different types of dictionary. No speed improvements were noted in the test suite when
/// doing this, and it is assessed that this is likely due to the normally small size of the 
/// mapping - most rules will only have one or two replacements.
/// </para>
public class SigmaMap
{
    /// <summary>
    /// A SigmaMap with no replacements, meant to be used in order to reduce object creations.
    /// </summary>
    public static readonly SigmaMap Empty = new();

    /// <summary>
    /// Private constructor for the use of SigmaMap.Empty.
    /// </summary>
    private SigmaMap()
    {
        Map = new List<(IMessage, IMessage)>(0);
    }

    /// <summary>
    /// Create a new SigmaMap with only one replacement. This occurrence is sufficiently common
    /// that it deserves its own constructor.
    /// </summary>
    /// <param name="variable">Message to be replaced.</param>
    /// <param name="val">Message it is to be replaced with.</param>
    public SigmaMap(IMessage variable, IMessage val)
    {
        Map = new List<(IMessage, IMessage)>(1) { new(variable, val) };
    }

    /// <summary>
    /// Create a SigmaMap from a dictionary, often used by SigmaFactory.
    /// </summary>
    /// <param name="zippedSubs">
    /// Dictionary with the terms to be replaced as the keys and the messages they are to be
    /// replaced with as the values.
    /// </param>
    public SigmaMap(IDictionary<VariableMessage, IMessage> zippedSubs)
    {
        List<(IMessage, IMessage)> m = new(zippedSubs.Count);
        foreach (KeyValuePair<VariableMessage, IMessage> pair in zippedSubs)
        {
            m.Add((pair.Key, pair.Value));
        }
        Map = m;
    }

    /// <summary>
    /// Create a SigmmMap from an enumeration of tuples. This tends to be used whenever a 
    /// sequence of message replacements needs to be done, and there should be no
    /// assumption made that the left-hand message is an IAssignableMessage.
    /// </summary>
    /// <param name="tuplePairs">
    /// Sequence of replacements, with the left-hand tuple member being the message to be
    /// replaced.
    /// </param>
    public SigmaMap(IEnumerable<(IMessage, IMessage)> tuplePairs)
    {
        List<(IMessage, IMessage)> m = new();
        foreach ((IMessage varMsg, IMessage valMsg) in tuplePairs)
        {
            m.Add((varMsg, valMsg));
        }
        Map = m;
    }

    #region Basic properties and access.

    /// <summary>
    /// A member allowing for random access of the replacement pairs.
    /// </summary>
    public IReadOnlyList<(IMessage Variable, IMessage Value)> Map;

    /// <summary>
    /// True if there are no replacements in this map.
    /// </summary>
    public bool IsEmpty => Map.Count == 0;

    /// <summary>
    /// Attempt to return a replacement value for the given variable.
    /// </summary>
    /// <param name="v">Variable message to search for.</param>
    /// <param name="value">Value message return parameter. Set to null if not found.</param>
    /// <returns>True if the variable is found.</returns>
    public bool TryGetValue(VariableMessage v, out IMessage? value)
    {
        // There are typically one or messages only.
        for (int i = 0; i < Map.Count; i++)
        {
            (IMessage vr, IMessage val) = Map[i];
            if (v.Equals(vr))
            {
                value = val;
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Cache for the variables contained within the replacement messages.
    /// </summary>
    private HashSet<IMessage>? _InsertedVariables = null;

    /// <summary>
    /// Variable messages that will be inserted if the SigmaMap is used for substitutions.
    /// </summary>
    public IReadOnlySet<IMessage> InsertedVariables
    {
        get
        {
            if (_InsertedVariables == null)
            {
                _InsertedVariables = new();
                for (int i = 0; i < Map.Count; i++)
                {
                    Map[i].Value.CollectVariables(_InsertedVariables);
                }
            }
            return _InsertedVariables;
        }
    }

    #endregion

    public override string ToString()
    {
        if (Map.Count == 0)
        {
            return "{EMPTY}";
        }
        IEnumerable<string> parts = from m in Map select $"{m.Variable} ↦ {m.Value}";
        return "{" + string.Join(", ", parts) + "}";
    }

    public override bool Equals(object? obj)
    {
        // Note that this method is rarely called outside of testing.
        return obj is SigmaMap sm && Map.Count == sm.Map.Count && Map.ToHashSet().SetEquals(sm.Map);
    }

    public override int GetHashCode() => Map.Count; // As there is no ordering.

}
