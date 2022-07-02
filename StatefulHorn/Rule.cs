using StatefulHorn.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatefulHorn;

/// <summary>
/// The base class for both types of Stateful Horn Clauses providing common functionality.
/// </summary>
public abstract class Rule
{

    /// <summary>
    /// Initialises the base members of the Rule class. This constructor is protected as Stateful
    /// Horn Clauses do not make sense without their results.
    /// </summary>
    /// <param name="lbl">User provided description of the rule.</param>
    /// <param name="g">Guard to be applied to the rule.</param>
    /// <param name="prems">Premises required to satisfy rule.</param>
    /// <param name="ss">Applicable tree of snapshots required to satisfy clause.</param>
    protected Rule(string lbl, Guard g, HashSet<Event> prems, SnapshotTree ss)
    {
        Label = lbl;
        Snapshots = ss;
        Premises = prems;
        Guard = g.IsEmpty ? g : g.Filter(CollectAllVariables());
    }

    #region Properties.

    /// <summary>
    /// A user provided description attached to the rule.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// What variables in this rule cannot match.
    /// </summary>
    public Guard Guard { get; protected set; }

    /// <summary>
    /// The applicable tree of snapshots required to satisfy the clause.
    /// </summary>
    public SnapshotTree Snapshots { get; init; }

    /// <summary>
    /// The premises required for the rule to be applicable. It is assumed that individual
    /// Snapshots have been associated with individual premises as part of the 
    /// creation of that Snapshots prior to rule initialisation.
    /// </summary>
    public IReadOnlySet<Event> Premises { get; private set; }

    /// <summary>
    /// The result of applying the rule to a system.
    /// </summary>
    /// <remarks>
    /// This member is abstract as the defining difference between State Consistent Rules and 
    /// State Transferring Rules is the nature of the result.
    /// </remarks>
    public abstract ISigmaUnifiable Result { get; }

    /// <summary>
    /// A description of how the user specified the rule to the system. This may take the
    /// form of a textual description of a Stateful Horn Clause, or it may be another
    /// form that is translated to a Stateful Horn Clause.
    /// </summary>
    public UserDefinition? Definition { get; set; }

    /// <summary>
    /// True if this rule does not rely on state for its application.
    /// </summary>
    public bool IsStateless => Snapshots.IsEmpty;

    /// <summary>
    /// Return the list of variables used within the premises of this rule. Note that this is not
    /// the full list of variables within the rule, as there may be variables used within the
    /// snapshots.
    /// </summary>
    public HashSet<IMessage> PremiseVariables
    {
        get
        {
            HashSet<IMessage> variables = new();
            foreach (Event prem in Premises)
            {
                if (prem.EventType == Event.Type.Know)
                {
                    prem.Message.CollectVariables(variables);
                }
            }
            return variables;
        }
    }

    /// <summary>
    /// The list of nonces declared in the premises of this rule.
    /// </summary>
    public IEnumerable<Event> NonceDeclarations => from p in Premises where p.EventType == Event.Type.New select p;

    /// <summary>
    /// Go through the premises, and determine which nonces are used by this rule that are not
    /// declared by the rule.
    /// </summary>
    public IEnumerable<NonceMessage> NoncesRequired
    {
        get
        {
            HashSet<IMessage> foundNonces = new();
            foreach (Event ev in Premises)
            {
                if (ev.IsKnow)
                {
                    ev.Message.CollectMessages(foundNonces, (IMessage msg) => msg is NonceMessage);
                }
            }
            HashSet<IMessage> declaredNonces = new(from nd in NonceDeclarations select nd.Message);
            foundNonces.ExceptWith(declaredNonces);
            foreach (IMessage msg in foundNonces)
            {
                yield return (NonceMessage)msg;
            }
        }
    }

    #endregion

