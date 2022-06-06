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

    public const int UseDefaultDepth = -1;

    public async Task Execute(
        Action? onStartNextLevel,
        Action<Attack>? onGlobalAttackFound,
        Action<Nession, IReadOnlySet<HornClause>, Attack?>? onAttackAssessed,
        Action? onCompletion,
        int maxDepth = UseDefaultDepth,
        bool checkIteratively = false)
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
        (List<HornClause> basic, List<HornClause> comp) = SeparateBasicAndComp(globalKnowledge);
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

                        (List<HornClause> basic, List<HornClause> comp) = SeparateBasicAndComp(fullNessionSet);
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
            }, maxElab, checkIteratively);

        // Update the user interface.
        onStartNextLevel?.Invoke();
        IReadOnlyList<Nession> allN = CurrentNessionManager.FoundNessions!;
        for (int i = allN.Count - 1; i >= 0; i--)
        {
            Nession finalN = allN[i];
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
            if (hc.Result is FunctionMessage)
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
        // Sometimes, there are no rules due to the system and knowledge rules interations during
        // the nession elaboration.
        const int infiniteRank = -1; // FIXME: Infinite centralise.
        if (basicRules.Count + compRules.Count == 0)
        {
            return QueryResult.Failed(queryToFind, infiniteRank, When); 
        }
        QueryFrame qf = new(
            basicFacts,
            basicRules,
            compRules,
            status.NowProving,
            guard,
            stateVarReplacements,
            //new(),
            rank,
            new(),
            (from hc in basicRules.Concat(compRules) select Math.Abs(hc.IncreasesDepthBy)).Max() * 2 + queryToFind.FindMaximumDepth());
        //(from hc in basicRules.Concat(compRules) select Math.Abs(hc.IncreasesDepthBy)).Sum() + queryToFind.FindMaximumDepth());
        List<QueryResult> options = InnerCheckQuery(queryToFind, qf);
        if (options.Count == 0)
        {
            return QueryResult.Failed(queryToFind, infiniteRank, When);
        }
        return options[0]; // At this level, does not matter if there are many.
    }

    private record QueryFrame(
        HashSet<IMessage> BasicFacts,
        List<HornClause> BasicRules,
        List<HornClause> FunctionRules,
        HashSet<IMessage> NowProving,
        Guard Guard,
        Dictionary<IMessage, IMessage?> StateVariableReplacements,
        int Rank,
        Dictionary<IMessage, SortedDictionary<int, HashSet<HornClause>>> FailureCache,
        int MaxDepth
        )
    {

        public QueryFrame Next(
            Guard guard,
            Dictionary<IMessage, IMessage?> statVar, 
            int rank)
        {
            return this with
            {
                Guard = guard,
                StateVariableReplacements = statVar,
                Rank = rank
            };
        }

        public QueryFrame CloneSubstitutions()
        {
            return this with
            {
                StateVariableReplacements = new(StateVariableReplacements),
            };
        }

        public void CacheFailure(IMessage value, HornClause followingClause)
        {
            IMessage blankedValue = MessageUtils.BlankMessage(value);
            if (FailureCache.TryGetValue(blankedValue, out SortedDictionary<int, HashSet<HornClause>>? noFollows))
            {
                bool foundThisRank = false;
                foreach ((int r, HashSet<HornClause> clauses) in noFollows)
                {
                    foundThisRank |= r == Rank;
                    if (r <= Rank)
                    {
                        clauses.Add(followingClause);
                    }
                    else
                    {
                        break;
                    }
                }
                if (!foundThisRank)
                {
                    noFollows[Rank] = new() { followingClause };
                }
            }
            else
            {
                FailureCache[blankedValue] = new SortedDictionary<int, HashSet<HornClause>>() {
                    { Rank, new HashSet<HornClause>() { followingClause } }
                };
            }
        }

        public HashSet<HornClause>? GetCachedNoFollowClauses(IMessage value)
        {
            IMessage blankedValue = MessageUtils.BlankMessage(value);
            if (!FailureCache.TryGetValue(blankedValue, out SortedDictionary<int, HashSet<HornClause>>? noFollowsAll))
            {
                return null;
            }
            if (noFollowsAll.TryGetValue(Rank, out HashSet<HornClause>? noFollows))
            {
                return noFollows;
            }
            foreach ((int r, HashSet<HornClause> clauses) in noFollowsAll)
            {
                if (r < Rank)
                {
                    noFollows = clauses;
                }
                else
                {
                    break;
                }
            }
            return noFollows;
        }

        public IEnumerable<IMessage> GetFactsFromRank(int rank)
        {
            // Start with basic facts, as they are always available.
            foreach (IMessage f in BasicFacts)
            {
                yield return f;
            }

            // Go through rules at or below rank.
            if (BasicRules.Count + FunctionRules.Count > 0)
            {
                foreach (HornClause br in BasicRules.Concat(FunctionRules))
                {
                    if (br.Rank <= rank && br.Premises.Count == 0)
                    {
                        yield return br.Result;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Maximum number of rules investigated in solving for a message.
    /// </summary>
    private static readonly int MaxChase = 10;

    private List<QueryResult> InnerCheckQuery(IMessage queryToFind, QueryFrame qf)
    {
        if (queryToFind.FindMaximumDepth() > qf.MaxDepth)
        {
            return new(); // Hit the depth boundary - failed.
        }

        List<QueryResult> options = new();
        IMessage blankedQuery = MessageUtils.BlankMessage(queryToFind);
        if (!qf.NowProving.Contains(queryToFind))
        {
            if (queryToFind is VariableMessage vQuery)
            {
                options.Add(QueryResult.Unresolved(vQuery, qf.Rank, When));
            }
            else
            {
                if (qf.NowProving.Count >= MaxChase)
                {
                    return options; // Probably not going to find the answer down this branch.
                }
                qf.NowProving.Add(queryToFind);

                // Check the facts, see what matches.
                if (BasicFacts.Contains(queryToFind))
                {
                    options.Add(QueryResult.BasicFact(queryToFind, queryToFind, new(), When));
                }
                else
                {
                    if (CanMatchBasicFacts(qf.BasicFacts, queryToFind, qf.Guard, out List<(IMessage, SigmaFactory)> matchList))
                    {
                        options.AddRange(from ml in matchList select QueryResult.BasicFact(queryToFind, ml.Item1, ml.Item2, When));
                    }

                    // Also have a look at other rule-derived possibilities.
                    if (queryToFind is TupleMessage tMsg)
                    {
                        options.AddRange(CheckTupleQuery(tMsg, qf));
                    }
                    else if (queryToFind is FunctionMessage fMsg)
                    {
                        options.AddRange(CheckFunctionQuery(fMsg, qf));
                    }
                    else
                    {
                        options.AddRange(CheckBasicQuery(queryToFind, qf));
                    }
                }

                qf.NowProving.Remove(queryToFind);
            }
        }
        return options;
    }

    private static bool CanMatchBasicFacts(
        HashSet<IMessage> basicFacts, 
        IMessage queryToFind, 
        Guard g, 
        out List<(IMessage, SigmaFactory)> options)
    {
        options = new();
        foreach (IMessage fact in basicFacts)
        {
            SigmaFactory sf = new();
            if (fact.DetermineUnifiableSubstitution(queryToFind, Guard.Empty, g, sf))
            {
                options.Add((fact, sf));
            }
        }
        return options.Count > 0;
    }

    private List<List<QueryResult>> AttemptSatisfyPremises(QueryFrame qf, IReadOnlyList<IMessage> premiseSet)
    {
        // Try to deal with this particular premise.
        IMessage premise = premiseSet[0];
        List<QueryResult> options = InnerCheckQuery(premise, qf);

        // Adjust other premises, and reincorporate any of their replacements.
        if (premiseSet.Count > 1)
        {
            List<List<QueryResult>> composedResult = new();

            foreach (QueryResult qr in options)
            {
                // Adjust remaining premises.
                SigmaMap bwdMap = qr.Transformation.CreateBackwardMap();
                List<IMessage> remainingPremises = new(premiseSet.Count);
                for (int i = 1; i < premiseSet.Count; i++)
                {
                    remainingPremises.Add(premiseSet[i].PerformSubstitution(bwdMap));
                }

                // See if they can be satisfied.
                List<List<QueryResult>> furtherOptions = AttemptSatisfyPremises(qf.CloneSubstitutions(), remainingPremises);

                // Perform any adjustments on this premise as it is stored in the composed result.
                // All results in a row must share the same transformation. Note that a zero-length
                // result automatically means a "fail" result.
                for (int i = 0; i < furtherOptions.Count; i++)
                {
                    furtherOptions[i].Add(qr.Transform(furtherOptions[i][0].Transformation));
                }
                composedResult.AddRange(furtherOptions);
            }

            return composedResult;
        }
        else
        {
            return new(from o in options select new List<QueryResult>() { o });
        }
    }

    private List<QueryResult> QueryWork(IMessage queryToFind, QueryFrame qf, List<HornClause> candidates)
    {
        HashSet<HornClause>? noFollows = qf.GetCachedNoFollowClauses(queryToFind);
        if (noFollows != null)
        {
            candidates.RemoveAll((HornClause hc) => noFollows.Contains(hc));
        }
        candidates.Sort(GetSortRulesFor(queryToFind));

        List<QueryResult> options = new();
        foreach (HornClause checkRule in candidates)
        {
            List<QueryResult> qrParts = new();
            if (checkRule.CanResultIn(queryToFind, qf.Guard, out SigmaFactory? sf) &&
                !sf!.AnyContradictionsWithState(qf.StateVariableReplacements))
            {
                HornClause updated = checkRule.Substitute(sf.CreateForwardMap());
                Guard nextGuard = qf.Guard.PerformSubstitution(sf.CreateBackwardMap()).Union(updated.Guard);
                Dictionary<IMessage, IMessage?> updatedStateRepl = sf.UpdateStateReplacements(qf.StateVariableReplacements);

                if (updated.Premises.Count > 0) 
                { 
                    List<IMessage> prioritisedPremises = new(updated.Premises);
                    prioritisedPremises.Sort(PrioritisePremises);
                    QueryFrame innerQF = qf.Next(nextGuard, updatedStateRepl, RatchetRank(checkRule, qf.Rank));
                    List<List<QueryResult>> optionsFound = AttemptSatisfyPremises(innerQF, prioritisedPremises);

                    if (optionsFound.Count == 0)
                    {
                        qf.CacheFailure(queryToFind, checkRule);
                    }
                    else
                    {
                        foreach (List<QueryResult> option in optionsFound)
                        {
                            // All QueryResults in option should have the same transformation based on sf.
                            SigmaFactory retSF = option[0].Transformation;
                            options.Add(QueryResult.Compose(
                                queryToFind,
                                updated.Result.PerformSubstitution(retSF.CreateBackwardMap()),
                                When,
                                retSF,
                                option));
                        }
                    }
                }
                else
                {
                    // If there are no premises, this is one case where the system can just present
                    // a single option.
                    options.Add(QueryResult.ResolvedKnowledge(queryToFind, updated.Result, checkRule, sf));
                    return options;
                }
            }
        }
        return options;
    }

    private List<QueryResult> CheckBasicQuery(IMessage queryToFind, QueryFrame qf)
    {
        List<HornClause> candidates = new(from r in qf.BasicRules
                                          where (r.BeforeRank(qf.Rank) 
                                                 && queryToFind.IsUnifiableWith(r.Result))
                                                 && !r.Premises.Contains(queryToFind)
                                          select r);
        return QueryWork(queryToFind, qf, candidates);
    }

    private List<QueryResult> CheckFunctionQuery(FunctionMessage queryToFind, QueryFrame qf)
    {
        List<HornClause> candidates = new(from r in qf.FunctionRules
                                          where r.Result is FunctionMessage fMsg && fMsg.Name == queryToFind.Name && r.BeforeRank(qf.Rank)
                                          select r);
        return QueryWork(queryToFind, qf, candidates);
    }

    private static int RatchetRank(HornClause current, int rank) => HornClause.RatchetRank(current.Rank, rank);

    private List<QueryResult> CheckTupleQuery(TupleMessage queryToFind, QueryFrame qf)
    {
        // Try to look up elements individually first...
        List<List<QueryResult>> options = AttemptSatisfyPremises(qf, queryToFind.Members);
        List<QueryResult> toReturn = new();
        foreach (List<QueryResult> option in options)
        {
            // For each set of options, determine what the actually result is and generate the
            // transformation for inclusion in the QueryResult.
            TupleMessage tMsg = new(from o in option select o.Actual);
            SigmaFactory sf = new();
            queryToFind.DetermineUnifiableSubstitution(tMsg, Guard.Empty, Guard.Empty, sf);
            toReturn.Add(QueryResult.Compose(queryToFind, tMsg, When, sf, option));
        }

        // ... then see if we can match a result with the whole tuple.
        List<HornClause> candidates = new(from r in qf.BasicRules
                                          where (queryToFind.IsUnifiableWith(r.Result) && r.BeforeRank(qf.Rank))
                                                 && !r.Premises.Contains(queryToFind)
                                          select r);
        toReturn.AddRange(QueryWork(queryToFind, qf, candidates));

        return toReturn;
    }

    private static Comparison<HornClause> GetSortRulesFor(IMessage target)
    {
        return (HornClause hc1, HornClause hc2) =>
        {
            bool hc1Matches = hc1.Result.Equals(target);
            bool hc2Matches = hc2.Result.Equals(target);

            if (hc1Matches && !hc2Matches)
            {
                return -1;
            }
            else if (hc2Matches && !hc1Matches)
            {
                return 1;
            }
            
            int hc1TempRank = hc1.Rank == -1 ? int.MaxValue : hc1.Rank;
            int hc2TempRank = hc2.Rank == -1 ? int.MaxValue : hc2.Rank;
            int cmp = hc2TempRank.CompareTo(hc1TempRank);
            if (cmp == 0)
            {
                cmp = hc1.Variables.Count.CompareTo(hc2.Variables.Count);
                if (cmp == 0)
                {
                    cmp = hc1.Complexity.CompareTo(hc2.Complexity);
                }
            }
            return cmp;
        };
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

    private static readonly List<Type> PremiseTypePriorities = new()
    {
        typeof(FunctionMessage),
        typeof(TupleMessage),
        typeof(NonceMessage),
        typeof(NameMessage),
        typeof(TupleVariableMessage),
        typeof(VariableMessage)
    };

    private static int PrioritisePremises(IMessage msg1, IMessage msg2)
    {
        int msg1Priority = PremiseTypePriorities.IndexOf(msg1.GetType());
        int msg2Priority = PremiseTypePriorities.IndexOf(msg2.GetType());
        int cmp = msg1Priority - msg2Priority;
        if (msg1Priority == msg2Priority && msg1 is FunctionMessage fMsg1)
        {
            FunctionMessage fMsg2 = (FunctionMessage)msg2;
            return fMsg1.FindMaximumDepth().CompareTo(fMsg2.FindMaximumDepth());
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
