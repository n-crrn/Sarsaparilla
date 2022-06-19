using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// This class stores information required to describe a rule, and is intended to make it easy to
/// use templating (e.g. Razor pages) to present rules to users.
/// </summary>
public class RuleDescription
{
    /// <summary>
    /// Generate a new rule description from the given rule.
    /// </summary>
    /// <param name="r">Rule to represent.</param>
    public RuleDescription(Rule r)
    {
        GuardStatements.AddRange(r.Guard.ToTuples());
        GetSnapshotsFromRule(r);
        GetPremisesFromRule(r);
        GetResultFromRule(r);
    }

    #region Guards

    /// <summary>
    /// The guard statement of the rule as a sequence of tuples.
    /// </summary>
    public List<(IMessage, IMessage)> GuardStatements { get; init; } = new();

    /// <summary>
    /// True if the rule has a guard to display.
    /// </summary>
    public bool HasGuard => GuardStatements.Count > 0;

    #endregion
    #region Snapshots

    /// <summary>
    /// The list of snapshot states to display. These are not just the heads of the traces.
    /// </summary>
    public List<Snapshot> Snapshots { get; init; } = new();

    /// <summary>
    /// A list of the required ordering statements to display. Each statement is presented as a
    /// tuple containing the first snapshot label, its ordering relationship and then the
    /// second snapshot label.
    /// </summary>
    public List<(string, Snapshot.Ordering, string)> OrderingStatements { get; init; } = new();

    /// <summary>
    /// Convenience method to generate OrderingStatements.
    /// </summary>
    /// <param name="r">The rule to generate the statements from.</param>
    private void GetSnapshotsFromRule(Rule r)
    {
        Snapshots.AddRange(r.Snapshots.OrderedList);
        for (int i = 0; i < Snapshots.Count; i++)
        {
            Snapshot ss = Snapshots[i];
            if (ss.Prior != null)
            {
                OrderingStatements.Add((ss.Prior.S.Label!, ss.Prior.O, ss.Label!));
            }
        }
    }

    /// <summary>
    /// True if there are snapshots to display.
    /// </summary>
    public bool HasSnapshots => Snapshots.Count > 0;

    /// <summary>
    /// True if there are snapshot orderings to display. This is separate to HasSnapshots as it
    /// is possible to have a collection of snapshots that aren't related to one another.
    /// </summary>
    public bool HasSnapshotOrderings => OrderingStatements.Count > 0;

    #endregion
    #region Premises

    /// <summary>
    /// A list of premise labels and their associated premise events.
    /// </summary>
    public SortedList<string, Event> Premises { get; init; } = new();

    /// <summary>
    /// The mapping of premise labels to their associated snapshots.
    /// </summary>
    public Dictionary<string, List<string>> PremiseSnapshotMapping { get; init; } = new();

    /// <summary>
    /// Generate the Premises and PremiseSnapshotMapping member variables from the given rule.
    /// This is called during object construction.
    /// </summary>
    /// <param name="r">Rule to extract the premise details of.</param>
    private void GetPremisesFromRule(Rule r)
    {
        List<Event> rulePremises = r.Premises.ToList();
        for (int i = 0; i < rulePremises.Count; i++)
        {
            Event prem = rulePremises[i];
            string id = (i + 1).ToString();
            Premises[id] = prem;
            List<string> snapshotIds = new();
            foreach (Snapshot ss in Snapshots)
            {
                if (ss.Premises.Contains(prem))
                {
                    snapshotIds.Add(ss.Label ?? "<UNLABELLED>");
                }
            }
            if (snapshotIds.Count > 0)
            {
                PremiseSnapshotMapping[id] = snapshotIds;
            }                    
        }
    }

    /// <summary>
    /// True if there are premises to display.
    /// </summary>
    public bool HasPremises => Premises.Count > 0;

    /// <summary>
    /// True if the mappings of the premises will need to be displayed for the rule to be
    /// understood.
    /// </summary>
    public bool HasPremiseMappings => PremiseSnapshotMapping.Count > 0;

    #endregion
    #region Result representation.

    /// <summary>
    /// True if ResultEvent is not null. This member is a convenience for the templating code.
    /// </summary>
    public bool ResultIsEvent => ResultEvent != null;

    /// <summary>
    /// Result to display if the rule was a State Consistent Rule.
    /// </summary>
    public Event? ResultEvent { get; private set; }

    /// <summary>
    /// Transformations to display if the rule was a State Transferring Rule.
    /// </summary>
    public StateTransformationSet? ResultTransformations { get; private set; }

    /// <summary>
    /// Generate the ResultEvent and ResultTransformations members from the given rule.
    /// This method is called during this object's construction.
    /// </summary>
    /// <param name="r">The rule to describe.</param>
    private void GetResultFromRule(Rule r)
    {
        if (r is StateConsistentRule scr)
        {
            ResultEvent = scr.Result;
        }
        else if (r is StateTransferringRule str)
        {
            ResultTransformations = str.Result;
        }
    }

    #endregion

}