    /// <summary>
    /// This method supports the Li et al 2017 paper's algorithms for composition and state
    /// transformation. It determines which snapshots in this rule correspond with the 
    /// snapshots in other.
    /// </summary>
    /// <param name="other">Rule to compare with.</param>
    /// <param name="sf">SigmaFactory for assessing and storing substitutions.</param>
    /// <returns>
    /// A list of tuples of the form: (1) Snapshot; (2) Which trace the snapshot is part of;
    /// and (3) The offset of the snapshot from the head of the trace. If null, then no
    /// correspondence was found.
    /// </returns> 
    protected List<(Snapshot, int, int)>? DetermineSnapshotCorrespondencesWith(
        Rule other,
        SigmaFactory sf)
    {
        List<(Snapshot, int, int)>? overallCorres = new();

        foreach (Snapshot ss in Snapshots.Traces)
        {
            bool found = false;
            for (int otherTraceI = 0; otherTraceI < other.Snapshots.Traces.Count; otherTraceI++)
            {
                Snapshot otherTrace = other.Snapshots.Traces[otherTraceI];
                List<(Snapshot, int, int)>? corres = IsTraceUnifiableWith(
                    ss, 
                    otherTrace, 
                    otherTraceI, 
                    Guard, 
                    other.Guard, 
                    sf);
                if (corres != null)
                {
                    found = true;
                    overallCorres.Add((ss, otherTraceI, 0));
                    overallCorres.AddRange(corres);
                    break;
                }
            }
            if (!found)
            {
                overallCorres = null;
                break;
            }
        }

        return overallCorres;
    }

