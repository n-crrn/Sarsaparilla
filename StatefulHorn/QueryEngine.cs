using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using StatefulHorn.Messages;

namespace StatefulHorn;

public class QueryEngine
{
    public QueryEngine(
        IReadOnlySet<State> states, 
        IMessage q, State? when, 
        IEnumerable<Rule> userRules)
        : this(states, new List<IMessage>() { q }, when, userRules)
    { }

    public QueryEngine(IReadOnlySet<State> states, List<IMessage> queries, State? when, IEnumerable<Rule> userRules)
    {
        Debug.Assert(states.Count > 0);

        StateSet = new(states);
        Queries = queries;
        When = when;

        foreach (IMessage q in queries)
        {
            if (q.ContainsVariables)
            {
                string msg = $"Query message ({q}) contains variables - variables are not currently supported in the query.";
                throw new ArgumentException(msg);
            }
        }

        BasicFacts = new();
        KnowledgeRules = new();
        SystemRules = new();
        TransferringRules = new();

        foreach (Rule r in userRules)
        {
            if (r is StateConsistentRule scr)
            {
                if (scr.IsFact)
                {
                    BasicFacts.Add(scr.Result.Messages.Single());
                }
                else if (scr.Snapshots.IsEmpty && !scr.NonceDeclarations.Any() && !scr.NoncesRequired.Any())
                {
                    HornClause? kr = HornClause.FromStateConsistentRule(scr);
                    Debug.Assert(kr != null);
                    KnowledgeRules.Add(kr);
                }
                else
                {
                    SystemRules.Add(scr);
                }
            }
            else if (r is StateTransferringRule str)
            {
                TransferringRules.Add(str);
            }
            else
            {
                throw new NotImplementedException($"Unknown rule type '{r.GetType()}'");
            }
        }
    }

    #region Setup properties.

    public HashSet<State> StateSet { get; init; }

    public List<IMessage> Queries { get; init; }

    public State? When { get; init; }

    public HashSet<IMessage> BasicFacts { get; init; }

    public HashSet<HornClause> KnowledgeRules { get; init; }

    public HashSet<StateConsistentRule> SystemRules { get; init; }

    public HashSet<StateTransferringRule> TransferringRules { get; init; }

    #endregion
    #region Basic object overrides.

    public override string ToString()
    {
        string whenDesc = When == null ? "" : $"when {When} ";
        string queryDesc = Queries.Count == 1 ? Queries[0].ToString() : string.Join(" or ", Queries);
        return $"Query leak {queryDesc} {whenDesc}with {KnowledgeRules.Count} " +
            $"knowledge rules, {SystemRules.Count} system rules and " +
            $"{TransferringRules.Count} transferring rules.";
    }

    #endregion
    #region Knowledge and rule elaboration.

    private NessionManager? CurrentNessionManager = null;

    private bool CancelQuery = false;

