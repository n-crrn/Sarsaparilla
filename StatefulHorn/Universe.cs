using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

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

    private readonly List<StateConsistentRule> _ConsistentRules;

    public IReadOnlyList<StateConsistentRule> ConsistentRules => _ConsistentRules;

    private readonly List<StateTransferringRule> _TransferringRules;

    public IReadOnlyList<StateTransferringRule> TransferringRules => _TransferringRules;

    public int RuleCount => _ConsistentRules.Count + _TransferringRules.Count;

    #endregion

    public record StatusReporter(Action OnStart, Action<Status> OnMessage, Action OnEnd);

    public record Status(string StatusDescription,
                         int CompositionsAttempted = 0,
                         int CompositionsFound = 0,
                         int CompositionsAdded = 0,
                         int StateUnificationsFound = 0,
                         int StateUnificationsAdded = 0,
                         int StateTransformationsFound = 0,
                         int StateTransformationsAdded = 0);

    private static readonly int ReportEveryCount = 100;

    private async Task AddRules(List<Rule> newRules, Action<int> ruleCountChanged)
    {
        int attemptedAdds = 0;
        foreach (Rule r in newRules)
        {
            _ = AddRule(r);
            attemptedAdds++;
            if (attemptedAdds % ReportEveryCount == 0)
            {
                ruleCountChanged(attemptedAdds);
                await Task.Delay(1);
            }
        }
        ruleCountChanged(attemptedAdds);
        await Task.Delay(1);
    }

    public async Task<IReadOnlyList<ChangeLogEntry>> GenerateNextRuleSet(StatusReporter reporter)
    {
        // FIXME: Ensure that a list of names and nonces is kept for the state instantiation.
        // Such a list will provide options for the SigmaMap to use.
        // FIXME: Actually track the trivially newly determined events (N), rather than just doing
        // this naively.
        // Note that rules are added in batches - this is to prevent issues with iterators
        // being invalidated by changes to the underlying lists.

        // Note that Task.Delay(1) is used instead of Task.Yield(). I have found that Yield does 
        // not consistently prompt the user interface.

        // The integration of status notification below is not elegant. I have not refined it as I
        // intend to completely rework this elaboration algorithm.

        if (IsExhausted)
        {
            return _ChangeLog[^1];
        }

        StartGeneration();

        // 1. Attempt state composition.
        int compsAttempted = 0;
        int compsFound = 0;
        string statusDescription = "Attempting state compositions…";
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
                    compsFound++;
                }
                compsAttempted++;
                if (compsAttempted % ReportEveryCount == 0)
                {
                    reporter.OnMessage(new(statusDescription, compsAttempted, compsFound, 0, 0, 0, 0, 0));
                    await Task.Delay(1);
                }
            }
            foreach (StateTransferringRule str in _TransferringRules)
            {
                if (composer.TryComposeWith(str, out Rule? newRule))
                {
                    Debug.Assert(newRule != null);
                    newRules.Add(newRule);
                    compsFound++;
                }
                compsAttempted++;
                if (compsAttempted % ReportEveryCount == 0)
                {
                    reporter.OnMessage(new(statusDescription, compsAttempted, compsFound, 0, 0, 0, 0, 0));
                    await Task.Delay(1);
                }
            }
        }
        Status addRuleStatus = new($"Attempting to add {newRules.Count} to rules ({RuleCount})…", compsAttempted, compsFound, 0, 0, 0, 0, 0);
        int rulesAddAttemptCount = 0;
        await AddRules(newRules, (int latestRuleAddAttemptCount) =>
        {
            rulesAddAttemptCount = latestRuleAddAttemptCount;
            reporter.OnMessage(addRuleStatus with { CompositionsAdded = rulesAddAttemptCount });
        });

        // 2. Attempt state unification.
        Status stateUnifStatus = addRuleStatus with
        {
            StatusDescription = "Attempting state unifications…",
            CompositionsAdded = rulesAddAttemptCount 
        };
        int stateUnificationsFound = 0;
        newRules.Clear();
        foreach (StateConsistentRule possUnif in _ConsistentRules)
        {
            if (possUnif.ResultIsTerminating)
            {
                newRules.AddRange(possUnif.GenerateStateUnifications());
                stateUnificationsFound++;
                if (stateUnificationsFound % ReportEveryCount == 100)
                {
                    reporter.OnMessage(stateUnifStatus with { StateUnificationsFound = stateUnificationsFound });
                    await Task.Delay(1);
                }
            }
        }
        reporter.OnMessage(stateUnifStatus with { StateUnificationsFound = stateUnificationsFound });
        Status addUnifiedRulesStatus = stateUnifStatus with 
        { 
            StatusDescription = $"Attempting to add {newRules.Count} unifications to rules ({RuleCount})…",
            StateUnificationsFound = stateUnificationsFound
        };
        rulesAddAttemptCount = 0;
        await AddRules(newRules, (int latestRuleAddAttemptCount) =>
        {
            rulesAddAttemptCount = latestRuleAddAttemptCount;
            reporter.OnMessage(addUnifiedRulesStatus with { StateUnificationsAdded = rulesAddAttemptCount });
        });

        // 3. Attempt state transformation.
        newRules.Clear();
        Status stateTransStatus = addUnifiedRulesStatus with
        {
            StatusDescription = "Attempting state transformations…",
            StateUnificationsAdded = rulesAddAttemptCount
        };
        int stateTransformationsFound = 0;
        foreach (StateTransferringRule str in _TransferringRules)
        {
            foreach (StateConsistentRule scr in _ConsistentRules)
            {
                StateConsistentRule? newRule = str.Transform(scr);
                if (newRule != null)
                {
                    newRules.Add(newRule);
                    stateTransformationsFound++;
                    if (stateTransformationsFound % ReportEveryCount == 100)
                    {
                        reporter.OnMessage(stateTransStatus with { StateTransformationsFound = stateTransformationsFound });
                        await Task.Delay(1);
                    }
                }
            }
        }
        stateTransStatus = stateTransStatus with
        {
            StatusDescription = $"Adding {newRules.Count} state transformations to ruleset ({RuleCount})…",
            StateTransformationsFound = stateTransformationsFound
        };
        rulesAddAttemptCount = 0;
        await AddRules(newRules, (int latestRuleAddAttemptCount) =>
        {
            rulesAddAttemptCount = latestRuleAddAttemptCount;
            reporter.OnMessage(stateTransStatus with { StateTransformationsAdded = rulesAddAttemptCount });
        });

        // 4. Attempt state instantiation.
        // FIXME: Actually implement based on the new items in N.

        reporter.OnEnd();
         
        return EndGeneration();
    }

}
