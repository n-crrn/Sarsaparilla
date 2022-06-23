using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn.Messages;

namespace StatefulHorn.Query;

public enum QNStatus
{
    InProgress,
    Waiting,
    Unresolvable,
    Proven,
    Failed,
    TooComplex
}

public class QueryNode
{

    public QueryNode(IMessage msg, int rank, Guard g, State? when)
    {
        Message = msg;
        if (msg is VariableMessage)
        {
            Status = QNStatus.Unresolvable;
        }
        Rank = rank;
        Guard = g;
        When = when;
    }

    #region Properties.

    public IMessage Message { get; init; }

    public Guard Guard { get; init; }

    public int Rank { get; init; }

    private readonly State? When;

    public QNStatus Status { get; private set; } = QNStatus.InProgress;

    public bool ResultSucceeded => Status == QNStatus.Proven;

    public bool ResultFailed => Status == QNStatus.Failed || Status == QNStatus.TooComplex;

    public bool Terminated => ResultSucceeded || ResultFailed;

    public readonly List<QueryNode> LeadingFrom = new();

    public readonly List<PremiseOptionSet> OptionSets = new();

    public readonly List<PremiseOptionSet> FailedOptionSets = new();

    public readonly List<PremiseOptionSet> SuccessfulOptionSets = new();

    #endregion
    #region Result generation.

    public IEnumerable<IMessage> GetPossibilities()
    {
        Dictionary<IMessage, IMessage?> emptyDict = new();
        foreach (PremiseOptionSet pos in SuccessfulOptionSets)
        {
            Attack? att = pos.CreateSuccessResult(Message, When, Guard, emptyDict);
            if (att != null)
            {
                yield return att.Actual;
            }
        }
        if (Status == QNStatus.Unresolvable)
        {
            yield return Message;
        }
    }

    #endregion
    #region Rule assessment.

    public List<QueryNode> AssessRules(IEnumerable<HornClause> systemRules, QueryNodeMatrix matrix)
    {
        List<QueryNode> premiseNodes = new();
        foreach (HornClause hc in systemRules)
        {
            PremiseOptionSet? optionSet = PremiseOptionSet.FromRule(Message, Guard, Rank, hc, matrix, this, out SigmaFactory? _);
            if (optionSet != null)
            {
                if (optionSet.Nodes.Count == 0)
                {
                    SuccessfulOptionSets.Add(optionSet);
                    Status = QNStatus.Proven;
                }
                else if (Status != QNStatus.Proven)
                {
                    OptionSets.Add(optionSet);
                    premiseNodes.AddRange(optionSet.InProgressNodes);
                }
            }
        }
        if (Status == QNStatus.Proven)
        {
            // No further processing required if there is a premise-less rule.
            OptionSets.Clear();
            return new();
        }

        if (Message is TupleMessage tMsg)
        {
            // Auto-generate a new horn clause for tuple assembly.
            HornClause tupleHc = new(Message, tMsg.Members, Guard);
            PremiseOptionSet pos = PremiseOptionSet.FromMessages(tMsg.Members, Guard, Rank, tupleHc, matrix, this);
            OptionSets.Add(pos);
            premiseNodes.AddRange(pos.InProgressNodes);
        }

        Status = OptionSets.Count > 0 ? QNStatus.Waiting : QNStatus.Failed;
        Changed = true;
        return premiseNodes;
    }

    /// <summary>
    /// This method is called on the King QueryNode at the end of the run, to provide the
    /// final assessment of the viability of a message. Typically, if a variable has not
    /// been fully resolved, it means that the variable can be anything. If it can be
    /// anything, then we have a viable value.
    /// </summary>
    public void FinalAssess() => InnerFinalAssess(new());

    public void InnerFinalAssess(HashSet<QueryNode> previousNodes)
    {
        if (Status != QNStatus.Waiting || previousNodes.Contains(this))
        {
            return;
        }
        previousNodes.Add(this);
        foreach (PremiseOptionSet pos in OptionSets)
        {
            foreach (QueryNode qn in pos.Nodes)
            {
                qn.InnerFinalAssess(previousNodes);
            }
        }
        InnerAssessState();
        for (int i = 0; i < OptionSets.Count; i++)
        {
            PremiseOptionSet pos = OptionSets[i];
            if (pos.PartialSuccess)
            {
                Status = QNStatus.Proven;
                OptionSets.RemoveAt(i);
                SuccessfulOptionSets.Add(pos);
                break;
            }
        }
    }

