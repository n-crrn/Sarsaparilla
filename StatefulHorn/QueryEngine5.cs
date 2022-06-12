using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;
using StatefulHorn.Messages;

namespace StatefulHorn;

public class QueryEngine5
{

    public QueryEngine5(
        IReadOnlySet<State> states,
        IMessage query,
        State? when,
        IEnumerable<Rule> userRules) : this(states, new List<IMessage>() { query }, when, userRules) { }

    public QueryEngine5(
        IReadOnlySet<State> states,
        List<IMessage> queries,
        State? when,
        IEnumerable<Rule> userRules)
    {
        Queries = queries;
        InitStates = new(states);
        When = when;
        KnowledgeRules = new();
        SystemRules = new();
        TransferringRules = new();
        foreach (Rule r in userRules)
        {
            if (r is StateConsistentRule sysRule)
            {
                if (r.IsStateless)
                {
                    KnowledgeRules.Add(HornClause.FromStateConsistentRule(sysRule)!);
                }
                else
                {
                    SystemRules.Add(sysRule);
                }
                
            }
            else if (r is StateTransferringRule transRule)
            {
                TransferringRules.Add(transRule);
            }
        }
    }

    #region Properties.

    public List<IMessage> Queries { get; init; }

    public List<State> InitStates { get; init; }

    public State? When { get; init; }

    public int MaximumRank { get; init; }

    public List<HornClause> KnowledgeRules { get; init; }

    public List<StateConsistentRule> SystemRules { get; init; }

    public List<StateTransferringRule> TransferringRules { get; init; }

    #endregion
    #region Highest level query management - multiple executions.

    private NessionManager? CurrentNessionManager = null;

    public const int UseDefaultDepth = -1;

    public async Task Execute(
        Action? onStartNextLevel,
        //Action<Attack>? onGlobalAttackFound,
        Action<Nession, IReadOnlySet<HornClause>, Attack?>? onAttackAssessed,
        Action? onCompletion,
        int maxDepth = UseDefaultDepth,
        bool checkIteratively = false)
    {
        CurrentNessionManager = new(InitStates, SystemRules, TransferringRules);
        int maxElab = maxDepth == -1 ? SystemRules.Count + (TransferringRules.Count * 2) : maxDepth;
        await CurrentNessionManager.Elaborate((List<Nession> nextLevelNessions) =>
        {
            bool atLeastOneAttack = false;
            foreach (IMessage q in Queries)
            {
                foreach (Nession n in nextLevelNessions)
                {
                    // Update the query to handle the query when construct.
                    Nession? queryNession;
                    IMessage query = q;
                    if (When != null)
                    {
                        queryNession = n.MatchingWhenAtEnd(When);
                        if (queryNession == null)
                        {
                            continue;
                        }
                        HashSet<IMessage> updatedPremises = queryNession.FinalStateNonVariablePremises(When.Name);
                        updatedPremises.Add(q);
                        query = new TupleMessage(updatedPremises);
                    }
                    else
                    {
                        queryNession = n;
                    }

                    // Collect horn clauses for submission to query engine.
                    HashSet<HornClause> clauses = new();
                    queryNession.CollectHornClauses(clauses);
                    clauses.UnionWith(KnowledgeRules);
                    QueryResult result = CheckQuery(query, clauses, n.FindStateVariables());
                    Attack? foundAttack = result.Found ? new(Enumerable.Empty<IMessage>(), result.Knowledge!) : null;
                    queryNession.FoundAttack = foundAttack;
                    atLeastOneAttack |= result.Found;
                    if (result.Found)
                    {
                        break;
                    }
                }
                if (atLeastOneAttack)
                {
                    break;
                }
            }
            return atLeastOneAttack;
        }, maxElab, checkIteratively);

        onStartNextLevel?.Invoke();
        IReadOnlyList<Nession> allN = CurrentNessionManager.FoundNessions!;
        for (int i = allN.Count - 1; i >= 0; i--)
        {
            Nession finalN = allN[i];
            onAttackAssessed?.Invoke(finalN, finalN.FoundSystemClauses!, finalN.FoundAttack);
        }
        onCompletion?.Invoke();
    }

    public void CancelExecution()
    {
        CurrentNessionManager?.CancelElaboration();
    }

    #endregion
    #region Individual query execution.

    const int MaxTerms = 1000;

    private QueryResult CheckQuery(IMessage query, HashSet<HornClause> clauses, HashSet<IMessage> stateVars)
    {
        int maxRank = 0;
        foreach (HornClause hc in clauses)
        {
            maxRank = Math.Max(maxRank, hc.Rank);
        }

        if (!PreQueryCheck(query, clauses))
        {
            return QueryResult.Failed(query, maxRank, When);
        }

        QueryNodeMatrix matrix = new(stateVars, When);
        QueueSet<QueryNode> inProgressNodes = new();
        QueryNode kingNode = matrix.RequestNode(query, maxRank, Guard.Empty);
        inProgressNodes.Enqueue(kingNode);

        while (inProgressNodes.Count > 0 && matrix.TermCount < MaxTerms)
        {
            QueryNode next = inProgressNodes.Dequeue()!;
            if (next.Status != QNStatus.InProgress)
            {
                continue;
            }
            List<QueryNode> newNodes = next.AssessRules(clauses, matrix);
            newNodes.AddRange(matrix.EnsureNodesUpdated(next));
            if (newNodes != null)
            {
                foreach (QueryNode qn in from nn in newNodes where nn.Status == QNStatus.InProgress select nn)
                {
                    inProgressNodes.Enqueue(qn);
                }
            }
        }

        if (kingNode.Status != QNStatus.Proven)
        {
            kingNode.FinalAssess();
        }
        QueryResult? qr = kingNode.GetStateConsistentProof(stateVars);
        return qr ?? QueryResult.Failed(query, maxRank, When);
    }

    /// <summary>
    /// For the query to work, there HAS to be a mention of all names and nonces on the 
    /// right-hand-side of at least one of the input clauses. This is a quick check to
    /// ensure that the user does not waste time on something that will not pan out.
    /// </summary>
    /// <param name="query">Query to check.</param>
    /// <param name="clauses">Clauses to check the query against.</param>
    /// <returns>True if the query is possible.</returns>
    private static bool PreQueryCheck(IMessage query, HashSet<HornClause> clauses)
    {
        HashSet<IMessage> requiredTerms = new();
        query.CollectMessages(requiredTerms, (IMessage msg) => msg is NameMessage or NonceMessage);

        HashSet<IMessage> systemTerms = new();
        foreach (HornClause hc in clauses)
        {
            hc.Result.CollectMessages(systemTerms, (IMessage msg) => msg is NameMessage or NonceMessage);
        }

        return requiredTerms.IsSubsetOf(systemTerms);
    }

    #endregion

}
