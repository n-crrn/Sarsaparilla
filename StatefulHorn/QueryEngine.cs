using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using StatefulHorn.Messages;

namespace StatefulHorn;

public class QueryEngine
{
    public QueryEngine(IReadOnlySet<State> states, IMessage q, State? when, IEnumerable<Rule> userRules)
    {
        Debug.Assert(states.Count > 0);

        StateSet = new(states);
        Query = q;
        When = when;

        if (q.ContainsVariables)
        {
            string msg = $"Query message ({q}) contains variables - variables are not currently supported in the query.";
            throw new ArgumentException(msg);
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

    public IMessage Query { get; init; }

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
        return $"Query leak {Query} {whenDesc}with {KnowledgeRules.Count} " +
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
        Action<Nession, HashSet<HornClause>, Attack?>? onAttackAssessed,
        Action? onCompletion)
    {
        if (CancelQuery)
        {
            CancelQuery = false;
        }

        // Check the basic facts for a global attack.
        if (BasicFacts.Contains(Query))
        {
            onGlobalAttackFound?.Invoke(new(new List<IMessage>() { Query }, new List<HornClause>()));
            onCompletion?.Invoke();
            return;
        }

        // Check the knowledge rules for a global attack.
        HashSet<HornClause> globalKnowledge = new(KnowledgeRules);
        HashSet<HornClause> elaboratedKnowledge = ElaborateAndDetuple(globalKnowledge);
        QueryResult globalQR = CheckQuery(Query, BasicFacts, elaboratedKnowledge, new(new(), new()));
        if (globalQR.Found)
        {
            Attack globalAttack = new(globalQR.Facts!, globalQR.Knowledge!);
            onGlobalAttackFound?.Invoke(globalAttack);
            onCompletion?.Invoke();
            return;
        }

        // Check nessions for a attacks.
        CurrentNessionManager = new(StateSet, SystemRules.ToList(), TransferringRules.ToList());
        int maxElab = When == null ? -1 : (SystemRules.Count + TransferringRules.Count) * 2;
        await CurrentNessionManager.Elaborate((List<Nession> nextLevelNessions) =>
            {
                onStartNextLevel?.Invoke();

                List<(Nession, HashSet<HornClause>)> nessionClauses = new();
                HashSet<IMessage> premises = new();
                bool atLeastOneAttack = false;
                foreach (Nession n in nextLevelNessions)
                {
                    Nession? validN;
                    IMessage fullQuery = Query;
                    if (When != null)
                    {
                        validN = n.MatchingWhenAtEnd(When);
                        if (validN == null)
                        {
                            continue;
                        }
                        HashSet<IMessage> updatedPremises = validN.FinalStateNonVariablePremises();
                        updatedPremises.Add(Query);
                        fullQuery = new TupleMessage(updatedPremises);
                    }
                    else
                    {
                        validN = n;
                    }

                    HashSet<HornClause> thisNessionClauses = new();
                    validN.CollectHornClauses(thisNessionClauses, premises);

                    HashSet<HornClause> fullNessionSet = new(KnowledgeRules);
                    fullNessionSet.UnionWith(thisNessionClauses);

                    HashSet<HornClause> finNessionSet = ElaborateAndDetuple(fullNessionSet);

                    QueryResult qr = CheckQuery(fullQuery, BasicFacts, finNessionSet, new(new(), new()));
                    Attack? foundAttack = qr.Found ? new(qr.Facts!, qr.Knowledge!) : null;
                    onAttackAssessed?.Invoke(validN, fullNessionSet, foundAttack);
                    atLeastOneAttack |= foundAttack != null;
                }
                return atLeastOneAttack;
            }, maxElab);

        onCompletion?.Invoke();
        await Task.Delay(1);
    }

    public void CancelExecution()
    {
        CancelQuery = true;
        CurrentNessionManager?.CancelElaboration();
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
        fullRuleset.UnionWith(fullRuleset);

        bool found = newRuleset.Count > 0;
        while (found)
        {
            HashSet<HornClause> addedComplex = new(newRuleset.Count);
            HashSet<HornClause> addedSimple = new(newRuleset.Count);
            foreach (HornClause hc in newRuleset)
            {
                if (hc.ComplexResult)
                {
                    addedComplex.Add(hc);
                }
                else
                {
                    addedSimple.Add(hc);
                }
            }
            complexResults.UnionWith(addedComplex);
            simpleResults.UnionWith(addedSimple);

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

            int fullCount = fullRuleset.Count;
            fullRuleset.UnionWith(newRuleset);
            found = fullCount != fullRuleset.Count;
        }

        // === Detuple remaining rules ===
        HashSet<HornClause> finishedRuleset = new(fullRuleset.Count);
        foreach (HornClause hc in fullRuleset)
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
        HashSet<HornClause> rules,
        QueryStatus status,
        int rank = int.MaxValue)
    {
        // In order to prevent stack overflows from attempting to chase queries
        // that are already in the process of being proven, we firstly check to
        // see if it is covered in status.
        if (status.Proven.TryGetValue(queryToFind, out QueryResult? provenQR))
        {
            return provenQR;
        }
        if (status.NowProving.Contains(queryToFind))
        {
            return QueryResult.Failed(queryToFind, When);
        }

        status.NowProving.Add(queryToFind);
        QueryResult qr;
        if (BasicFacts.Contains(queryToFind))
        {
            qr = QueryResult.BasicFact(queryToFind, When);
        }
        else if (queryToFind is TupleMessage tMsg)
        {
            qr = CheckTupleQuery(tMsg, basicFacts, rules, status, rank);
        }
        else if (queryToFind is FunctionMessage fMsg)
        {
            qr = CheckFunctionQuery(fMsg, basicFacts, rules, status, rank);
        }
        else if (queryToFind is NonceMessage nMsg)
        {
            qr = CheckNonceQuery(nMsg, basicFacts, rules, rank);
        }
        else
        {
            qr = CheckBasicQuery(queryToFind, basicFacts, rules, status, rank);
        }
        status.Proven[queryToFind] = qr;
        status.NowProving.Remove(queryToFind);
        return qr;
    }

    private QueryResult CheckBasicQuery(IMessage queryToFind, HashSet<IMessage> facts, HashSet<HornClause> rules, QueryStatus status, int rank)
    {
        List<HornClause> candidates = new(from r in rules where queryToFind.IsUnifiableWith(r.Result) && r.BeforeRank(rank) select r);
        candidates.Sort(SortRules);
        foreach (HornClause checkRule in candidates)
        {
            SigmaFactory sf = new();
            List<QueryResult> qrParts = new();
            // FIXME: Update to follow rule-specific guard.
            if (queryToFind.DetermineUnifiableSubstitution(checkRule.Result, Guard.Empty, sf))
            {
                HornClause updated = checkRule.Substitute(sf.CreateBackwardMap()).Anify();
                bool found = true;
                foreach (IMessage premise in (from up in updated.Premises where !NameMessage.Any.Equals(up) select up))
                {
                    QueryResult innerResult = CheckQuery(premise, facts, rules, status, rank);
                    if (innerResult.Found)
                    {
                        qrParts.Add(innerResult);
                    }
                    else
                    {
                        found = false;
                        break;
                    }
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

    private QueryResult CheckNonceQuery(NonceMessage queryToFind, HashSet<IMessage> facts, HashSet<HornClause> rules, int rank)
    {
        List<HornClause> candidates = new(from r in rules where queryToFind.Equals(r.Result) && r.BeforeRank(rank) select r);
        candidates.Sort(SortRules);
        foreach (HornClause checkRule in candidates)
        {
            SigmaFactory sf = new();
            List<QueryResult> qrParts = new();
            // FIXME: Update to follow rule-specific guard.
            HornClause updated = checkRule.Anify();
            bool found = true;
            foreach (IMessage premise in (from up in updated.Premises where !NameMessage.Any.Equals(up) select up))
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
                qrParts.Add(QueryResult.ResolvedKnowledge(queryToFind, updated));
                return QueryResult.Compose(queryToFind, When, qrParts);
            }
        }
        return QueryResult.Failed(queryToFind, When);
    }

    private static int RatchetRank(HornClause current, int rank) => HornClause.RatchetRank(current.Rank, rank);

    private QueryResult CheckFunctionQuery(IMessage queryToFind, HashSet<IMessage> facts, HashSet<HornClause> rules, QueryStatus status, int rank)
    {
        List<HornClause> candidates = new(from r in rules
                                          where r.Result is FunctionMessage && queryToFind.IsUnifiableWith(r.Result) && r.BeforeRank(rank)
                                          select r);
        candidates.Sort(SortRules);
        foreach (HornClause checkRule in candidates)
        {
            SigmaFactory sf = new();
            List<QueryResult> qrParts = new();
            // FIXME: Update to follow rule-specific guard.
            if (queryToFind.DetermineUnifiableSubstitution(checkRule.Result, Guard.Empty, sf))
            {
                SigmaMap bwdMap = sf.CreateBackwardMap();
                HornClause updated = checkRule.Substitute(bwdMap).Anify();
                bool found = true;
                foreach (IMessage premise in (from up in updated.Premises where !NameMessage.Any.Equals(up) select up))
                {
                    QueryResult qr = CheckQuery(premise, facts, rules, status, RatchetRank(checkRule, rank));
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

    private QueryResult CheckTupleQuery(TupleMessage queryToFind, HashSet<IMessage> facts, HashSet<HornClause> rules, QueryStatus status, int rank)
    {
        // Note that the rules have been detupled, so each element will need to be followed up individually.
        List<QueryResult> qrParts = new();
        foreach (IMessage part in queryToFind.Members)
        {
            QueryResult qr = CheckQuery(part, facts, rules, status, rank);
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
        int cmp = hc1.Variables.Count.CompareTo(hc2.Variables.Count);
        if (cmp == 0)
        {
            cmp = hc1.Complexity.CompareTo(hc2.Complexity);
        }
        return cmp;
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
