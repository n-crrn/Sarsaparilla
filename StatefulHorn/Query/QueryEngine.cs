using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StatefulHorn.Messages;

namespace StatefulHorn.Query;

public class QueryEngine
{

    public const int DefaultMaximumTerms = 300;

    public QueryEngine(
        IReadOnlySet<State> states,
        IMessage query,
        State? when,
        IEnumerable<Rule> userRules,
        int elaborationLimit = -1,
        int maximumTerms = DefaultMaximumTerms) 
        : this(states, new List<IMessage>() { query }, when, userRules, elaborationLimit, maximumTerms) 
    { }

    public QueryEngine(
        IEnumerable<State> states,
        IEnumerable<IMessage> queries,
        State? when,
        IEnumerable<Rule> userRules,
        int elaborationLimit = -1,
        int maximumTerms = DefaultMaximumTerms)
    {
        Queries = new List<IMessage>(queries);
        InitStates = new List<State>(states);
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
        ElaborationLimit = elaborationLimit;
        MaximumTerms = maximumTerms;
    }

    #region Properties.

    public IReadOnlyList<IMessage> Queries { get; }

    public IReadOnlyList<State> InitStates { get; }

    public State? When { get; }

    public List<HornClause> KnowledgeRules { get; }

    public List<StateConsistentRule> SystemRules { get; }

    public List<StateTransferringRule> TransferringRules { get; }

    public int ElaborationLimit { get; }

    public int MaximumTerms { get; }

    #endregion
    #region Highest level query management - multiple executions.

    private NessionManager? CurrentNessionManager = null;

    public const int UseDefaultDepth = -1;

    public async Task Execute(
        Action? onStartNextLevel,
        Action<Nession, IReadOnlySet<HornClause>, Attack?>? onAttackAssessed,
        Action? onCompletion,
        int maxDepth = UseDefaultDepth,
        bool checkIteratively = false)
    {
        int maxElab;
        if (maxDepth == -1)
        {
            maxElab = ElaborationLimit == -1 ? SystemRules.Count + TransferringRules.Count * 2 : ElaborationLimit;
        }
        else
        {
            maxElab = maxDepth;
        }

        CurrentNessionManager = new(InitStates, SystemRules, TransferringRules);
        await CurrentNessionManager.Elaborate((nextLevelNessions) =>
        {
            bool atLeastOneAttack = false;
            foreach (IMessage q in Queries)
            {
                Task[] runningQueries = new Task[nextLevelNessions.Count];
                for (int i = 0; i < nextLevelNessions.Count; i++)
                {
                    Nession n = nextLevelNessions[i];
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
                    runningQueries[i] = Task.Factory.StartNew(() =>
                    {
                        HashSet<HornClause> clauses = new();
                        queryNession.CollectHornClauses(clauses);
                        clauses.UnionWith(KnowledgeRules);

                        Attack? foundAttack = CheckQuery(query, clauses, n.FindStateVariables());
                        queryNession.FoundAttack = foundAttack;
                        queryNession.FoundSystemClauses = clauses;
                        if (foundAttack != null)
                        {
                            atLeastOneAttack = true;
                        }
                    });
                }
                Task.WaitAll(runningQueries);
            
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

    private Attack? CheckQuery(IMessage query, HashSet<HornClause> clauses, HashSet<IMessage> stateVars)
    {
        int maxRank = 0;
        foreach (HornClause hc in clauses)
        {
            maxRank = Math.Max(maxRank, hc.Rank);
        }

        if (!PreQueryCheck(query, clauses))
        {
            return null;
        }

        QueryNodeMatrix matrix = new(When);
        PriorityQueueSet<QueryNode> inProgressNodes = new();
        QueryNode kingNode = matrix.RequestNode(query, maxRank, Guard.Empty);
        inProgressNodes.Enqueue(kingNode);
        int termCounter = 0;

        while (inProgressNodes.Count > 0 && termCounter < MaximumTerms)
        {
            termCounter++;
            QueryNode next = inProgressNodes.Dequeue()!;
            if (next.Status == QueryNode.NStatus.InProgress)
            {
                List<QueryNode> newNodes = next.AssessRules(clauses, matrix);
                newNodes.AddRange(matrix.EnsureNodesUpdated(next));
                if (kingNode.Status == QueryNode.NStatus.Proven)
                {
                    break;
                }
                for (int i = 0; i < newNodes.Count; i++)
                {
                    QueryNode qn = newNodes[i];
                    if (qn.Status == QueryNode.NStatus.InProgress)
                    {
                        inProgressNodes.Enqueue(qn);
                    }
                }
            }
        }

        if (kingNode.Status != QueryNode.NStatus.Proven)
        {
            kingNode.FinalAssess();
        }
        return kingNode.GetStateConsistentProof(stateVars);
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
        query.CollectMessages(requiredTerms, (msg) => msg is NameMessage or NonceMessage);

        HashSet<IMessage> systemTerms = new();
        foreach (HornClause hc in clauses)
        {
            hc.Result.CollectMessages(systemTerms, (msg) => msg is NameMessage or NonceMessage);
        }

        return requiredTerms.IsSubsetOf(systemTerms);
    }

    #endregion

}
