using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using StatefulHorn.Messages;

namespace StatefulHorn.Query;

public class QueryNode : IPriorityQueueSetItem
{

    public QueryNode(IMessage msg, int rank, Guard g)
    {
        Message = msg;
        if (msg is VariableMessage)
        {
            Status = NStatus.Unresolvable;
        }
        Rank = rank;
        Guard = g;
    }

    #region Properties.

    public IMessage Message { get; init; }

    public Guard Guard { get; init; }

    public int Rank { get; init; }

    public enum NStatus
    {
        InProgress,
        Waiting,
        Unresolvable,
        Proven,
        Failed
    }

    public NStatus Status { get; private set; } = NStatus.InProgress;

    public readonly List<QueryNode> LeadingFrom = new();

    public readonly List<PremiseOptionSet> OptionSets = new();

    public readonly List<PremiseOptionSet> FailedOptionSets = new();

    public readonly List<PremiseOptionSet> SuccessfulOptionSets = new();

    public int Priority => Message.FindMaximumDepth();

    #endregion
    #region Result generation.

    public IEnumerable<IMessage> GetPossibilities(State? when)
    {
        Dictionary<IMessage, IMessage?> emptyDict = new();
        foreach (PremiseOptionSet pos in SuccessfulOptionSets)
        {
            Attack? att = pos.CreateSuccessResult(Message, when, Guard, emptyDict);
            if (att != null)
            {
                yield return att.Actual;
            }
        }
        if (Status == NStatus.Unresolvable)
        {
            yield return Message;
        }
    }

    #endregion
    #region Rule assessment.

    /// <summary>
    /// Assess the possibility of the message of the current node according to the given system 
    /// rules.
    /// </summary>
    /// <param name="systemRules">Horn Clauses to check viability against.</param>
    /// <param name="matrix">The node creation and maintenance matrix for the system.</param>
    /// <param name="premiseNodes">
    /// An empty list to be populated with additional nodes to be assessed to check the
    /// possibility of this node. The purpose of this parameter is to minimise the 
    /// creation of new List objects during a query.
    /// </param>
    public void AssessRules(
        IEnumerable<HornClause> systemRules, 
        QueryNodeMatrix matrix, 
        List<QueryNode> premiseNodes)
    {
        Debug.Assert(premiseNodes.Count == 0);

        // If the message is a tuple, then prioritise searching for individual tuple members.
        if (Message is TupleMessage tMsg)
        {
            // Auto-generate a new horn clause for tuple assembly.
            HornClause tupleHc = new(Message, tMsg.Members, Guard);
            PremiseOptionSet pos = PremiseOptionSet.FromMessages(tMsg.Members, Guard, Rank, tupleHc, matrix, this);
            if (pos.HasSucceeded)
            {
                SuccessfulOptionSets.Add(pos);
                Status = NStatus.Proven;
            }
            else
            {
                OptionSets.Add(pos);
                premiseNodes.AddRange(pos.InProgressNodes);
            }
        }

        // Use the rule set to find premises.
        foreach (HornClause hc in systemRules)
        {
            PremiseOptionSet? optionSet = PremiseOptionSet.FromRule(Message, Guard, Rank, hc, matrix, this, out SigmaFactory? _);
            if (optionSet != null)
            {
                if (optionSet.HasSucceeded) // Nodes may have already been assessed.
                {
                    SuccessfulOptionSets.Add(optionSet);
                    Status = NStatus.Proven;
                }
                else if (Status != NStatus.Proven)
                {
                    OptionSets.Add(optionSet);
                    premiseNodes.AddRange(optionSet.InProgressNodes);
                }
            }
        }
        if (Status != NStatus.Proven)
        {
            Status = OptionSets.Count > 0 ? NStatus.Waiting : NStatus.Failed;
        }
        Changed = true;
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
        if (Status != NStatus.Waiting || previousNodes.Contains(this))
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
                Status = NStatus.Proven;
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

    public bool RefreshState(QueryNodeMatrix matrix, List<QueryNode> nodesToTry, State? when)
    {
        // Attempt to see if some sets can be resolved.
        List<PremiseOptionSet> resolvedAdditions = new();
        for (int i = 0; i < OptionSets.Count; i++)
        {
            List<PremiseOptionSet> newAttempts = OptionSets[i].AttemptResolve(matrix, this, when);
            foreach (PremiseOptionSet pos in newAttempts)
            {
                nodesToTry.AddRange(from n in pos.Nodes where n.Status == NStatus.InProgress select n);
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
        if (Status == NStatus.InProgress || Status == NStatus.Waiting)
        {
            if (OptionSets.Count == 0 && SuccessfulOptionSets.Count == 0)
            {
                Status = NStatus.Failed;
                Changed = true;
            }
            else if (SuccessfulOptionSets.Count > 0)
            {
                Status = NStatus.Proven;
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
    public Attack? GetStateConsistentProof(HashSet<IMessage> stateVars, State? when)
    {
        return GetStateConsistentProof(CreateStateVariablesLookup(stateVars), when);
    }

    public Attack? GetStateConsistentProof(IDictionary<IMessage, IMessage?> lookup, State? when) 
    { 
        if (Status == NStatus.Proven)
        {
            foreach (PremiseOptionSet pos in SuccessfulOptionSets)
            {
                Attack? att = pos.CreateSuccessResult(Message, when, Guard, lookup);
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
