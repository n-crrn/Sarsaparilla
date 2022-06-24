using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace StatefulHorn.Query;

/// <summary>
/// Version of the Query Engine which is designed to work with Nession structures to 
/// test a protocol.
/// </summary>
public class NessionManager
{

    /// <summary>
    /// Create a new NessionManager that will generate nessions from the given initial states with
    /// the given initial set of rules.
    /// </summary>
    /// <param name="init">Initial states.</param>
    /// <param name="systemRules">State Consistent Rules of the system.</param>
    /// <param name="transferringRules">State Transferring Rules of the system.</param>
    public NessionManager(
        IEnumerable<State> init,
        List<StateConsistentRule> systemRules,
        List<StateTransferringRule> transferringRules)
    {
        InitialConditions = new HashSet<State>(init);
        SystemRules = systemRules;
        TransferringRules = transferringRules;

        for (int idTag = 0; idTag < SystemRules.Count; idTag++)
        {
            SystemRules[idTag].IdTag = idTag;
        }

        Knitter = KnitPattern.From(transferringRules, systemRules);
    }

    #region Properties.

    public IReadOnlySet<State> InitialConditions { get; private init; }

    public IReadOnlyList<StateConsistentRule> SystemRules { get; private init; }

    public IReadOnlyList<StateTransferringRule> TransferringRules { get; private init; }

    private readonly KnitPattern Knitter;

    public IReadOnlyList<Nession>? FoundNessions { get; private set; }

    #endregion
    #region Horn clause generation.

    private bool CancelElaborate = false;

    public async Task Elaborate(
        Func<List<Nession>, bool> finishedFunc, 
        int numberOfSubElaborations, 
        bool checkFinishIteratively = false)
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
            for (int i = 0; i < SystemRules.Count; i++)
            {
                StateConsistentRule scr = SystemRules[i];
                for (int j = 0; j < nextLevel.Count; j++)
                {
                    Nession initN = nextLevel[j];
                    nextLevelIter.AddRange(initN.TryApplySystemRule(scr));
                }

                // Swap lists to prevent unnecessary object creation.
                (nextLevel, nextLevelIter) = (nextLevelIter, nextLevel);
                nextLevelIter.Clear();
            }

            // Provide check for cancellation.
            await Task.Delay(15);
            if (CancelElaborate ||
                checkFinishIteratively && finishedFunc(nextLevel) ||
                elabCounter == numberOfSubElaborations - 1)
            {
                goto finishElaborate;
            }

            for (int i = 0; i < nextLevel.Count; i++)
            {
                Nession thisSeed = nextLevel[i];
                bool prefixAccounted = false;
                List<List<StateTransferringRule>> matchingTR = Knitter.GetTransferGroups(thisSeed);
                for (int j = 0; j < matchingTR.Count; j++)
                {
                    List<StateTransferringRule> transferRules = matchingTR[j];
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

    /// <summary>
    /// Tells the elaboration loop to cease next time it has completed allocating system rules.
    /// </summary>
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