    #endregion
    #region Notification methods and auxiliaries.

    public bool Changed { get; private set; }

    public void ClearChanged()
    {
        Changed = false;
    }

    public bool RefreshState(QueryNodeMatrix matrix, List<QueryNode> nodesToTry)
    {
        // Attempt to see if some sets can be resolved.
        List<PremiseOptionSet> resolvedAdditions = new();
        for (int i = 0; i < OptionSets.Count; i++)
        {
            List<PremiseOptionSet> newAttempts = OptionSets[i].AttemptResolve(matrix, this);
            foreach (PremiseOptionSet pos in newAttempts)
            {
                nodesToTry.AddRange(from n in pos.Nodes where n.Status == QNStatus.InProgress select n);
            }
            resolvedAdditions.AddRange(newAttempts);
        }
        OptionSets.AddRange(resolvedAdditions);
        return InnerAssessState();
    }

    private bool InnerAssessState()
    {
        // Sort out what works and doesn't.
        for (int i = 0; i < OptionSets.Count; i++)
        {
            if (OptionSets[i].HasSucceeded)
            {
                SuccessfulOptionSets.Add(OptionSets[i]);
                OptionSets.RemoveAt(i);
                i--;
            }
            else if (OptionSets[i].HasFailed)
            {
                FailedOptionSets.Add(OptionSets[i]);
                OptionSets.RemoveAt(i);
                i--;
            }
        }

        // Final stage.
        if (Status == QNStatus.InProgress || Status == QNStatus.Waiting)
        {
            if (OptionSets.Count == 0 && SuccessfulOptionSets.Count == 0)
            {
                Status = QNStatus.Failed;
                Changed = true;
            }
            else if (SuccessfulOptionSets.Count > 0)
            {
                Status = QNStatus.Proven;
                Changed = true;
            }
        }
        return Changed;
    }

    #endregion
    #region Consistency checking.

    /// <summary>
    /// Convenience method to create a dictionary structure that can be used when checking the
    /// consistency of state variable across separate suggested solutions.
    /// </summary>
    /// <returns>
    /// Dictionary with the keys set to the state variables of the nession, and the values set
    /// to null (awaiting replacement by actual values).
    /// </returns>
    private static IDictionary<IMessage, IMessage?> CreateStateVariablesLookup(HashSet<IMessage> stateVars)
    {
        Dictionary<IMessage, IMessage?> lookup = new();
        foreach (IMessage varMsg in stateVars)
        {
            lookup[varMsg] = null;
        }
        return lookup;
    }

    /// <summary>
    /// Attempt to find an attack that maintains the consistency of the given variable messages.
    /// </summary>
    /// <param name="stateVars">Variable messages to keep consistent.</param>
    /// <returns>If an attack is found, it is returned. Otherwise, null.</returns>
    public Attack? GetStateConsistentProof(HashSet<IMessage> stateVars)
    {
        return GetStateConsistentProof(CreateStateVariablesLookup(stateVars));
    }

    public Attack? GetStateConsistentProof(IDictionary<IMessage, IMessage?> lookup) 
    { 
        if (Status == QNStatus.Proven)
        {
            foreach (PremiseOptionSet pos in SuccessfulOptionSets)
            {
                Attack? att = pos.CreateSuccessResult(Message, When, Guard, lookup);
                if (att != null)
                {
                    return att;
                }
            }
        }
        return null;
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is QueryNode qn && Message.Equals(qn.Message) && Guard.Equals(qn.Guard) && Rank == qn.Rank;
    }

    public override int GetHashCode() => Message.GetHashCode();

    public override string ToString() => $"{Rank}#{Message}" + (!Guard.IsEmpty ? "#" + Guard.ToString() : "");

    #endregion

}
