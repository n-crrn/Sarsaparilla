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

    public async Task Elaborate(Func<List<Nession>, bool> finishedFunc, int maxDepth = -1)
    {
        if (CancelElaborate)
        {
            CancelElaborate = false;
        }

        int numberOfSubElaborations = maxDepth == -1 ? TransferringRules.Count + SystemRules.Count : maxDepth;
        //const int numberOfSubElaborations = 4;

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
                foreach (StateTransferringRule transferRule in TransferringRules)
                {
                    (Nession? n, bool doesExtend) = thisSeed.TryApplyTransfer(transferRule);
                    if (n != null)
                    {
                        nextLevelIter.Add(n);
                        prefixAccounted |= doesExtend;
                    }
                }
                if (prefixAccounted)
                {
                    nextLevel.RemoveAt(i);
                    i--;
                }
            }
            processed.AddRange(nextLevel);
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
