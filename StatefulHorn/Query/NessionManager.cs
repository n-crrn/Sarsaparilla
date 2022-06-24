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

        // This ID tagging allows for very quick assessment of which rule is which by the Nession
        // methods. This is an important optimisation.
        for (int idTag = 0; idTag < SystemRules.Count; idTag++)
        {
            SystemRules[idTag].IdTag = idTag;
        }

        Knitter = KnitPattern.From(transferringRules, systemRules);
    }

    #region Properties.

    /// <summary>The initial set of state conditions.</summary>
    private readonly IReadOnlySet<State> InitialConditions;

    /// <summary>The State Consistent Rules that can be applied.</summary>
    private readonly IReadOnlyList<StateConsistentRule> SystemRules;

    /// <summary>
    /// An object for retrieving sets of State Transferring Rules that can be applied together.
    /// </summary>
    private readonly KnitPattern Knitter;

    /// <summary>
    /// The list of Nessions that were created at the last elaboration. This is null if 
    /// Elaboration(...) has not been called.
    /// </summary>
    public IReadOnlyList<Nession>? FoundNessions { get; private set; }

    #endregion
    #region Horn clause generation.

    private bool CancelElaborate = false;

    /// <summary>
    /// Generate nessions from scratch based on the conditions that the NessionManager was
    /// created with.
    /// </summary>
    /// <param name="finishedFunc">
    /// Function that is called with new nessions. The function can return true to indicate that
    /// further nessions do not need to be generated. This return value will be respected if
    /// checkFinishIteratively is also true.
    /// </param>
    /// <param name="numberOfSubElaborations">
    /// Number of times system (State Consistent Rules) are applied to the generated nessions.
    /// Between each application, State Transfer Rules are applied to extend the nessions.
    /// </param>
    /// <param name="checkFinishIteratively">
    /// If true, finishedFunc is called on every nession on every elaboration to see if the 
    /// desired end condition has been encountered. Otherwise, finishedFunc is called on 
    /// every nession on the conclusion of the elaboration.
    /// </param>
    /// <returns></returns>
    public async Task Elaborate(
        Func<List<Nession>, bool> finishedFunc, 
        int numberOfSubElaborations, 
        bool checkFinishIteratively = false)
    {
        // The following two lists are swapped throughout the run of the elaboration loop as the
        // nessions in one list are elaborated and stored in the other.
        List<Nession> nextLevel = new() { new(InitialConditions) };
        List<Nession> nextLevelIter = new();

        // Nessions that are not elaborated further are stored in this list. This list will
        // eventually be stored as FoundNessions.
        List<Nession> processed = new();

        // Elaboration loop.
        for (int elabCounter = 0; true; elabCounter++)
        {
            // Apply system rules (State Consistent Rules).
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

            // Extend nessions with State Transfer Rules.
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

            // If there are no new state, cease the elaboration. Otherwise, swap the lists in 
            // preparation for the next loop.
            if (nextLevelIter.Count == 0)
            {
                break;
            }
            processed.AddRange(nextLevel);
            (nextLevel, nextLevelIter) = (nextLevelIter, nextLevel);
            nextLevelIter.Clear();
        }

    finishElaborate:
        // Store the found result.
        processed.AddRange(nextLevel);
        FoundNessions = processed;

        // Run finishedFunc if it has not yet been run.
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

    /// <summary>Output a textual description of the nessions within the manager.</summary>
    /// <param name="writer">TextWriter to write description to.</param>
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
