using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatefulHorn;

/// <summary>
/// Version of the Query Engine which is designed to work with Nession structures to 
/// test a protocol.
/// </summary>
public class NessionManager
{

    public NessionManager(
        IEnumerable<State> init,
        List<StateConsistentRule> systemRules,
        List<StateTransferringRule> transferringRules)
    {
        InitialConditions = new(init);
        SystemRules = systemRules;
        TransferringRules = transferringRules;

        for (int idTag = 0; idTag < SystemRules.Count; idTag++)
        {
            SystemRules[idTag].IdTag = idTag;
        }
    }

    // FIXME: Include method to check that the query is sensible.

    #region Properties.

    public HashSet<State> InitialConditions { get; init; }

    public List<StateConsistentRule> SystemRules { get; init; }

    public List<StateTransferringRule> TransferringRules { get; init; }

    #endregion
    #region Horn clause generation.

    private List<Nession>? InitialNessions;

    public IEnumerable<Nession> GeneratedNessions()
    {
        if (InitialNessions != null /*&& NonceNessions != null*/)
        {
            foreach (Nession n in InitialNessions)
            {
                yield return n;
            }
        }
    }

    private bool CancelElaborate = false;

    public async Task Elaborate()
    {
        if (CancelElaborate)
        {
            CancelElaborate = false;
        }
        Nession initSeed = new(InitialConditions);
        List<Nession> initSeedList = new() { initSeed };

        int initSeedStart = 0;
        int initSeedEnd = initSeedList.Count;

        //const int numberOfSubElaborations = 100;
        int numberOfSubElaborations = TransferringRules.Count + SystemRules.Count;

        // Determine what states are possible.
        for (int elabCounter = 0; elabCounter < numberOfSubElaborations; elabCounter++)
        {
            foreach (StateConsistentRule scr in SystemRules)
            {
                await Task.Delay(1);
                if (CancelElaborate)
                {
                    goto finishElaborate;
                }
                List<Nession> updatedInitNessions = new();
                foreach (Nession initN in initSeedList)
                {
                    updatedInitNessions.AddRange(initN.TryApplySystemRule(scr));
                }
                initSeedList = updatedInitNessions;
            }

            foreach (StateTransferringRule transferRule in TransferringRules)
            {
                await Task.Delay(1);
                if (CancelElaborate)
                {
                    goto finishElaborate;
                }
                for (int i = initSeedStart; i < initSeedEnd; i++)
                {
                    Nession? n = initSeedList[i].TryApplyTransfer(transferRule);
                    if (n != null)
                    {
                        initSeedList.Add(n);
                    }
                }
            }
            // Update the ranges of states to be updated.
            initSeedStart = initSeedEnd;
            initSeedEnd = initSeedList.Count;

            RemoveRedundantNessions(initSeedList);
        }

    finishElaborate:
        InitialNessions = initSeedList;
    }

    public void CancelElaboration()
    {
        CancelElaborate = true;
    }

    private static void RemoveRedundantNessions(List<Nession> nList)
    {
        // Dispose of Nessions that are expanded on by other Nession. This will reduce processing
        // later in the elaboration. Note that this iteration strategy takes advantage of the
        // fact that later Nessions in the list are built upon previous Nessions.
        for (int i = 0; i < nList.Count - 1; i++)
        {
            Nession currentNession = nList[i];
            for (int j = i + 1; j < nList.Count; j++)
            {
                if (currentNession.IsPrefixOf(nList[j]))
                {
                    nList.RemoveAt(i);
                    i--;
                    break;
                }
            }
        }
    }

    public void GenerateHornClauseSet(State? s, List<(Nession, HashSet<HornClause>)> byNession)
    {
        if (InitialNessions == null)
        {
            throw new InvalidOperationException("Must run Elaborate(...) before running GenerateHornClauseSet(...)");
        }
        Debug.Assert(InitialNessions != null);

        HashSet<IMessage> premises = new(); // So that we only need to create one set.
        foreach (Nession n in InitialNessions)
        {
            HashSet<HornClause> thisNessionClauses = new();
            n.CollectHornClauses(thisNessionClauses, premises, s);
            byNession.Add((n, thisNessionClauses));
        }
    }

    #endregion
    #region State querying.

    public List<Nession> MessageFoundWhen(IMessage msg, State when)
    {
        Guard g = new();
        List<Nession> matches = new();
        foreach (Nession n in GeneratedNessions())
        {
            foreach (Nession.Frame frame in n.History)
            {
                foreach (State s in frame.StateSet)
                {
                    if (when.CanBeUnifiedTo(s, g, new()) && frame.ResultsContainMessage(msg))
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
    #region Debugging.

    public void DescribeAllNessions(TextWriter writer)
    {
        if (InitialNessions == null)
        {
            writer.WriteLine("=== No initial nessions set ===");
        }
        else
        {
            writer.WriteLine($"=== Initial ({InitialNessions.Count} nessions) ===");
            for (int i = 0; i < InitialNessions.Count; i++)
            {
                writer.WriteLine($"--- Nession ID {i} ---");
                writer.WriteLine(InitialNessions[i].ToString());
            }
        }
    }

    #endregion
}
