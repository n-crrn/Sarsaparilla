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
        //IEnumerable<Nession> nonceSeeds = from r in NewEventRules() select Nession.FromRule(r);
        //List<Nession> nonceSeeds = new();
        List<Nession> initSeedList = new() { initSeed };
        //List<List<Nession>> nonceSeedLists = new(from ns in nonceSeeds select new List<Nession>() { ns });

        int initSeedStart = 0;
        int initSeedEnd = initSeedList.Count;
        //List<(int Start, int End)> nonceStartEnd = new();
        /*for (int i = 0; i < nonceSeedLists.Count; i++)
        {
            nonceStartEnd.Add((0, 1));
        }*/

        //const int numberOfSubElaborations = 100;
        int numberOfSubElaborations = TransferringRules.Count + SystemRules.Count;

        // Determine what states are possible.
        for (int elabCounter = 0; elabCounter < numberOfSubElaborations; elabCounter++)
        {
            foreach (StateConsistentRule scr in SystemRules)
            {
                List<Nession> updatedInitNessions = new();
                foreach (Nession initN in initSeedList)
                {
                    updatedInitNessions.AddRange(initN.TryApplySystemRule(scr));
                }
                initSeedList = updatedInitNessions;
            }

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
                /*for (int i = 0; i < nonceStartEnd.Count; i++)
                {
                    for (int j = nonceStartEnd[i].Start; j < nonceStartEnd[i].End; j++)
                    {
                        Nession? n = nonceSeedLists[i][j].TryApplyTransfer(transferRule);
                        if (n != null)
                        {
                            nonceSeedLists[i].Add(n);
                        }
                    }
                }*/
            }
            // Update the ranges of states to be updated.
            initSeedStart = initSeedEnd;
            initSeedEnd = initSeedList.Count;
            /*for (int i = 0; i < nonceSeedLists.Count; i++)
            {
                nonceStartEnd[i] = (nonceStartEnd[i].End, nonceSeedLists[i].Count);
            }*/

            RemoveRedundantNessions(initSeedList);

        }

        //RemoveRedundantNessions(initSeedList);
        /*for (int i = 0; i < nonceSeedLists.Count; i++)
        {
            RemoveRedundantNessions(nonceSeedLists[i]);
        }*/

        /*List<Nession> nonceNessions = new();
        foreach (List<Nession> nList in nonceSeedLists)
        {
            nonceNessions.AddRange(nList);
        }*/

        // Determine what knowledge can be gained in all states.
        /*foreach (StateConsistentRule scr in SystemRules)
        {
            List<Nession> updatedInitNessions = new();
            //List<Nession> updatedNonceNessions = new();

            foreach (Nession initN in initSeedList)
            {
                updatedInitNessions.AddRange(initN.TryApplySystemRule(scr));
            }
            //foreach (Nession nonceN in nonceNessions)
            //{
                //updatedNonceNessions.AddRange(nonceN.TryApplySystemRule(scr));
            //}

            initSeedList = updatedInitNessions;
            //nonceNessions = updatedNonceNessions;
        }*/

        InitialNessions = initSeedList;
        //NonceNessions = nonceNessions;
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

    //public record HornClauseSet(HashSet<HornClause> GlobalClauses, List<(Nession, HashSet<HornClause>)> NessionClauses);

    public void GenerateHornClauseSet(State? s, List<(Nession, HashSet<HornClause>)> byNession)
    {
        if (InitialNessions == null)
        {
            Elaborate();
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
        foreach (Nession n in AllNessions())
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
     
        if (NonceNessions == null)
        {
            writer.WriteLine("=== No nonce nessions set ===");
        }
        else
        {
            writer.WriteLine($"=== Nonce-based ({NonceNessions.Count} nessions) ===");
            foreach (Nession nn in NonceNessions)
            {
                Debug.Assert(nn.InitialRule != null);
                writer.WriteLine($"--- Based on {nn.InitialRule.Label} ---");
                writer.WriteLine(nn.ToString());
            }
        }
    }

    #endregion
}
