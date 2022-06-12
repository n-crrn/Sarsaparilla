using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn.Messages;

namespace StatefulHorn;

public class PremiseOptionSet
{

    private PremiseOptionSet(List<QueryNode> nodes, SigmaFactory sf, HornClause? hc)
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
            Guard updatedGuard = g.PerformSubstitution(sf!.CreateBackwardMap()).Union(updated.Guard);
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
        QueryNodeMatrix qm,
        QueryNode? requester = null,
        HornClause? hc = null)
    {
        List<QueryNode> nodes = new(from m in msgList select qm.RequestNode(m, rank, g, requester));
        return new(nodes, new(), hc);
    }

    public static PremiseOptionSet LaterFailure(QueryNode qn)
    {
        return new(new() { qn }, new(), null);
    }

    public IReadOnlyList<QueryNode> Nodes { get; }

    public IEnumerable<QueryNode> InProgressNodes => from n in Nodes where n.Status == QNStatus.InProgress select n;

    private readonly SigmaFactory SigmaFactory;

    private readonly HornClause? SourceClause;

    internal QueryResult? Result { get; private set; }

    public bool IsEmpty => Nodes.Count == 0;

    public bool HasSucceeded => Nodes.All((QueryNode qn) => qn.ResultSucceeded);

    public bool HasFailed => (from qn in Nodes where qn.ResultFailed select qn).Any();

    internal QueryResult? CreateSuccessResult(IMessage query, State? when)
    {
        if (!HasSucceeded)
        {
            return null;
        }
        if (SourceClause != null)
        {
            Result = QueryResult.ResolvedKnowledge(
                query,
                query.PerformSubstitution(SigmaFactory.CreateBackwardMap()),
                SourceClause,
                SigmaFactory,
                when);
        }
        else
        {
            Result = QueryResult.Compose(
                query,
                query.PerformSubstitution(SigmaFactory.CreateBackwardMap()),
                when,
                SigmaFactory,
                from n in Nodes select n.Result[0]);
        }
        return Result;
    }

    public bool PartialSuccess
    {
        get
        {
            bool unresolvedSeen = false;
            foreach (QueryNode qn in Nodes)
            {
                if (qn.Status == QNStatus.Unresolvable || qn.Status == QNStatus.Proven)
                {
                    unresolvedSeen |= qn.Status == QNStatus.Unresolvable;
                }
                else
                {
                    return false;
                }
            }
            return unresolvedSeen;
        }
    }

    public List<PremiseOptionSet> AttemptResolve(QueryNodeMatrix qm, QueryNode requester)
    {
        if (!PartialSuccess)
        {
            return new();
        }

        List<IMessage> fullOriginal = new();
        List<IMessage> original = new();
        List<List<IMessage>> options = new();
        foreach (QueryNode n in Nodes)
        {
            fullOriginal.Add(n.Message);
            if (n.Status == QNStatus.Proven)
            {
                original.Add(n.Message);
                options = AddToOptionsList(options, n.Actual.ToList());
            }
        }

        List<PremiseOptionSet> optSet = new();
        Guard g = Nodes[0].Guard; // All nodes should have the same guard.
        int rank = Nodes[0].Rank; // All nodes should have the same rank.
        foreach (List<IMessage> opt in options)
        {
            SigmaFactory sf = new();
            if (sf.CanUnifyMessagesOneWay(original, opt, g))
            {
                SigmaMap sm = sf.CreateForwardMap();
                List<IMessage> updated = new(from m in fullOriginal select m.PerformSubstitution(sm));
                Guard updatedGuard = g.PerformSubstitution(sm);
                optSet.Add(PremiseOptionSet.FromMessages(updated, updatedGuard, rank, qm, requester, SourceClause));
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

    internal QueryResult? AttemptFinalResolveResult(IMessage query, State? when)
    {
        if (!PartialSuccess)
        {
            return null;
        }
        List<QueryResult> subItemResults = new();
        foreach (QueryNode qn in Nodes)
        {
            if (qn.ResultSucceeded)
            {
                subItemResults.Add(qn.Result[0]);
            }
            else
            {
                if (qn.Message is VariableMessage vMsg)
                {
                    subItemResults.Add(QueryResult.Unresolved(vMsg, qn.Rank, when));
                }
                else
                {
                    return null;
                }
            }
        }
        Result = QueryResult.Compose(
            query, 
            query.PerformSubstitution(SigmaFactory.CreateBackwardMap()), 
            when, 
            SigmaFactory, 
            subItemResults);
        return Result;
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
                hasGoodPos = n.Status == QNStatus.Proven;
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
