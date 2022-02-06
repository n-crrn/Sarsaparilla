using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatefulHorn;

/// <summary>
/// Version of the Query Engine which is designed to work with Nession structures to 
/// test a protocol.
/// </summary>
public class NessionQueryEngine
{

    public NessionQueryEngine(
        IEnumerable<State> init,
        List<StateConsistentRule> systemRules,
        List<StateTransferringRule> transferringRules)
    {
        InitialConditions = new(init);
        SystemRules = systemRules;
        TransferringRules = transferringRules;
    }

    // FIXME: Include method to check that the query is sensible.

    #region Properties.

    public HashSet<State> InitialConditions { get; init; }

    public List<StateConsistentRule> SystemRules { get; init; }

    public List<StateTransferringRule> TransferringRules { get; init; }

    public IEnumerable<Rule> NewEventRules()
    {
        foreach (StateConsistentRule sysRule in SystemRules)
        {
            if (sysRule.NewNonces.Any())
            {
                yield return sysRule;
            }
        }
        foreach (StateTransferringRule transferRule in TransferringRules)
        {
            if (transferRule.NonceDeclarations.Any())
            {
                yield return transferRule;
            }
        }
    }

    #endregion
    #region Horn clause generation.

    private List<Nession>? InitialNessions;

    private List<Nession>? NonceNessions;

    private IEnumerable<Nession> AllNessions()
    {
        if (InitialNessions != null && NonceNessions != null)
        {
            foreach (Nession n in InitialNessions)
            {
                yield return n;
            }
            foreach (Nession nn in NonceNessions)
            {
                yield return nn;
            }
        }
    }

    public void Elaborate()
    {
        Nession initSeed = new(InitialConditions);
        IEnumerable<Nession> nonceSeeds = from r in NewEventRules() select Nession.FromRule(r);
        List<Nession> initSeedList = new() { initSeed };
        List<List<Nession>> nonceSeedLists = new(from ns in nonceSeeds select new List<Nession>() { ns });

        const int numberOfSubElaborations = 10;

        int initSeedStart = 0;
        int initSeedEnd = initSeedList.Count;
        List<(int Start, int End)> nonceStartEnd = new();
        for (int i = 0; i < nonceSeedLists.Count; i++)
        {
            nonceStartEnd.Add((0, 1));
        }

        // Determine what states are possible.
        for (int elabCounter = 0; elabCounter < numberOfSubElaborations; elabCounter++)
        {
            foreach (StateTransferringRule transferRule in TransferringRules)
            {
                for (int i = initSeedStart; i < initSeedEnd; i++)
                {
                    Nession? n = initSeedList[i].TryApplyTransfer(transferRule);
                    if (n != null)
                    {
                        initSeedList.Add(n);
                    }
                }
                for (int i = 0; i < nonceStartEnd.Count; i++)
                {
                    for (int j = nonceStartEnd[i].Start; j < nonceStartEnd[i].End; j++)
                    {
                        Nession? n = nonceSeedLists[i][j].TryApplyTransfer(transferRule);
                        if (n != null)
                        {
                            nonceSeedLists[i].Add(n);
                        }
                    }
                }
            }
            for (int i = 0; i < nonceSeedLists.Count; i++)
            {
                nonceStartEnd[i] = (nonceStartEnd[i].End, nonceSeedLists.Count);
            }
        }

        List<Nession> nonceNessions = new();
        foreach (List<Nession> nList in nonceSeedLists)
        {
            nonceNessions.AddRange(nList);
        }

        // Determine what knowledge can be gained in all states.
        foreach (StateConsistentRule scr in SystemRules)
        {
            List<Nession> updatedInitNessions = new();
            List<Nession> updatedNonceNessions = new();

            foreach (Nession initN in initSeedList)
            {
                updatedInitNessions.AddRange(initN.TryApplySystemRule(scr));
            }
            foreach (Nession nonceN in nonceNessions)
            {
                updatedNonceNessions.AddRange(nonceN.TryApplySystemRule(scr));
            }

            initSeedList = updatedInitNessions;
            nonceNessions = updatedNonceNessions;
        }

        InitialNessions = initSeedList;
        NonceNessions = nonceNessions;
    }

    public HashSet<HornClause> GenerateHornClauses()
    {
        if (InitialNessions == null)
        {
            Elaborate();
        }
        Debug.Assert(InitialNessions != null && NonceNessions != null);

        HashSet<HornClause> clauses = new();
        foreach (Nession n in AllNessions())
        {
            HashSet<IMessage> premises = new();
            foreach (Nession.Frame frame in n.History)
            {
                premises.UnionWith(from fp in frame.Premises where fp.IsKnow select fp.Messages.Single());
                foreach (Event result in frame.Results)
                {
                    clauses.Add(new(result.Messages.Single(), premises));
                }
            }
        }
        return clauses;
    }

    #endregion
    #region State querying.

    public List<Nession> MessageFoundWhen(IMessage msg, State when)
    {
        Guard g = new();
        Event knowMessage = Event.Know(msg);
        List<Nession> matches = new();
        foreach (Nession n in AllNessions())
        {
            foreach (Nession.Frame frame in n.History)
            {
                foreach (State s in frame.StateSet)
                {
                    if (when.CanBeUnifiedTo(s, g, new()) && frame.Results.Contains(knowMessage))
                    {
                        matches.Add(n);
                        goto breakoutPoint;
                    }
                }
            }
        breakoutPoint:;
        }
        return matches;
    }

    #endregion
}
