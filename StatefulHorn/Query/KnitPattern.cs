using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StatefulHorn.Query;

/// <summary>
/// Used to group State Transfer Rules for optimal application to Nessions.
/// </summary>
public class KnitPattern
{

    private record Relationships
    (
        StateTransferringRule Rule,
        BitArray AffectedStates, // Used as BitVector32 is does not easily handle variable lengths.
        List<int> Exclusions,
        HashSet<int> ConcurrentSCRs,
        HashSet<int> ResultSCRs,
        bool Excluded
    );

    private KnitPattern(List<Relationships> lt)
    {
        LookupTable = lt;
        CreateCompatibilityMatrix();
        Debug.Assert(CompatibilityMatrix != null);
    }

    private readonly List<Relationships> LookupTable;

    private bool[,] CompatibilityMatrix;

    #region Creation.

    private void CreateCompatibilityMatrix()
    {
        // Build a map of the conflicts - consider making class resident.
        CompatibilityMatrix = new bool[LookupTable.Count, LookupTable.Count];
        for (int i = 0; i < LookupTable.Count; i++)
        {
            for (int j = i + 1; j < LookupTable.Count; j++)
            {
                bool v = RulesCompatible(i, j);
                CompatibilityMatrix[i, j] = v;
                CompatibilityMatrix[j, i] = v;
            }
        }
    }

    private bool RulesCompatible(int r1, int r2)
    {
        Relationships rel1 = LookupTable[r1];
        Relationships rel2 = LookupTable[r2];
        BitArray working = new(rel1.AffectedStates);
        // Ensure don't affect same states, nor interfere with each other's applicable rules.
        return !(working.And(rel2.AffectedStates).Cast<bool>().Contains(true) ||
                 rel1.ResultSCRs.Intersect(rel2.ConcurrentSCRs).Any() ||
                 rel2.ResultSCRs.Intersect(rel1.ConcurrentSCRs).Any());
    }

    public static KnitPattern From(List<StateTransferringRule> strs, List<StateConsistentRule> scrs)
    {
        // The first objective is to create the Ids. This will allow quicker operations on 
        // Transfer Rules.
        HashSet<string> allStates = new();
        foreach (StateTransferringRule str in strs)
        {
            allStates.UnionWith(from s in str.Snapshots.States select s.Name);
        }
        List<string> allStatesSorted = allStates.ToList();
        allStatesSorted.Sort();

        // The second objective is to generate bit patterns indicating which states are used by
        // each rule.
        List<Relationships> table = new(strs.Count);
        for (int i = 0; i < strs.Count; i++)
        {
            StateTransferringRule rule = strs[i];
            // Inefficient, but simple algorithm for creating bit lookup table for state usage.
            BitArray affectedStates = new(allStates.Count);
            foreach (State s in from rt in rule.Result.Transformations select rt.Condition)
            {
                for (int searchI = 0; searchI < allStatesSorted.Count; searchI++)
                {
                    if (allStatesSorted[searchI] == s.Name)
                    {
                        affectedStates[searchI] = true;
                        continue;
                    }
                }
            }
            table.Add(new Relationships(rule, affectedStates, new(), new(), new(), false));
        }

        // Now determine the relationships.
        for (int i = 0; i < strs.Count; i++)
        {
            Relationships thisR = table[i];
            for (int j = 0; j < strs.Count; j++)
            {
                if (i != j)
                {
                    // Can this be applied at the same time as other?
                    Relationships otherR = table[i];
                    BitArray working = new(thisR.AffectedStates);
                    if (!working.And(otherR.AffectedStates).Cast<bool>().Contains(true))
                    {
                        thisR.Exclusions.Add(j);
                    }
                }
            }

            // Are there state consistent rules that depend on this rule to be applied?
            for (int k = 0; k < scrs.Count; k++)
            {
                if (RuleConcurrent(thisR.Rule, scrs[k]))
                {
                    thisR.ConcurrentSCRs.Add(k);
                }
                if (RulePreceeds(thisR.Rule, scrs[k]))
                {
                    thisR.ResultSCRs.Add(k);
                }
            }
        }

        return new(table);
    }

