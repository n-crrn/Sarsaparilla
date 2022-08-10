using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn.Messages;

namespace StatefulHorn.Query;

public class PremiseOptionSet
{

    private PremiseOptionSet(List<QueryNode> nodes, SigmaFactory sf, HornClause hc)
    {
        Nodes = nodes;
        SigmaFactory = sf;
        SourceClause = hc;
    }

    public static PremiseOptionSet? FromRule(
        IMessage result,
        Guard g,
        int rank,
        HornClause hc,
        QueryNodeMatrix qm,
        QueryNode? requester,
        out SigmaFactory? sf)
    {
        if (hc.BeforeRank(rank) && hc.CanResultIn(result, g, out sf))
        {
            HornClause updated = hc.Substitute(sf!.CreateForwardMap());
            Guard updatedGuard = g.Substitute(sf!.CreateBackwardMap()).Union(updated.Guard);
            List<QueryNode> nodes = new();
            foreach (IMessage premise in updated.Premises)
            {
                QueryNode qn = qm.RequestNode(premise, hc.Rank, updatedGuard, requester);
                nodes.Add(qn);
            }
            return new(nodes, sf, hc);
        }
        sf = null;
        return null;
    }

    public static PremiseOptionSet FromMessages(
        IReadOnlyList<IMessage> msgList,
        Guard g,
        int rank,
        HornClause hc,
        QueryNodeMatrix qm,
        QueryNode? requester = null)
    {
        List<QueryNode> nodes = new(from m in msgList select qm.RequestNode(m, rank, g, requester));
        return new(nodes, new(), hc);
    }

    public IReadOnlyList<QueryNode> Nodes { get; }

    public IEnumerable<QueryNode> InProgressNodes
    {
        get
        {
            return from n in Nodes where n.Status == QueryNode.NStatus.InProgress select n;
        }
    }

    private readonly SigmaFactory SigmaFactory;

    private readonly HornClause SourceClause;

    private HashSet<SigmaFactory>? PreviousResolutions;

    public bool IsEmpty => Nodes.Count == 0;

