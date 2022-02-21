using StatefulHorn.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn;

public class QueryEngine
{
    public QueryEngine(HashSet<State> states, IMessage q, State? when, IEnumerable<Rule> userRules)
    {
        StateSet = states;
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
    #region Knowledge and rule elaboration.

    public QueryResult Execute()
    {
        if (BasicFacts.Contains(Query))
        {
            return QueryResult.BasicFact(Query);
        }

        // If there are multiple nonces in the query, then we need to consider rules
        // nession by nession.
        NessionManager nm = new(StateSet, SystemRules.ToList(), TransferringRules.ToList());
        List<(Nession, HashSet<HornClause>)> nessionClauses = new();
        nm.GenerateHornClauseSet(When, nessionClauses);
        foreach ((Nession n, HashSet<HornClause> clauseSet) in nessionClauses)
        {
            Console.WriteLine(n);
            HashSet<HornClause> fullNessionSet = new(KnowledgeRules);
            fullNessionSet.UnionWith(clauseSet);

            Console.WriteLine("--------------------------------");
            HashSet<HornClause> finNessionSet = ElaborateAndDetuple(fullNessionSet);
            DescribeExecutionState(BasicFacts, finNessionSet);
            
            QueryResult qr = CheckQuery(Query, BasicFacts, finNessionSet, new(new(), new()));
            if (qr.Found)
            {
                qr.AddSession(n);
                return qr;
            }
            Console.WriteLine("=====================================================");
        }
        return QueryResult.Failed(Query, When);
    }

    private static HashSet<HornClause> ElaborateAndDetuple(HashSet<HornClause> fullRuleset)
    {
        HashSet<HornClause> newRules = new();

        // === Compose where possible ===
        HashSet<HornClause> complexResults = new(from r in fullRuleset where r.ComplexResult select r);
        HashSet<HornClause> simpleResults = new(from r in fullRuleset where !r.ComplexResult select r);
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
            Console.WriteLine("Commencing elaboration...");

            HashSet<HornClause> addedComplex = new(from r in newRuleset where r.ComplexResult select r);
            HashSet<HornClause> addedSimple = new(from r in newRuleset where !r.ComplexResult select r);
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

        Console.WriteLine($"Proving {queryToFind}");

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
            qr = CheckBasicQuery(queryToFind, basicFacts, rules, rank);
        }
        status.Proven[queryToFind] = qr;
        status.NowProving.Remove(queryToFind);
        return qr;
    }

    private QueryResult CheckBasicQuery(IMessage queryToFind, HashSet<IMessage> facts, HashSet<HornClause> rules, int rank)
    {
        Console.WriteLine($"Querying basic {queryToFind} at rank {rank}");
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
        }
        return QueryResult.Failed(queryToFind, When);
    }

    private QueryResult CheckNonceQuery(NonceMessage queryToFind, HashSet<IMessage> facts, HashSet<HornClause> rules, int rank)
    {
        Console.WriteLine($"Querying nonce {queryToFind} at rank {rank}");
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

    private static void DescribeExecutionState(
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
    }

    #endregion
}
