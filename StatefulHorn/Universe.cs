using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace StatefulHorn;

public class Universe
{
    public Universe(List<Rule> basisRules)
    {
        _ConsistentRules = new();
        _TransferringRules = new();
        _ChangeLog = new();
        BasisRules = basisRules;

        StartGeneration();
        BasisRulesHaveRedundancy = false;
        foreach (Rule r in basisRules)
        {
            (AddDecision dec, List<Rule>? _) = AddRule(r);
            BasisRulesHaveRedundancy |= (dec != AddDecision.IsNew);
        }
        EndGeneration();
    }

    public IReadOnlyList<Rule> BasisRules { get; init; }

    /// <summary>
    /// True if one or more of the basis rules implied other basis rules.
    /// </summary>
    public bool BasisRulesHaveRedundancy { get; init; }

    #region Change log management.

    public enum AddDecision
    {
        IsNew,
        IsImplied,
        ImpliesAnother
    }

    public record ChangeLogEntry(Rule AttemptedRule, AddDecision Decision, List<Rule>? AffectedRules);

    private List<ChangeLogEntry>? CurrentGeneration;
    
    private void StartGeneration()
    {
        CurrentGeneration = new();
    }

    private List<ChangeLogEntry> EndGeneration()
    {
        Debug.Assert(CurrentGeneration != null, "EndGeneration() is to be used only after StartGeneration().");
        List<ChangeLogEntry> allGenEntries = CurrentGeneration!;
        _ChangeLog.Add(CurrentGeneration!);
        CurrentGeneration = null;
        return allGenEntries;
    }

    public bool IsExhausted => ChangeLog.Count > 0 && ChangeLog[^1].Count == 0;

    private readonly List<List<ChangeLogEntry>> _ChangeLog;

    public IReadOnlyList<IReadOnlyList<ChangeLogEntry>> ChangeLog => _ChangeLog;

    #endregion
    #region Rule addition and list management.

    private (AddDecision, List<Rule>?) AddRule(Rule r)
    {
        (AddDecision, List<Rule>?) change;
        if (r is StateConsistentRule scr)
        {
            change = AddRuleToList(scr, _ConsistentRules);
        }
        else if (r is StateTransferringRule str)
        {
            change = AddRuleToList(str, _TransferringRules);
        }
        else
        {
            throw new NotImplementedException("Neither state consistent or transferring rule.");
        }
        CurrentGeneration!.Add(new(r, change.Item1, change.Item2));
        return change;
    }

    private static (AddDecision, List<Rule>?) AddRuleToList<T>(T rule, List<T> ruleList) where T : Rule
    {
        List<Rule> othersImplied = new();
        foreach (T rb in ruleList)
        {
            if (rb.CanImply(rule, out SigmaMap? _))
            {
                return (AddDecision.IsImplied, new() { rb });
            }
            if (rule.CanImply(rb, out SigmaMap? _))
            {
                othersImplied.Add(rb);
            }
        }
        if (othersImplied.Count > 0)
        {
            foreach (T implied in othersImplied)
            {
                ruleList.Remove(implied);
            }
            ruleList.Add(rule);
            return (AddDecision.ImpliesAnother, othersImplied);
        }
        ruleList.Add(rule);
        return (AddDecision.IsNew, null);
    }

    private void AddRules(List<Rule> newRules)
    {
        foreach (Rule r in newRules)
        {
            _ = AddRule(r);
        }
    }

    private readonly List<StateConsistentRule> _ConsistentRules;

    public IReadOnlyList<StateConsistentRule> ConsistentRules => _ConsistentRules;

    private readonly List<StateTransferringRule> _TransferringRules;

    public IReadOnlyList<StateTransferringRule> TransferringRules => _TransferringRules;

    #endregion

    public IReadOnlyList<ChangeLogEntry> GenerateNextRuleSet()
    {
        // FIXME: Ensure that a list of names and nonces is kept for the state instantiation.
        // Such a list will provide options for the SigmaMap to use.
        // FIXME: Actually track the trivially newly determined events (N), rather than just doing
        // this naively.
        // Note that rules are added in batches - this is to prevent issues with iterators
        // being invalidated by changes to the underlying lists.

        if (IsExhausted)
        {
            return _ChangeLog[^1];
        }

        StartGeneration();

        // 1. Attempt state composition.
        List<Rule> newRules = new();
        for (int i = 0; i < _ConsistentRules.Count; i++)
        {
            StateConsistentRule composer = _ConsistentRules[i];
            for (int j = 0; j < _ConsistentRules.Count; j++)
            {
                if (i != j && composer.TryComposeWith(_ConsistentRules[j], out Rule? newRule))
                {
                    Debug.Assert(newRule != null);
                    newRules.Add(newRule);
                }
            }
            foreach (StateTransferringRule str in _TransferringRules)
            {
                if (composer.TryComposeWith(str, out Rule? newRule))
                {
                    Debug.Assert(newRule != null);
                    newRules.Add(newRule);
                }
            }
        }
        AddRules(newRules);

        // 2. Attempt state unification.
        newRules.Clear();
        foreach (StateConsistentRule possUnif in _ConsistentRules)
        {
            if (possUnif.ResultIsTerminating)
            {
                newRules.AddRange(possUnif.GenerateStateUnifications());
            }
        }
        AddRules(newRules);

        // 3. Attempt state transformation.
        newRules.Clear();
        foreach (StateTransferringRule str in _TransferringRules)
        {
            foreach (StateConsistentRule scr in _ConsistentRules)
            {
                StateConsistentRule? newRule = str.Transform(scr);
                if (newRule != null)
                {
                    newRules.Add(newRule);
                }
            }
        }
        AddRules(newRules);

        // 4. Attempt state instantiation.
        // FIXME: Actually implement based on the new items in N.

        return EndGeneration();
    }

}