    private static bool RuleConcurrent(Rule r, Rule other)
    {
        List<State> reqStates = new(from s in r.Snapshots.Traces select s.Condition);
        List<State> otherStates = new(from s in other.Snapshots.Traces select s.Condition);

        foreach (State os in otherStates)
        {
            foreach (State rs in reqStates)
            {
                if (os.CanBeUnifiableWith(rs, other.Guard, r.Guard, new()))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool RulePreceeds(Rule r, Rule other)
    {
        // What is the result of this rule running?
        List<State> resultStates = new();
        foreach (Snapshot s in r.Snapshots.Traces)
        {
            if (s.TransfersTo != null)
            {
                resultStates.Add(s.TransfersTo);
            }
        }

        // What does the other rule require to work?
        List<State> otherStates = new(from s in other.Snapshots.Traces select s.Condition);

        foreach (State os in otherStates)
        {
            foreach (State rs in resultStates)
            {
                if (os.CanBeUnifiableWith(rs, other.Guard, r.Guard, new()))
                {
                    return true;
                }
            }
        }
        return false;
    }

    #endregion
    #region Querying.

    public List<(int, SigmaFactory)> GetMatchingTransferRules(Nession n)
    {
        List<(int, SigmaFactory)> matches = new();

        for (int i = 0; i < LookupTable.Count; i++)
        {
            Relationships r = LookupTable[i];
            if (r.Excluded)
            {
                continue;
            }
            SigmaFactory sf = new();
            if (n.CanApplyRule(r.Rule, sf))
            {
                matches.Add((i, sf!));
            }
        }

        return matches;
    }

    public List<List<StateTransferringRule>> GetTransferGroups(Nession n)
    {
        List<(int, SigmaFactory)> matchingTR = GetMatchingTransferRules(n);
        List<List<StateTransferringRule>> groups = new();

        List<int> emptyGroup = new();
        foreach ((int ruleId, SigmaFactory sf) in matchingTR)
        {
            if (sf.NotBackward)
            {
                emptyGroup.Add(ruleId);
            }
            else
            {
                groups.Add(new() { LookupTable[ruleId].Rule });
            }
        }

        bool changed = false;
        // Group together as many non-conflicting changes as possible.
        List<HashSet<int>> combinations = new(from eg in emptyGroup select new HashSet<int>() { eg });
        List<bool> combinedEarlier = new();
        do
        {
            List<HashSet<int>> nextCombinations = new();

            changed = false;
            combinedEarlier.Clear();
            for (int i = 0; i < combinations.Count; i++)
            {
                combinedEarlier.Add(false);
            }

            for (int i = 0; i < combinations.Count; i++)
            {
                bool combined = false;
                for (int j = i + 1; j < combinations.Count; j++)
                {
                    if (CombinationsCompatible(combinations[i], combinations[j]) &&
                        TryAddToList(nextCombinations, new(combinations[i].Concat(combinations[j]))))
                    {
                        combined = true;
                        combinedEarlier[j] = true;
                    }
                }
                if (!(combined || combinedEarlier[i]))
                {
                    TryAddToList(nextCombinations, combinations[i]);
                }
                changed |= combined;
            }

            combinations = nextCombinations;
        } while (changed);

        foreach (HashSet<int> comb in combinations)
        {
            List<StateTransferringRule> rules = new(from c in comb select LookupTable[c].Rule);
            groups.Add(rules);
        }
        return groups;
    }

    private bool CombinationsCompatible(HashSet<int> comb1, HashSet<int> comb2)
    {
        foreach (int c1 in comb1)
        {
            foreach (int c2 in comb2)
            {
                if (c1 != c2 && !CompatibilityMatrix[c1, c2])
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool TryAddToList(List<HashSet<int>> combinations, HashSet<int> latest)
    {
        for (int i = 0; i < combinations.Count; i++)
        {
            HashSet<int> held = combinations[i];
            // Is the exact sequence held?
            if (latest.IsSubsetOf(held))
            {
                return false;
            }
            if (held.IsSubsetOf(latest))
            {
                combinations.RemoveAt(i);
                i--;
            }
        }
        combinations.Add(latest);
        return true;
    }

    #endregion

}
