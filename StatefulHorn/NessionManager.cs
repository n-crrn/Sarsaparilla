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

    // FIXME: Include method to check that the query is sensible.

    #region Properties.

    public HashSet<State> InitialConditions { get; init; }

    public List<StateConsistentRule> SystemRules { get; init; }

    public List<StateTransferringRule> TransferringRules { get; init; }

    private readonly KnitPattern Knitter;

    #endregion
    #region Horn clause generation.

    private List<Nession>? InitialNessions;

    public IEnumerable<Nession> GeneratedNessions()
    {
        if (InitialNessions != null)
        {
            foreach (Nession n in InitialNessions)
            {
                yield return n;
            }
        }
    }

    private bool CancelElaborate = false;

    public async Task Elaborate(Func<List<Nession>, bool> finishedFunc, int maxDepth = -1)
    {
        if (CancelElaborate)
        {
            CancelElaborate = false;
        }

        int numberOfSubElaborations = maxDepth == -1 ? TransferringRules.Count + SystemRules.Count : maxDepth;

        Nession initSeed = new(InitialConditions);

        // Determine what states are possible.
        List<Nession> nextLevel = new() { initSeed };
        List<Nession> nextLevelIter = new();
        List<Nession> processed = new();
        for (int elabCounter = 0; elabCounter < numberOfSubElaborations; elabCounter++)
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
            if (CancelElaborate)
            {
                goto finishElaborate;
            }
            if (finishedFunc(nextLevel))
            {
                goto finishElaborate;
            }

            // Skip the last transfer elaboration - does not add value as system rules will not
            // be considered for the new states, while the number of nessions increases.
            if (elabCounter == (numberOfSubElaborations - 1))
            {
                break;
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
            processed.AddRange(nextLevel);

            if (nextLevelIter.Count == 0)
            {
                // There were no new states found. In this case, we cease the elaboration here.
                break;
            }
            (nextLevel, nextLevelIter) = (nextLevelIter, nextLevel);
            nextLevelIter.Clear();
        }

        processed.AddRange(nextLevel);

    finishElaborate:
        
        InitialNessions = processed;
    }

    public void CancelElaboration()
    {
        CancelElaborate = true;
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
