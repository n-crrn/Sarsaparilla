using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatefulHorn;

public abstract class Rule
{
    protected Rule(string lbl, Guard g, HashSet<Event> prems, SnapshotTree ss)
    {
        GuardStatements = g;
        Label = lbl;
        Snapshots = ss;
        _Premises = prems;
    }

    protected void GenerateHashCode()
    {
        unchecked
        {
            // Prime numbers randomly selected.
            HashCode = 31;
            foreach (Event ev in _Premises)
            {
                HashCode = HashCode * 41 + ev.GetHashCode();
            }
            HashCode = HashCode * 41 + Result.GetHashCode();
        }
    }

    #region Common properties

    public Guard GuardStatements;

    public SnapshotTree Snapshots { get; init; }

    protected readonly HashSet<Event> _Premises;
    public IReadOnlySet<Event> Premises => _Premises;

    public abstract ISigmaUnifiable Result { get; }

    public bool IsStateless => Snapshots.IsEmpty;

    public bool HasPremiseVariables => (from p in _Premises where p.IsKnow && p.ContainsVariables select p).Any();
    
    public HashSet<IMessage> PremiseVariables
    {
        get
        {
            HashSet<IMessage> variables = new();
            foreach (Event prem in _Premises)
            {
                if (prem.EventType == Event.Type.Know)
                {
                    prem.Messages[0].CollectVariables(variables);
                }
            }
            return variables;
        }
    }

    public IEnumerable<Event> NonceDeclarations => from p in _Premises where p.EventType == Event.Type.New select p;

    /// <summary>
    /// Go through the premises, and determine which nonces are used by this rule that are not
    /// declared by the rule.
    /// </summary>
    /// <returns>Nonces that need to be declared before this rule can be used.</returns>
    public IEnumerable<NonceMessage> NoncesRequired
    {
        get
        {
            HashSet<IMessage> foundNonces = new();
            foreach (Event ev in (from p in _Premises where p.IsKnow select p))
            {
                ev.Messages.Single().CollectMessages(foundNonces, (IMessage msg) => msg is NonceMessage);
            }
            HashSet<IMessage> declaredNonces = new(from nd in NonceDeclarations select nd.Messages.Single());
            foundNonces.ExceptWith(declaredNonces);
            foreach (IMessage msg in foundNonces)
            {
                yield return (NonceMessage)msg;
            }
        }
    }

    #endregion