    public bool HasSucceeded
    {
        get { 
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Status != QueryNode.NStatus.Proven)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public bool HasFailed
    {
        get
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Status == QueryNode.NStatus.Failed)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public Attack? CreateSuccessResult(
        IMessage query,
        State? when,
        Guard g,
        IDictionary<IMessage, IMessage?> stateVarValues)
    {
        // Check that this PremiseOptionSet MAY provide a successful result.
        if (!((HasSucceeded || PartialSuccess) && IsConsistentWithStateVariables(stateVarValues)))
        {
            return null;
        }

        // If there are no premises, this PremiseOptionSet is a priori successful.
        if (Nodes.Count == 0)
        {
            return new Attack(
                query, 
                query.Substitute(SigmaFactory.CreateBackwardMap()), 
                SourceClause!, 
                SigmaFactory, 
                Enumerable.Empty<Attack>(), 
                null);
        }

        // Check that the premises are successful themselves.
        List<Attack> premiseAttacks = new();
        List<QueryNode> matchingNode = new();
        for (int i = 0; i < Nodes.Count; i++)
        {
            QueryNode n = Nodes[i];
            if (n.Status != QueryNode.NStatus.Unresolvable)
            {
                Attack? possAttack = n.GetStateConsistentProof(stateVarValues, when);
                if (possAttack == null)
                {
                    return null;
                }
                premiseAttacks.Add(possAttack);
                matchingNode.Add(n);
            }
        }

        // All backward maps must be consistent. The quickest way to check this is to ensure that
        // all premises can be derived with a consistent Sigma transformation.
        IMessage finalActual = query.Substitute(SigmaFactory.CreateBackwardMap());
        SigmaFactory subAttackSF = new(SigmaFactory);
        for (int i = 0; i < premiseAttacks.Count; i++)
        {
            Attack a = premiseAttacks[i];
            finalActual = finalActual.Substitute(a.Transformation.CreateBackwardMap());
            if (!a.Actual.DetermineUnifiableSubstitution(a.Query, g, matchingNode[i].Guard, subAttackSF))
            {
                return null;
            }
        }

        // As there may be several layers of substitutions, the transformation for the 
        // attack needs to be generated from scratch directly between the query and the final
        // actual resulting message.
        SigmaFactory attackSF = new();
        finalActual.DetermineUnifiableSubstitution(query, Guard.Empty, Guard.Empty, attackSF);
        return new Attack(
            query,
            finalActual,
            SourceClause!,
            attackSF,
            premiseAttacks,
            when);
    }

    public bool PartialSuccess
    {
        get
        {
            bool unresolvedSeen = false;
            for (int i = 0; i < Nodes.Count; i++)
            {
                QueryNode qn = Nodes[i];
                if (qn.Status == QueryNode.NStatus.Unresolvable || qn.Status == QueryNode.NStatus.Proven)
                {
                    unresolvedSeen |= qn.Status == QueryNode.NStatus.Unresolvable;
                }
                else
                {
                    return false;
                }
            }
            return unresolvedSeen;
        }
    }

    public List<PremiseOptionSet> AttemptResolve(QueryNodeMatrix qm, QueryNode requester, State? when)
    {
        if (!PartialSuccess)
        {
            return new();
        }

        if (PreviousResolutions == null)
        {
            PreviousResolutions = new();
        }

        List<IMessage> fullOriginal = new();
        List<IMessage> original = new();
        List<List<IMessage>> options = new();
        foreach (QueryNode n in Nodes)
        {
            fullOriginal.Add(n.Message);
            if (n.Status == QueryNode.NStatus.Proven)
            {
                original.Add(n.Message);
                options = AddToOptionsList(options, n.GetPossibilities(when).ToList());
            }
        }

        List<PremiseOptionSet> optSet = new();
        Guard g = Nodes[0].Guard; // All nodes should have the same guard.
        int rank = Nodes[0].Rank; // All nodes should have the same rank.
        foreach (List<IMessage> opt in options)
        {
            SigmaFactory sf = new();
            if (sf.CanUnifyMessagesOneWay(original, opt, g) 
                && !sf.IsEmpty 
                && !PreviousResolutions.Contains(sf))
            {
                SigmaMap sm = sf.CreateForwardMap();
                List<IMessage> updated = new(from m in fullOriginal select m.Substitute(sm));
                Guard updatedGuard = g.Substitute(sm);
                optSet.Add(FromMessages(updated, updatedGuard, rank, SourceClause, qm, requester));
                PreviousResolutions.Add(sf);
            }
        }
        return optSet;
    }

    private static List<List<IMessage>> AddToOptionsList(List<List<IMessage>> optList, List<IMessage> options)
    {
        // The simplest and most common situation.
        if (options.Count == 1)
        {
            if (optList.Count == 0)
            {
                optList.Add(options);
                return optList;
            }
            else
            {
                foreach (List<IMessage> ol in optList)
                {
                    ol.Add(options[0]);
                }
                return optList;
            }
        }
        else
        {
            if (optList.Count == 0)
            {
                foreach (IMessage o in options)
                {
                    optList.Add(new List<IMessage>() { o });
                }
                return optList;
            }
            else
            {
                List<List<IMessage>> updatedOptions = new();
                foreach (List<IMessage> ol in optList)
                {
                    foreach (IMessage o in options)
                    {
                        List<IMessage> newList = new(ol) { o };
                        updatedOptions.Add(newList);
                    }
                }
                return updatedOptions;
            }
        }
    }

    #region State variable consistency checking.

    public bool IsConsistentWithStateVariables(IDictionary<IMessage, IMessage?> stateVarValues)
    {
        if (SigmaFactory.AnyContradictionsWithState(stateVarValues))
        {
            return false;
        }
        if (Nodes.Count == 0)
        {
            return true; // Can't be inconsistent.
        }
        Dictionary<IMessage, IMessage?> freshLookup = new(stateVarValues);
        SigmaFactory.UpdateStateReplacements(freshLookup);
        foreach (QueryNode n in Nodes)
        {
            bool hasGoodPos = false;
            if (n.SuccessfulOptionSets.Count > 0)
            {
                foreach (PremiseOptionSet innerPos in n.SuccessfulOptionSets)
                {
                    if (innerPos.IsConsistentWithStateVariables(freshLookup))
                    {
                        hasGoodPos = true;
                        break;
                    }
                }
            }
            else
            {
                // If success was based on a rule, there won't be option sets.
                hasGoodPos = n.Status == QueryNode.NStatus.Unresolvable;
            }

            if (!hasGoodPos)
            {
                return false;
            }
        }
        return true;
    }

    #endregion
    #region Basic object overrides.

    public override string ToString() => string.Join(",", Nodes);

    #endregion

}