    public async Task Execute(
        Action? onStartNextLevel,
        Action<Attack>? onGlobalAttackFound,
        Action<Nession, IReadOnlySet<HornClause>, Attack?>? onAttackAssessed,
        Action? onCompletion,
        int maxDepth = -1)
    {
        if (CancelQuery)
        {
            CancelQuery = false;
        }

        // Check the basic facts for a global attack.
        foreach (IMessage q in Queries)
        {
            if (BasicFacts.Contains(q))
            {
                onGlobalAttackFound?.Invoke(new(new List<IMessage>() { q }, new List<HornClause>()));
                onCompletion?.Invoke();
                return;
            }
        }

        // Check the knowledge rules for a global attack.
        HashSet<HornClause> globalKnowledge = new(KnowledgeRules);
        HashSet<HornClause> elaboratedKnowledge = ElaborateAndDetuple(globalKnowledge);
        (List<HornClause> basic, List<HornClause> comp) = SeparateBasicAndComp(elaboratedKnowledge);
        foreach (IMessage q in Queries)
        {
            QueryResult globalQR = CheckQuery(q, BasicFacts, basic, comp, new(new(), new()), Guard.Empty, new());
            if (globalQR.Found)
            {
                Attack globalAttack = new(globalQR.Facts!, globalQR.Knowledge!);
                onGlobalAttackFound?.Invoke(globalAttack);
                onCompletion?.Invoke();
                return;
            }
        }

        // Check nessions for attacks.
        CurrentNessionManager = new(StateSet, SystemRules.ToList(), TransferringRules.ToList());
        int maxElab = maxDepth;
        if (maxElab == -1)
        {
            maxElab = When == null ? -1 : (SystemRules.Count + TransferringRules.Count) * 2;
        }
        await CurrentNessionManager.Elaborate((List<Nession> nextLevelNessions) =>
            {
                foreach (IMessage q in Queries)
                {
                    bool atLeastOneAttack = false;
                    foreach (Nession n in nextLevelNessions)
                    {
                        Nession? validN;
                        IMessage fullQuery = q;
                        if (When != null)
                        {
                            validN = n.MatchingWhenAtEnd(When);
                            if (validN == null)
                            {
                                continue;
                            }
                            HashSet<IMessage> updatedPremises = validN.FinalStateNonVariablePremises(When.Name);
                            updatedPremises.Add(q);
                            fullQuery = new TupleMessage(updatedPremises);
                        }
                        else
                        {
                            validN = n;
                        }

                        HashSet<HornClause> thisNessionClauses = new();
                        validN.CollectHornClauses(thisNessionClauses);

                        HashSet<HornClause> fullNessionSet = new(KnowledgeRules);
                        fullNessionSet.UnionWith(thisNessionClauses);

                        HashSet<HornClause> finNessionSet = ElaborateAndDetuple(fullNessionSet);

                        (List<HornClause> basic, List<HornClause> comp) = SeparateBasicAndComp(finNessionSet);
                        QueryResult qr = CheckQuery(
                            fullQuery, 
                            BasicFacts, 
                            basic, 
                            comp, 
                            new(new(), new()), 
                            Guard.Empty, 
                            StateVarsReplacementSpec(validN));
                        Attack? foundAttack = qr.Found ? new(qr.Facts!, qr.Knowledge!) : null;
                        validN.FoundAttack = foundAttack;
                        validN.FoundSystemClauses = fullNessionSet;
                        atLeastOneAttack |= foundAttack != null;
                    }
                    if (atLeastOneAttack)
                    {
                        return true;
                    }
                }
                return false;
            }, maxElab);

        // Update the user interface.
        onStartNextLevel?.Invoke();
        foreach (Nession finalN in CurrentNessionManager.FoundNessions!)
        {
            onAttackAssessed?.Invoke(finalN, finalN.FoundSystemClauses!, finalN.FoundAttack);
        }

        onCompletion?.Invoke();
        await Task.Delay(1);
    }

    public void CancelExecution()
    {
        CancelQuery = true;
        CurrentNessionManager?.CancelElaboration();
    }

    private static (List<HornClause>, List<HornClause>) SeparateBasicAndComp(HashSet<HornClause> inputSet)
    {
        List<HornClause> basic = new();
        List<HornClause> comp = new();
        foreach (HornClause hc in inputSet)
        {
            if (hc.ComplexResult)
            {
                comp.Add(hc);
            }
            else
            {
                basic.Add(hc);
            }
        }
        return (basic, comp);
    }

    private static Dictionary<IMessage, IMessage?> StateVarsReplacementSpec(Nession n)
    {
        HashSet<IMessage> stateVarsSet = n.FindStateVariables();
        Dictionary<IMessage, IMessage?> result = new();
        foreach (IMessage sv in stateVarsSet)
        {
            result[sv] = null;
        }
        return result;
    }

    private static HashSet<HornClause> ElaborateAndDetuple(HashSet<HornClause> fullRuleset)
    {
        // === Compose where possible ===
        HashSet<HornClause> complexResults = new(fullRuleset.Count);
        HashSet<HornClause> simpleResults = new(fullRuleset.Count);
        foreach (HornClause hc in fullRuleset)
        {
            if (hc.ComplexResult)
            {
                complexResults.Add(hc);
            }
            else
            {
                simpleResults.Add(hc);
            }
        }
        HashSet<HornClause> newRuleset = new();
        foreach (HornClause cr in complexResults)
        {
            foreach (HornClause sr in simpleResults)
            {
                List<HornClause>? composed = cr.ComposeUpon(sr);
                if (composed != null)
                {
                    newRuleset.UnionWith(composed);
                }
            }
        }

        HashSet<HornClause> finishedRuleset = new(fullRuleset);
        finishedRuleset.UnionWith(newRuleset);

        // === Detuple remaining rules ===
        foreach (HornClause hc in finishedRuleset.ToList())
        {
            if (hc.Result is TupleMessage)
            {
                finishedRuleset.UnionWith(hc.DetupleResult());
            }
            else
            {
                finishedRuleset.Add(hc);
            }
        }
        return finishedRuleset;
    }

    private record QueryStatus(Dictionary<IMessage, QueryResult> Proven, HashSet<IMessage> NowProving);