    /// <summary>
    /// Determine if the two "next" snapshot list can be unified based on history in their 
    /// respective trees. This method supports the Li et al 2017 paper's algorithms for 
    /// composition and state transformation.
    /// </summary>
    /// <param name="ss1">The first starsnapshot to consider.</param>
    /// <param name="ss2">The second snapshot to consider.</param>
    /// <param name="traceId">
    /// Which trace is being investigated, used for setting the return tuple.
    /// </param>
    /// <param name="fwdGuard">The guard statements associated with the first snapshot.</param>
    /// <param name="bwdGuard">The guard statements associated with the second snapshot.</param>
    /// <param name="sf">The SigmaFactory used for substitution deconfliction and storage.</param>
    /// <returns>
    /// A list of tuples of the form: (1) Snapshot; (2) Which trace the snapshot is part of;
    /// and (3) The offset of the snapshot from the head of the trace. If null, then no
    /// correspondence was found.
    /// </returns>
    private static List<(Snapshot, int, int)>? IsTraceUnifiableWith(
        Snapshot ss1, 
        Snapshot ss2, 
        int traceId, 
        Guard fwdGuard, 
        Guard bwdGuard, 
        SigmaFactory sf)
    {
        List<(Snapshot, int, int)>? matches = null;
        if (ss1.Condition.Name == ss2.Condition.Name &&
            ss1.Condition.CanBeUnifiableWith(ss2.Condition, fwdGuard, bwdGuard, sf))
        {
            matches = new();
            Snapshot.PriorLink? prior1 = ss1.Prior;
            Snapshot.PriorLink? prior2 = ss2.Prior;
            int offset = 1;
            while (prior1 != null)
            {
                if (prior2 == null)
                {
                    return null;
                }
                if (prior1.O == prior2.O && 
                    prior1.S.Condition.CanBeUnifiableWith(prior2.S.Condition, fwdGuard, bwdGuard, sf))
                {
                    matches.Add(new(prior1.S, traceId, offset));
                    prior1 = prior1.S.Prior;
                    prior2 = prior2.S.Prior;
                }
                else if (prior1.O == Snapshot.Ordering.LaterThan && prior2.O == Snapshot.Ordering.ModifiedOnceAfter)
                {
                    if (prior1.S.Condition.CanBeUnifiableWith(prior2.S.Condition, fwdGuard, bwdGuard, sf))
                    {
                        matches.Add(new(prior1.S, traceId, offset));
                        prior1 = prior1.S.Prior;
                        prior2 = prior2.S.Prior;
                    }
                    else
                    {
                        prior2 = prior2.S.Prior;
                    }
                }
                offset++;
            }
        }
        return matches;
    }

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        // This comparison ignores the label - we just want to know if they *mean* precisely
        // the same thing.
        return obj is Rule otherRule &&
               Result.Equals(otherRule.Result) &&
               Premises.Count == otherRule.Premises.Count &&
               Premises.SetEquals(otherRule.Premises) &&
               Snapshots.Equals(otherRule.Snapshots) &&
               Guard.Equals(otherRule.Guard);
    }

    /// <summary>
    /// Cached calculated hash code for the rule.
    /// </summary>
    private int HashCode = 0;

    /// <summary>
    /// Creates a hash code based on the rule's premises and result. Rules are commonly placed in 
    /// HashSets in order to remove duplicates, so it is calculated once and cached in member
    /// HashCode.
    /// </summary>
    protected void GenerateHashCode()
    {
        unchecked
        {
            // Prime numbers 31 and 41 selected at random.
            HashCode = 31;
            foreach (Event ev in Premises)
            {
                HashCode = HashCode * 41 + ev.GetHashCode();
            }
            HashCode = HashCode * 41 + Result.GetHashCode();
        }
    }

    public override int GetHashCode() => HashCode;

    #endregion
    #region Rule operations

    /// <summary>
    /// Creates a new rule with the same result as this rule, but with non-right-hand
    /// elements replaced. This method is used by StateConsistentRule.TryComposeWith,
    /// .PerformSubstitution and .Clone in order to create newly composed rules with the correct
    /// type.
    /// </summary>
    /// <param name="label">Label for the new rule.</param>
    /// <param name="g">Combined guard statement for the new rule.</param>
    /// <param name="prems">Premises for the new rule.</param>
    /// <param name="ss">Snapshot tree of the new rule.</param>
    /// <param name="substitutions">
    /// The list of substitutions that need to be conducted on the result.
    /// </param>
    /// <returns>A new rule with the same result as this one.</returns>
    public abstract Rule CreateDerivedRule(
        string label, 
        Guard g, 
        HashSet<Event> prems, 
        SnapshotTree ss, 
        SigmaMap substitutions);

    /// <summary>
    /// Replace variables within the rule with the values specified in the given SigmaMap.
    /// </summary>
    /// <param name="sigma">
    /// List of valid substitutions. May include variables not used within this rule.
    /// </param>
    /// <returns>
    /// A new rule with the substitutions performed.
    /// </returns>
    public Rule Substitute(SigmaMap sigma)
    {
        if (sigma.IsEmpty)
        {
            return this;
        }

        string newLabel = $"{Label} · {sigma}";
        Guard newG = Guard.Substitute(sigma);

        SnapshotTree newTree = Snapshots.PerformSubstitutions(sigma);
        HashSet<Event> newPremises = new(Premises.Count);
        foreach (Event p in Premises)
        {
            newPremises.Add(p.Substitute(sigma));
        }
        return CreateDerivedRule(newLabel, newG, newPremises, newTree, sigma);
    }

    /// <summary>
    /// Return a set containing all variables defined within the Rule's premises and snapshots.
    /// </summary>
    /// <returns>Set of variable messages.</returns>
    private HashSet<IMessage> CollectAllVariables()
    {
        HashSet<IMessage> oldVars = PremiseVariables;
        foreach (Snapshot ss in Snapshots.OrderedList)
        {
            oldVars.UnionWith(ss.Condition.Variables);
        }
        return oldVars;
    }

    /// <summary>
    /// When adding a rule to a nession, there needs to be a way to consider variables with the
    /// same name differently. To prevent namespace collisions, variables are renamed with a 
    /// "subscript" to retain understandability yet specify that they are unique to the rule.
    /// </summary>
    /// <param name="subscript">The text to apply after the variable name.</param>
    /// <returns>A new rule with variables replaced by subscripted ones.</returns>
    public Rule SubscriptVariables(string subscript)
    {
        HashSet<IMessage> oldVars = CollectAllVariables();
        List<(IMessage Variable, IMessage Value)> newVars = 
            new(from v in oldVars 
                select (v, MessageUtils.SubscriptVariableMessage(v, subscript)));
        return Substitute(new SigmaMap(newVars));
    }

    /// <summary>
    /// Create a new copy of the rule.
    /// </summary>
    /// <returns>New rule with premises and snapshots also fully duplicated.</returns>
    public Rule Clone()
    {
        return CreateDerivedRule(Label, Guard, new(Premises), Snapshots.CloneTree(), SigmaMap.Empty);
    }

    #endregion
    #region Filtering

    /// <summary>Searches the clause result for the given message.</summary>
    /// <param name="msg">Message to find.</param>
    /// <returns>True if the message is found in the result.</returns>
    protected abstract bool ResultContainsMessage(IMessage msg);

    /// <summary>Searches the clause result for the given event.</summary>
    /// <param name="ev">Event to find.</param>
    /// <returns>True if the event is present in the result.</returns>
    protected abstract bool ResultContainsEvent(Event ev);

    /// <summary>Searches the clause result for the given state.</summary>
    /// <param name="st">State to find.</param>
    /// <returns>True if the state is present in the result.</returns>
    protected abstract bool ResultContainsState(State st);

    /// <summary>
    /// Searches the rule for any mention of the given message. Note that the Guard is not
    /// searched.
    /// </summary>
    /// <param name="msg">Message to find.</param>
    /// <returns>True if found in either the premises, snapshots or result.</returns>
    public bool ContainsMessage(IMessage msg)
    {
        return
            Premises.Any((Event ev) => ev.ContainsMessage(msg)) ||
            Snapshots.ContainsMessage(msg) ||
            ResultContainsMessage(msg);
    }

    /// <summary>
    /// Searches the premise and result for the given message.
    /// </summary>
    /// <param name="ev">Event to find.</param>
    /// <returns>True if the event appears in the premise or result.</returns>
    public bool ContainsEvent(Event ev)
    {
        return Premises.Contains(ev) || ResultContainsEvent(ev);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="st"></param>
    /// <returns></returns>
    public virtual bool ContainsState(State st)
    {
        return Snapshots.ContainsState(st) || ResultContainsState(st);
    }

    #endregion
    #region Textual rule description

    /// <summary>
    /// Provide the textual description of the premises, when the premises are linked to snapshots.
    /// </summary>
    protected string LinkedPremisesDescription
    {
        get
        {
            if (Premises.Count == 0)
            {
                return ""; // If there are no premises, output an empty string.
            }

            List<Event> premiseList = Premises.ToList();
            List<string> premiseStrings = new();
            for (int i = 0; i < premiseList.Count; i++)
            {
                string cNum = "(" + (i + 1) + ")";
                premiseStrings.Add($"{premiseList[i]}{cNum}");
            }

            List<string> premiseOrdering = new();
            IReadOnlyList<Snapshot> orderedSS = Snapshots.OrderedList;
            foreach (Snapshot ss in orderedSS)
            {
                foreach (Event ev in ss.Premises)
                {
                    int idx = premiseList.IndexOf(ev);
                    if (idx < 0)
                    {
                        string dbgDesc = "Debug description of rule: " + DebugDescription();
                        throw new InvalidOperationException($"Cannot describe premises - snapshot with invalid event {ev}. {dbgDesc}");
                    }
                    string premiseRef = "(" + (idx + 1) + ")"; /* CircledNumber(idx + 1); */
                    premiseOrdering.Add($"{premiseRef} :: {ss.Label}");
                }
            }

            return string.Join(", ", premiseStrings) + " : {" + string.Join(", ", premiseOrdering) + "}";
        }
    }

    /// <summary>
    /// Provides a last-ditch attempt to describe a rule, without attempting to be consistent.
    /// </summary>
    /// <returns>String description of rule that is suitable for programmer consideration.</returns>
    protected string DebugDescription()
    {
        StringBuilder buffer = new();
        buffer.Append("LABEL: ");
        buffer.Append(Label);
        buffer.Append("PREMISES: {").Append(string.Join(", ", Premises)).Append("}, ");
        buffer.Append("SNAPSHOTS: {").Append(string.Join(", ", Snapshots.OrderedList)).Append('}');
        buffer.Append("RESULT: {").Append(Result).Append(" }");
        return buffer.ToString();
    }

    /// <summary>
    /// Abstract method providing the textual description of the result of a clause.
    /// </summary>
    /// <returns>Text description of the clause result.</returns>
    protected abstract string DescribeResult();

    /// <summary>
    /// Cached value of the string description, as used by ToString().
    /// </summary>
    private string? Description;

    public override string ToString() => Description ??= Describe();

    /// <summary>
    /// Generates the textual description of the rule, which is cached for usage by ToString();
    /// </summary>
    /// <returns>String description of the rule.</returns>
    public string Describe()
    {
        string gDesc = Guard.IsEmpty ? "" : "[" + Guard.ToString() + "] ";
        string ssDesc;
        string premisesDesc;
        if (Snapshots.IsEmpty)
        {
            ssDesc = "";
            premisesDesc = string.Join(", ", from p in Premises select p.ToString());
        }
        else
        {
            // Snapshots.ToString be called to ensure snapshots are labelled before
            // LinkedPremisesDescription is called.
            ssDesc = Snapshots.ToString();
            premisesDesc = LinkedPremisesDescription;
        }
        string resultDesc = DescribeResult();
        return NonEmptyJoiner(gDesc, premisesDesc, "-[", ssDesc, "]->", resultDesc);
    }

    /// <summary>
    /// Support method for Describe(), allowing strings to be joined with only single spaces.
    /// </summary>
    /// <param name="parts">List of strings to join.</param>
    /// <returns>Single connected string without consecutive spaces.</returns>
    private static string NonEmptyJoiner(params string[] parts)
    {
        return string.Join(" ", from p in parts where p.Length > 0 select p);
    }

    #endregion

}
