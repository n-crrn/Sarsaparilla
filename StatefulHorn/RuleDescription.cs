﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatefulHorn;

/// <summary>
/// This class stores information required to describe a rule, and is intended to make it easy to
/// use templating (e.g. Razor pages) to present rules to users.
/// </summary>
public class RuleDescription
{
    public RuleDescription(Rule r)
    {
        GuardStatements = new();

        Snapshots = new();
        Premises = new();
        PremiseSnapshotMapping = new();
        OrderingStatements = new();

        GetGuardFromRule(r);
        GetSnapshotsFromRule(r);
        GetPremisesFromRule(r);
        GetResultFromRule(r);
    }

    #region Guards

    public enum GuardOp
    {
        CannotBeUnifiedTo,
        CannotBeUnifiableWith
    }

    public List<(IMessage, GuardOp, IMessage)> GuardStatements { get; init; }

    public bool HasGuard => GuardStatements.Count > 0;

    private void GetGuardFromRule(Rule r)
    {
        foreach ((IMessage from, IMessage to) in r.GuardStatements.Ununified)
        {
            GuardStatements.Add((from, GuardOp.CannotBeUnifiedTo, to));
        }
        foreach ((IMessage from, IMessage to) in r.GuardStatements.Ununifiable)
        {
            GuardStatements.Add((from, GuardOp.CannotBeUnifiableWith, to));
        }
    }

    #endregion
    #region Snapshots

    public List<Snapshot> Snapshots { get; init; }

    public List<(string, Snapshot.Ordering, string)> OrderingStatements { get; init; }

    private void GetSnapshotsFromRule(Rule r)
    {
        Snapshots.AddRange(r.Snapshots.OrderedList);
        foreach (Snapshot ss in Snapshots)
        {
            foreach ((Snapshot prev, Snapshot.Ordering o) in ss.PriorSnapshots)
            {
                OrderingStatements.Add((prev.Label!, o, ss.Label!));
            }
        }
    }

    public bool HasSnapshots => Snapshots.Count > 0;

    public bool HasSnapshotOrderings => Snapshots.Count > 1;

    #endregion
    #region Premises

    public SortedList<string, Event> Premises { get; init; }

    public Dictionary<string, List<string>> PremiseSnapshotMapping { get; init; }

    private void GetPremisesFromRule(Rule r)
    {
        IReadOnlyList<Event> rulePremises = r.Premises;
        for (int i = 0; i < rulePremises.Count; i++)
        {
            Event prem = rulePremises[i];
            string id = (i + 1).ToString();
            Premises[id] = prem;
            List<string> snapshotIds = new();
            foreach (Snapshot ss in Snapshots)
            {
                if (ss.AssociatedPremises.Contains(prem))
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

    public bool HasPremises => Premises.Count > 0;

    public bool HasPremiseMappings => PremiseSnapshotMapping.Count > 0;

    #endregion
    #region Result representation.

    public bool ResultIsEvent => ResultEvent != null;

    public Event? ResultEvent { get; private set; }

    public StateTransformationSet? ResultTransformations { get; private set; }

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