    private QueryResult CheckQuery(
        IMessage queryToFind,
        HashSet<IMessage> basicFacts,
        List<HornClause> basicRules,
        List<HornClause> compRules,
        QueryStatus status,
        Guard guard,
        Dictionary<IMessage, IMessage?> stateVarReplacements,
        int rank = int.MaxValue)
    {
        if (status.NowProving.Contains(queryToFind))
        {
            return QueryResult.Failed(queryToFind, When);
        }

        if (queryToFind is VariableMessage)
        {
            // This will match anything except what is in the guard. The guard never bans 
            // everything.
            return QueryResult.BasicFact(NameMessage.Any);
        }

        status.NowProving.Add(queryToFind);
        QueryResult qr;
        if (BasicFacts.Contains(queryToFind))
        {
            qr = QueryResult.BasicFact(queryToFind, When);
        }
        else if (queryToFind is TupleMessage tMsg)
        {
            qr = CheckTupleQuery(tMsg, basicFacts, basicRules, compRules, status, guard, stateVarReplacements, rank);
        }
        else if (queryToFind is FunctionMessage fMsg)
        {
            qr = CheckFunctionQuery(fMsg, basicFacts, basicRules, compRules, status, guard, stateVarReplacements, rank);
        }
        else if (queryToFind is NonceMessage nMsg)
        {
            qr = CheckNonceQuery(nMsg, basicFacts, basicRules, rank);
        }
        else
        {
            qr = CheckBasicQuery((NameMessage)queryToFind, basicFacts, basicRules, compRules, status, guard, stateVarReplacements, rank);
        }
        status.NowProving.Remove(queryToFind);
        return qr;
    }

    private static Dictionary<IMessage, IMessage?> TransferStateReplacements(
        Dictionary<IMessage, IMessage?> whole, 
        Dictionary<IMessage, IMessage?> part)
    {
        foreach ((IMessage fromMsg, IMessage? toMsg) in part)
        {
            if (whole.ContainsKey(fromMsg))
            {
                whole[fromMsg] = toMsg;
            }
        }
        return whole;
    }