    // The following tuple is of form Snapshot, Trace Index, Offset Index with Trace.
    protected List<(Snapshot, int, int)>? DetermineSnapshotCorrespondencesWith(Rule other, Guard g, SigmaFactory sf)
    {
        List<(Snapshot, int, int)>? overallCorres = new();

        foreach (Snapshot ss in Snapshots.Traces)
        {
            bool found = false;
            for (int otherTraceI = 0; otherTraceI < other.Snapshots.Traces.Count; otherTraceI++)
            {
                Snapshot otherTrace = other.Snapshots.Traces[otherTraceI];
                List<(Snapshot, int, int)>? corres = IsTraceUnifiableWith(ss, otherTrace, otherTraceI, g, sf);
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

    private static List<(Snapshot, int, int)>? IsTraceUnifiableWith(Snapshot ss1, Snapshot ss2, int traceId, Guard g, SigmaFactory sf)
    {
        List<(Snapshot, int, int)>? matches = null;
        if (ss1.Condition.Name == ss2.Condition.Name && ss1.Condition.CanBeUnifiableWith(ss2.Condition, g, sf))
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
                if (prior1.O == prior2.O && prior1.S.Condition.CanBeUnifiableWith(prior2.S.Condition, g, sf))
                {
                    matches.Add(new(prior1.S, traceId, offset));
                    prior1 = prior1.S.Prior;
                    prior2 = prior2.S.Prior;
                }
                else if (prior1.O == Snapshot.Ordering.LaterThan && prior2.O == Snapshot.Ordering.ModifiedOnceAfter)
                {
                    if (prior1.S.Condition.CanBeUnifiableWith(prior2.S.Condition, g, sf))
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
               _Premises.Count == otherRule._Premises.Count &&
               _Premises.SetEquals(otherRule._Premises) &&
               Snapshots.Equals(otherRule.Snapshots) &&
               GuardStatements.Equals(otherRule.GuardStatements);
    }

    private int HashCode = 0;

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
    /// <param name="substitutions">The list of substitutions that need to be conducted on the result.</param>
    /// <returns>A new rule with the same result as this one.</returns>
    public abstract Rule CreateDerivedRule(string label, Guard g, HashSet<Event> prems, SnapshotTree ss, SigmaMap substitutions);

    public Rule PerformSubstitution(SigmaMap sigma)
    {
        if (sigma.IsEmpty)
        {
            return Clone();
        }

        string newLabel = $"{Label} · {sigma}";
        Guard newG = GuardStatements.PerformSubstitution(sigma);
        HashSet<Event> newPremises = new(Premises.Count);
        foreach (Event p in Premises)
        {
            newPremises.Add(p.PerformSubstitution(sigma));
        }
        SnapshotTree newTree = Snapshots.PerformSubstitutions(sigma);
        return CreateDerivedRule(newLabel, newG, newPremises, newTree, sigma);
    }

    public Rule SubscriptVariables(string subscript)
    {
        HashSet<IMessage> oldVars = PremiseVariables;
        foreach (Snapshot ss in Snapshots.OrderedList)
        {
            oldVars.UnionWith(ss.Condition.Variables);
        }
        List<(IMessage Variable, IMessage Value)> newVars = new(from v in oldVars select (v, ScriptVariableMessage(v, subscript)));
        return PerformSubstitution(new SigmaMap(newVars));
    }

    private static IMessage ScriptVariableMessage(IMessage originalMsg, string subscript)
    {
        VariableMessage originalVar = (VariableMessage)originalMsg;
        string originalName = originalVar.Name;
        string newName = originalName.Contains('_') ? $"{originalName}-{subscript}" : $"{originalName}_{subscript}";
        return new VariableMessage(newName);
    }

    /// <summary>
    /// A "facts substitution" is initially used by the query engine to create the initial rules.
    /// It means that every premise substituted can be removed as it is assumed from the ruleset.
    /// This helps to reduce the number of rule compositions that need to be conducted at later
    /// stages in the elaboration.
    /// </summary>
    /// <param name="map">Substitutions to make.</param>
    /// <returns>A new rule with substituted premises removed from the rule.</returns>
    public Rule PerformFactsSubstitution(SigmaMap sigma)
    {
        string newLabel = $"{Label} · {sigma}";
        Guard newG = GuardStatements.PerformSubstitution(sigma);
        HashSet<Event> newPremises = new();
        foreach (Event p in Premises)
        {
            Event updatedPremise = p.PerformSubstitution(sigma);
            if (updatedPremise.Equals(p) || updatedPremise.ContainsVariables)
            {
                // The premise was not affected by the substitution, so will not be removed for
                // being a fact.
                newPremises.Add(updatedPremise);
            }
        }
        SnapshotTree newTree = Snapshots.PerformSubstitutions(sigma);
        return CreateDerivedRule(newLabel, newG, newPremises, newTree, sigma);
    }

    public Rule Clone()
    {
        return CreateDerivedRule(Label, GuardStatements, new(Premises), Snapshots.CloneTree(), SigmaMap.Empty);
    }

    #endregion
    #region Filtering

    public virtual bool ContainsMessage(IMessage msg)
    {
        return
            _Premises.Any((Event ev) => ev.ContainsMessage(msg)) ||
            //null != _Premises.Find((Event ev) => ev.ContainsMessage(msg)) ||
            Snapshots.ContainsMessage(msg) ||
            ResultContainsMessage(msg);
    }

    protected abstract bool ResultContainsMessage(IMessage msg);

    public virtual bool ContainsEvent(Event ev)
    {
        return _Premises.Contains(ev) || ResultContainsEvent(ev);
    }

    protected abstract bool ResultContainsEvent(Event ev);

    public virtual bool ContainsState(State st)
    {
        return Snapshots.ContainsState(st) || ResultContainsState(st);
    }

    protected abstract bool ResultContainsState(State st);

    #endregion
    #region Textual rule description

    public string Label { get; set; }

    protected string LinkedPremisesDescription
    {
        get
        {
            if (_Premises.Count == 0)
            {
                return ""; // If there are no premises, output an empty string.
            }

            List<Event> premiseList = _Premises.ToList();
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

    private string FreePremisesDescription => string.Join(", ", from p in _Premises select p.ToString());

    protected abstract string DescribeResult();

    private string? Description;

    public override string ToString() => Description ??= /*Label + " = " +*/ Describe();

    public string Describe()
    {
        string gDesc = GuardStatements.IsEmpty ? "" : "[" + GuardStatements.ToString() + "] ";
        string ssDesc;
        string premisesDesc;
        if (Snapshots.IsEmpty)
        {
            ssDesc = "";
            premisesDesc = FreePremisesDescription;
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

    private static string NonEmptyJoiner(params string[] parts) => string.Join(" ", from p in parts where p.Length > 0 select p);

    #endregion
}
