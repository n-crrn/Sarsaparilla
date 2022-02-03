using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatefulHorn;

public class QueryEngine
{

    public QueryEngine(IMessage q, List<Rule> userRules)
    {
        Query = q;

        // Filter rules and store in their appropriate buckets.
        foreach (Rule r in userRules)
        {
            if (r is StateConsistentRule scr)
            {
                if (scr.IsFact)
                {
                    FactRules.Add(scr);
                }
                else if (scr.IsStateless)
                {
                    KnowledgeRules.Add(scr);
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
                throw new NotImplementedException("Unknown rule type.");
            }
        }

        EstablishFacts();
        InitialElaborate();
    }

    public IMessage Query { get; init; }

    public HashSet<StateConsistentRule> FactRules { get; } = new();

    public HashSet<StateConsistentRule> KnowledgeRules { get; } = new();

    public HashSet<StateConsistentRule> SystemRules { get; } = new();

    public IEnumerable<StateConsistentRule> ConsistentRules()
    {
        foreach (StateConsistentRule r in FactRules)
        {
            yield return r;
        }
        foreach (StateConsistentRule r in KnowledgeRules)
        {
            yield return r;
        }
        foreach (StateConsistentRule r in SystemRules)
        {
            yield return r;
        }
    }

    public List<StateTransferringRule> TransferringRules { get; } = new();

    public IEnumerable<Rule> Rules()
    {
        foreach (Rule r in ConsistentRules())
        {
            yield return r;
        }
        foreach (Rule r in TransferringRules)
        {
            yield return r;
        }
    }

    public List<StateConsistentRule> MatchingRules { get; } = new();

    public Dictionary<IMessage, HashSet<StateConsistentRule>> RulesByFact = new();

    public HashSet<IMessage> Facts { get; } = new();
    
    private void EstablishFacts()
    {
        foreach (StateConsistentRule scr in FactRules)
        {
            IMessage resultMsg = scr.Result.Messages[0];
            Facts.Add(resultMsg);
            TryAddToKnownFacts(Facts, resultMsg, scr);
        }
    }

    private HashSet<IMessage> NextGenFacts = new();

    private readonly HashSet<StateConsistentRule> NextGenSystemRules = new();

    private void InitialElaborate()
    {
        // Two runs are conducted here for knowledge introduction. The first is to attempt
        // to replace whole premises
        // with known, compound facts. This allows for the extraction of contained facts
        // within those compound facts. The second run will directly replace variables with
        // facts one-to-one.

        HashSet<StateConsistentRule> newRules = new();
        List<IMessage> compoundFacts = new(from f in Facts where f is FunctionMessage || f is TupleMessage select f);
        foreach (StateConsistentRule stateless in KnowledgeRules)
        {
            if (stateless.HasPremiseVariables)
            {
                foreach (IMessage cFact in compoundFacts)
                {
                    foreach (Event premise in stateless.Premises)
                    {
                        SigmaFactory sf = new();
                        if (premise.IsKnow && premise.Messages[0].DetermineUnifiedToSubstitution(cFact, stateless.GuardStatements, sf))
                        {
                            StateConsistentRule ruleToAdd = (StateConsistentRule)stateless.PerformSubstitution(sf.CreateForwardMap());
                            if (newRules.Add(ruleToAdd))
                            {
                                NextGenFacts.Add(ruleToAdd.Result.Messages[0]);
                            }
                        }
                    }
                }
            }
        }
        KnowledgeRules.UnionWith(newRules);
        newRules.Clear();

        foreach (StateConsistentRule stateless in KnowledgeRules)
        {
            List<IMessage> ruleVariables = stateless.PremiseVariables.ToList();
            if (ruleVariables.Count > 0)
            {
                // Direct replacement run.
                foreach (List<IMessage> factPerm in Permutations(Facts.ToList(), ruleVariables.Count))
                {
                    List<(IMessage Variables, IMessage Value)> subsList = Zip(ruleVariables, factPerm);
                    SigmaMap map = new(subsList);
                    StateConsistentRule newRule = (StateConsistentRule)stateless.PerformFactsSubstitution(map);
                    newRules.Add(newRule);
                }
            }
        }
        KnowledgeRules.UnionWith(newRules);
        newRules.Clear();

        if (CheckQuery())
        {
            return; // Nothing further is required if we have found the answer.
        }

        foreach (StateConsistentRule scr in SystemRules)
        {
            StateConsistentRule? compressed = scr.TryCompressStates();
            if (compressed != null)
            {
                newRules.Add(compressed);
            }
        }
        SystemRules.UnionWith(newRules);
        newRules.Clear();

        ElaborateSystemRules();

        CheckQuery();
    }

    public bool CheckQuery()
    {
        MatchingRules.Clear();
        IEnumerable<StateConsistentRule> candidates = ConsistentRules();
        List<StateConsistentRule> toTryNext = new();
        bool addedToKnown = false;
        HashSet<IMessage> newFoundFacts = new();
        foreach (StateConsistentRule scr in candidates)
        {
            if (scr.IsResolved)
            {
                if (scr.AreAllPremisesKnown(Facts, newFoundFacts))
                {
                    if (Facts.Add(scr.Result.Messages[0]))
                    {
                        TryAddToKnownFacts(newFoundFacts, scr.Result.Messages[0], scr);
                        addedToKnown = true;
                    }
                }
                else
                {
                    toTryNext.Add(scr);
                }
            }
        }
        while (addedToKnown)
        {
            candidates = toTryNext;
            toTryNext = new();
            addedToKnown = false;
            foreach (StateConsistentRule scr in candidates)
            {
                if (scr.AreAllPremisesKnown(Facts, newFoundFacts))
                {
                    if (Facts.Add(scr.Result.Messages[0]))
                    {
                        TryAddToKnownFacts(newFoundFacts, scr.Result.Messages[0], scr);
                        addedToKnown = true;
                    }
                }
                else
                {
                    toTryNext.Add(scr);
                }
            }
        }
        NextGenFacts = newFoundFacts;

        if (RulesByFact.TryGetValue(Query, out HashSet<StateConsistentRule>? foundMatches))
        {
            MatchingRules.AddRange(foundMatches);
        }
        else
        {
            // If there is no match, attempt to work backwards from the query itself to see if the
            // pieces have been found.
            if (Query is TupleMessage || Query is FunctionMessage)
            {
                (bool queryKnown, List<IMessage>? subFacts) = IsSubMessageKnown(Facts, Query);
                if (queryKnown)
                {
                    Debug.Assert(subFacts != null);
                    foreach (IMessage subFact in subFacts)
                    {
                        MatchingRules.AddRange(RulesByFact[subFact]);
                    }
                }
            }
        }
        return MatchingRules.Count > 0;
    }

    private static (bool, List<IMessage>?) IsSubMessageKnown(HashSet<IMessage> rs1, IMessage premiseMessage)
    {
        if (rs1.Contains(premiseMessage))
        {
            return (true, new() { premiseMessage });
        }

        List<IMessage> subMessagesToTry = new();
        if (premiseMessage is TupleMessage tMsg)
        {
            subMessagesToTry.AddRange(tMsg.Members);
        }
        else if (premiseMessage is FunctionMessage fMsg)
        {
            subMessagesToTry.AddRange(fMsg.Parameters);
        }

        if (subMessagesToTry.Count > 0)
        {
            List<IMessage> knownMessages = new();
            foreach (IMessage member in subMessagesToTry)
            {
                (bool found, List<IMessage>? latestReturn) = IsSubMessageKnown(rs1, member);
                if (!found)
                {
                    return (false, null);
                }
                knownMessages.AddRange(latestReturn!);
            }
            return (true, knownMessages);
        }
        return (false, null);
    }

    private void TryAddToKnownFacts(HashSet<IMessage> factsSet, IMessage newFact, StateConsistentRule scr)
    {
        if (RulesByFact.TryGetValue(newFact, out HashSet<StateConsistentRule>? ruleList))
        {
            ruleList!.Add(scr);
        }
        else
        {
            RulesByFact[newFact] = new() { scr };
        }

        if (factsSet.Add(newFact) && newFact is TupleMessage tMsg)
        {
            foreach (IMessage msg in tMsg.Members)
            {
                TryAddToKnownFacts(factsSet, msg, scr);
            }
        }
    }

    private static IEnumerable<List<IMessage>> Permutations(List<IMessage> facts, int permLength)
    {
        if (permLength == 0)
        {
            yield return new();
        }
        else
        {
            List<IMessage> perm = new();
            for (int i = 0; i < facts.Count; i++)
            {
                perm.Clear();
                perm.Add(facts[i]);
                foreach (List<IMessage> innerPerm in Permutations(facts, permLength - 1))
                {
                    perm.AddRange(innerPerm);
                    yield return perm;
                    perm.RemoveRange(1, perm.Count - 1);
                }
            }
        }
    }

    private static List<(IMessage Variable, IMessage Value)> Zip(List<IMessage> variables, List<IMessage> values)
    {
        Debug.Assert(variables.Count == values.Count);
        List<(IMessage Variable, IMessage Value)> subs = new(variables.Count);
        for (int i = 0; i < variables.Count; i++)
        {
            subs.Add((variables[i], values[i]));
        }
        return subs;
    }

    public void Elaborate()
    {
        HashSet<IMessage> newFacts = new();
        HashSet<StateConsistentRule> newRules = new();
        List<IMessage> compoundFacts = new(from f in NextGenFacts where f is FunctionMessage || f is TupleMessage select f);
        foreach (StateConsistentRule stateless in KnowledgeRules)
        {
            List<IMessage> ruleVariables = stateless.PremiseVariables.ToList();
            if (ruleVariables.Count > 0)
            {
                // Two runs are conducted here. The first is to attempt to replace whole premises
                // with known, compound facts. This allows for the extraction of contained facts
                // within those compound facts. The second run will directly replace variables with
                // facts one-to-one.

                // Unifying run.
                foreach (IMessage cFact in compoundFacts)
                {
                    foreach (Event premise in stateless.Premises)
                    {
                        SigmaFactory sf = new();
                        if (premise.IsKnow && premise.Messages[0].DetermineUnifiedToSubstitution(cFact, stateless.GuardStatements, sf))
                        {
                            StateConsistentRule ruleToAdd = (StateConsistentRule)stateless.PerformSubstitution(sf.CreateForwardMap());
                            newRules.Add(ruleToAdd);
                        }
                    }
                }

                // Direct replacement run.
                foreach (List<IMessage> factPerm in Permutations(Facts.ToList(), ruleVariables.Count))
                {
                    // Skip the combination if it has not been done before.
                    if (factPerm.Any((IMessage msg) => NextGenFacts!.Contains(msg)))
                    {
                        List<(IMessage Variables, IMessage Value)> subsList = Zip(ruleVariables, factPerm);
                        SigmaMap map = new(subsList);
                        StateConsistentRule newRule = (StateConsistentRule)stateless.PerformFactsSubstitution(map);
                        newRules.Add(newRule);
                    }
                }
            }
        }
        Facts.UnionWith(newFacts);
        KnowledgeRules.UnionWith(newRules);
        NextGenFacts = newFacts;

        ElaborateSystemRules();

        CheckQuery();
    }

    private void ElaborateSystemRules()
    {
        HashSet<StateConsistentRule> newRules = new();

        foreach (StateTransferringRule str in TransferringRules)
        {
            foreach (StateConsistentRule scr in SystemRules)
            {
                StateConsistentRule? derived = str.TryTransform(scr);
                if (derived != null)
                {
                    newRules.Add(derived);
                }
            }
        }
        SystemRules.UnionWith(newRules);
        newRules.Clear();

        List<StateConsistentRule> sysRulesList = SystemRules.ToList();
        for (int i = 0; i < sysRulesList.Count; i++)
        {
            for (int j = 0; j < sysRulesList.Count; j++)
            {
                if (i != j)
                {
                    StateConsistentRule? derived = sysRulesList[i].TryComposeUpon(sysRulesList[j]);
                    if (derived != null)
                    {
                        newRules.Add(derived);
                    }
                }
            }
        }
    }
}
