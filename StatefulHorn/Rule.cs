using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatefulHorn;

public abstract class Rule
{
    protected Rule(string lbl, Guard g, List<Event> prems, SnapshotTree ss)
    {
        GuardStatements = g;
        Label = lbl;
        Snapshots = ss;
        _Premises = prems;
    }

    #region Common properties

    public Guard GuardStatements;

    public SnapshotTree Snapshots { get; init; }

    protected readonly List<Event> _Premises;
    public IReadOnlyList<Event> Premises => _Premises;

    public abstract ISigmaUnifiable Result { get; }

    public bool IsStateless => Snapshots.IsEmpty;

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        // This comparison ignores the label - we just want to know if they *mean* precisely
        // the same thing.
        return obj is Rule otherRule &&
               GuardStatements.Equals(otherRule.GuardStatements) &&
               PremisesEqual(otherRule._Premises) &&
               Result.Equals(otherRule.Result) &&
               Snapshots.Equals(otherRule.Snapshots);
    }

    private bool PremisesEqual(IEnumerable<Event> other)
    {
        bool missed = false;
        foreach (Event ev in other)
        {
            bool found = false;
            foreach (Event premiseEv in _Premises)
            {
                if (premiseEv.Equals(ev))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                missed = true;
                break;
            }
        }
        return !missed;
    }

    public override int GetHashCode() => Result.GetHashCode();

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
    public abstract Rule CreateDerivedRule(string label, Guard g, List<Event> prems, SnapshotTree ss, SigmaMap substitutions);

    public Rule PerformSubstitution(SigmaMap sigma)
    {
        if (sigma.IsEmpty)
        {
            return Clone();
        }

        string newLabel = $"{Label} · {sigma}";
        Guard newG = GuardStatements.PerformSubstitution(sigma);
        List<Event> newPremises = new(Premises);
        foreach (Event p in Premises)
        {
            newPremises.Add(p.PerformSubstitution(sigma));
        }
        SnapshotTree newTree = Snapshots.PerformSubstitutions(sigma);
        return CreateDerivedRule(newLabel, newG, newPremises, newTree, sigma);
    }

    public Rule Clone()
    {
        return CreateDerivedRule(Label, GuardStatements, new(Premises), Snapshots.CloneTree(), SigmaMap.Empty);
    }

    public bool CanImply(Rule other, out SigmaMap? sigma)
    {
        if (_Premises.Count > other._Premises.Count)
        {
            // This rule simply cannot imply the other.
            sigma = null;
            return false;
        }

        // Can we substitute for the result?
        Guard combinedGuard = GuardStatements.UnionWith(other.GuardStatements);
        SigmaFactory possReplacements = new(false);
        if (Result.CanBeUnifiedTo(other.Result, combinedGuard, possReplacements))
        {
            List<ISigmaUnifiable> premsStates = new(_Premises);
            premsStates.AddRange(Snapshots.States);

            List<ISigmaUnifiable> otherPremsStates = new(other._Premises);
            otherPremsStates.AddRange(other.Snapshots.States);

            if (UnifyUtils.IsUnifiedToSubset(premsStates, otherPremsStates, combinedGuard, possReplacements))
            {
                sigma = possReplacements.CreateForwardMap();
                bool finalCheck = Snapshots.CanImply(other.Snapshots, sigma);
                if (!finalCheck)
                {
                    sigma = null;
                }
                return finalCheck;
            } 
        }
        sigma = null;
        return false;
    }

    #endregion
    #region Filtering

    public virtual bool ContainsMessage(IMessage msg)
    {
        return 
            null != _Premises.Find((Event ev) => ev.ContainsMessage(msg)) ||
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

            List<string> premiseStrings = new();
            for (int i = 0; i < _Premises.Count; i++)
            {
                string cNum = "(" + (i + 1) + ")";
                premiseStrings.Add($"{_Premises[i]}{cNum}");
            }

            List<string> premiseOrdering = new();
            IReadOnlyList<Snapshot> orderedSS = Snapshots.OrderedList;
            foreach (Snapshot ss in orderedSS)
            {
                foreach (Event ev in ss.AssociatedPremises)
                {
                    int idx = _Premises.IndexOf(ev);
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

    public override string ToString() => Description ??= Label + " = " + Describe();

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