    private QueryResult CheckBasicQuery(
        NameMessage queryToFind, 
        HashSet<IMessage> facts, 
        List<HornClause> basicRules, 
        List<HornClause> compRules,
        QueryStatus status, 
        Guard guard,
        Dictionary<IMessage, IMessage?> stateRepl,
        int rank)
    {
        List<HornClause> candidates = new(from r in basicRules
                                          where (queryToFind.IsUnifiableWith(r.Result) && r.BeforeRank(rank)) 
                                                && !r.Premises.Contains(queryToFind)
                                          select r);
        candidates.Sort(SortRules);

        Dictionary<IMessage, QueryResult> foundPremiseCache = new();
        HashSet<IMessage> failedPremiseCache = new();

        foreach (HornClause checkRule in candidates)
        {
            SigmaFactory sf = new();
            List<QueryResult> qrParts = new();
            if (checkRule.Result.DetermineUnifiableSubstitution(queryToFind, checkRule.Guard, guard, sf))
            {
                if (sf.AnyContradictionsWithState(stateRepl))
                {
                    continue;
                }

                HornClause updated = checkRule.Substitute(sf.CreateForwardMap());
                Guard nextGuard = guard.PerformSubstitution(sf.CreateBackwardMap()).Union(updated.Guard);
                Dictionary<IMessage, IMessage?> updatedStateRepl = sf.UpdateStateReplacements(stateRepl);

                bool found = true;
                foreach (IMessage premise in updated.Premises)
                {
                    if (failedPremiseCache.Contains(premise))
                    {
                        found = false;
                        break;
                    }
                    if (foundPremiseCache.TryGetValue(premise, out QueryResult? cachedQR))
                    {
                        qrParts.Add(cachedQR!);
                        continue;
                    }

                    QueryResult innerResult = CheckQuery(
                        premise, 
                        facts, 
                        basicRules, 
                        compRules, 
                        status, 
                        nextGuard, 
                        updatedStateRepl, 
                        RatchetRank(checkRule, rank));
                    if (innerResult.Found)
                    {
                        qrParts.Add(innerResult);
                        foundPremiseCache[premise] = innerResult;
                    }
                    else
                    {
                        failedPremiseCache.Add(premise);
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    TransferStateReplacements(stateRepl, updatedStateRepl);
                    qrParts.Add(QueryResult.ResolvedKnowledge(queryToFind, updated));
                    return QueryResult.Compose(queryToFind, When, qrParts);
                }
            }
        }
        return QueryResult.Failed(queryToFind, When);
    }

    private QueryResult CheckNonceQuery(
        NonceMessage queryToFind,
        HashSet<IMessage> facts,
        List<HornClause> basicRules,
        int rank)
    {
        List<HornClause> candidates = new(from r in basicRules where queryToFind.Equals(r.Result) && r.BeforeRank(rank) select r);
        candidates.Sort(SortRules);
        foreach (HornClause checkRule in candidates)
        {
            SigmaFactory sf = new();
            List<QueryResult> qrParts = new();
            bool found = true;
            foreach (IMessage premise in checkRule.Premises)
            {
                if (facts.Contains(premise))
                {
                    qrParts.Add(QueryResult.BasicFact(premise));
                }
                else
                {
                    found = false;
                    break;
                }
            }
            if (found)
            {
                qrParts.Add(QueryResult.ResolvedKnowledge(queryToFind, checkRule));
                return QueryResult.Compose(queryToFind, When, qrParts);
            }
        }
        return QueryResult.Failed(queryToFind, When);
    }

    private static int RatchetRank(HornClause current, int rank) => HornClause.RatchetRank(current.Rank, rank);

    private QueryResult CheckFunctionQuery(
        FunctionMessage queryToFind, 
        HashSet<IMessage> facts, 
        List<HornClause> basicRules,
        List<HornClause> compRules,
        QueryStatus status, 
        Guard guard,
        Dictionary<IMessage, IMessage?> stateRepl,
        int rank)
    {
        List<HornClause> candidates = new(from r in compRules
                                          where r.Result is FunctionMessage fMsg && fMsg.Name == queryToFind.Name && r.BeforeRank(rank)
                                          select r);
        candidates.Sort(SortRules);
        foreach (HornClause checkRule in candidates)
        {
            SigmaFactory sf = new();
            List<QueryResult> qrParts = new();
            if (checkRule.Result.DetermineUnifiableSubstitution(queryToFind, checkRule.Guard, guard, sf))
            {
                if (sf.AnyContradictionsWithState(stateRepl))
                {
                    continue;
                }

                HornClause updated = checkRule.Substitute(sf.CreateForwardMap());
                Guard nextGuard = guard.PerformSubstitution(sf.CreateBackwardMap()).Union(updated.Guard);
                Dictionary<IMessage, IMessage?> updatedStateRepl = sf.UpdateStateReplacements(stateRepl);

                bool found = true;
                foreach (IMessage premise in updated.Premises)
                {
                    QueryResult qr = CheckQuery(
                        premise, 
                        facts, 
                        basicRules, 
                        compRules, 
                        status, 
                        nextGuard, 
                        updatedStateRepl, 
                        RatchetRank(checkRule, rank));
                    if (!qr.Found)
                    {
                        found = false;
                        break;
                    }
                    qrParts.Add(qr);
                }
                if (found)
                {
                    qrParts.Add(QueryResult.ResolvedKnowledge(queryToFind, updated));
                    return QueryResult.Compose(queryToFind, When, qrParts);
                }
            }
        }
        return QueryResult.Failed(queryToFind, When);
    }

    private QueryResult CheckTupleQuery(
        TupleMessage queryToFind, 
        HashSet<IMessage> facts, 
        List<HornClause> basicRules, 
        List<HornClause> compRules,
        QueryStatus status, 
        Guard guard,
        Dictionary<IMessage, IMessage?> stateRepl,
        int rank)
    {
        // Note that the rules have been detupled, so each element will need to be followed up individually.
        List<QueryResult> qrParts = new();
        foreach (IMessage part in queryToFind.Members)
        {
            QueryResult qr = CheckQuery(part, facts, basicRules, compRules, status, guard, stateRepl, rank);
            if (!qr.Found)
            {
                return QueryResult.Failed(queryToFind, When);
            }
            qrParts.Add(qr);
        }
        return QueryResult.Compose(queryToFind, When, qrParts);
    }

    private static int SortRules(HornClause hc1, HornClause hc2)
    {
        if (hc1.BeforeRank(hc2.Rank))
        {
            return 1;
        }
        else if (hc2.BeforeRank(hc1.Rank))
        {
            return -1;
        }
        else
        {
            int cmp = hc1.Variables.Count.CompareTo(hc2.Variables.Count);
            if (cmp == 0)
            {
                cmp = hc1.Complexity.CompareTo(hc2.Complexity);
            }
            return cmp;
        }
    }

    /*private static void DescribeExecutionState(
        IEnumerable<IMessage> facts,
        IEnumerable<HornClause> rules)
    {
        Console.WriteLine("--- Facts ---");
        int factCount = 0;
        foreach (IMessage f in facts)
        {
            Console.WriteLine(f);
            factCount++;
        }
        Console.WriteLine("--- Rules ---");
        int ruleCount = 0;
        foreach (HornClause hc in rules)
        {
            Console.WriteLine(hc);
            ruleCount++;
        }
        Console.WriteLine($"=== {factCount} facts, {ruleCount} rules ===");
    }*/

    #endregion
}
