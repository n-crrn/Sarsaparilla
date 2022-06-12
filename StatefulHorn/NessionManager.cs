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

        Knitter = KnitPattern.From(TransferringRules, SystemRules);
    }

    #region Properties.

    public HashSet<State> InitialConditions { get; init; }

    public List<StateConsistentRule> SystemRules { get; init; }

    public List<StateTransferringRule> TransferringRules { get; init; }

    private readonly KnitPattern Knitter;

    public IReadOnlyList<Nession>? FoundNessions;

    #endregion
    #region Horn clause generation.

    private bool CancelElaborate = false;

    public async Task Elaborate(Func<List<Nession>, bool> finishedFunc, int numberOfSubElaborations, bool checkFinishIteratively = false)
    {
        if (CancelElaborate)
        {
            CancelElaborate = false;
        }

        Nession initSeed = new(InitialConditions);

        // Determine what states are possible.
        List<Nession> nextLevel = new() { initSeed };
        List<Nession> nextLevelIter = new();
        List<Nession> processed = new();
        for (int elabCounter = 0; true; elabCounter++)
        {
            foreach (StateConsistentRule scr in SystemRules)
            {
                foreach (Nession initN in nextLevel)
                {
                    nextLevelIter.AddRange(initN.TryApplySystemRule(scr));
                }

                // Swap lists to prevent unnecessary object creation.
                (nextLevel, nextLevelIter) = (nextLevelIter, nextLevel);
                nextLevelIter.Clear();
            }

            // Provide check for cancellation.
            await Task.Delay(15);
            if (CancelElaborate ||
                (checkFinishIteratively && finishedFunc(nextLevel)) || 
                elabCounter == (numberOfSubElaborations - 1))
            {
                goto finishElaborate;
            }

            for (int i = 0; i < nextLevel.Count; i++)
            {
                Nession thisSeed = nextLevel[i];
                bool prefixAccounted = false;
                List<List<StateTransferringRule>> matchingTR = Knitter.GetTransferGroups(thisSeed);
                foreach (List<StateTransferringRule> transferRules in matchingTR)
                {
                    (Nession? updated, bool canKeep) = thisSeed.TryApplyMultipleTransfers(transferRules);
                    prefixAccounted |= canKeep;
                    if (updated != null)
                    {
                        nextLevelIter.Add(updated);
                    }
                }

                if (prefixAccounted)
                {
                    nextLevel.RemoveAt(i);
                    i--;
                }
            }
            
            if (nextLevelIter.Count == 0)
            {
                // There were no new states found. In this case, we cease the elaboration here.
                break;
            }
            processed.AddRange(nextLevel);
            (nextLevel, nextLevelIter) = (nextLevelIter, nextLevel);
            nextLevelIter.Clear();
        }

    finishElaborate:
        processed.AddRange(nextLevel);
        FoundNessions = processed;

        if (!checkFinishIteratively)
        {
            processed.Reverse();
            finishedFunc(processed);
        }
    }

    public void CancelElaboration()
    {
        CancelElaborate = true;
    }

    #endregion
    #region Debugging.

    public void DescribeAllNessions(TextWriter writer)
    {
        if (FoundNessions == null)
        {
            writer.WriteLine("=== No nessions ===");
        }
        else
        {
            writer.WriteLine($"=== {FoundNessions.Count} ===");
            for (int i = 0; i < FoundNessions.Count; i++)
            {
                writer.WriteLine($"--- Nession ID {i} ---");
                writer.WriteLine(FoundNessions[i].ToString());
            }
        }
    }

    #endregion
}
