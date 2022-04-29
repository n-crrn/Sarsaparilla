using System;
using System.Collections.Generic;
using System.Linq;
using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate;

public class ChannelCell
{
    public static readonly IMessage InitialValue = new NameMessage("_Initial");

    public static readonly IMessage WaitingValue = new NameMessage("_Waiting");

    public static readonly IMessage ShutValue = new NameMessage("_Shut");

    public static readonly string ReadStateName = "_Read";

    public static readonly IMessage ReadValue = new NameMessage("_Read");

    public static readonly string WriteStateName = "_Write";

    public ChannelCell(string name, bool publiclyKnown)
    {
        Name = name;
        IsPublic = publiclyKnown;
    }

    private readonly string Name;

    private readonly bool IsPublic;

    private record Write(IMessage Written, HashSet<Event> Premises);

    private readonly Dictionary<int, List<Write>> WriteHistory = new();

    public void RegisterWrite(int bId, IMessage msg, HashSet<Event> premises)
    {
        Write thisWrite = new(msg, premises);
        if (WriteHistory.TryGetValue(bId, out List<Write>? history))
        {
            history!.Add(thisWrite);
        }
        else
        {
            WriteHistory[bId] = new() { thisWrite };
        }
    }

    private readonly Dictionary<int, int> ReadHistory = new();

    public void RegisterRead(int bId)
    {
        ReadHistory.TryGetValue(bId, out int prevReads);
        ReadHistory[bId] = prevReads + 1;
    }

    private readonly List<int> InfiniteTransitions = new();

    public void RegisterInfiniteAsOf(int bId)
    {
        InfiniteTransitions.Add(bId);
    }

    #region Channel cell naming scheme.

    private string InfOutName() => $"{Name}@Out";

    private string OutName(int bId) => $"{Name}@{bId}@Out";

    private State WriteOutStateForBranch(int bId, string valName = "_v")
    {
        return new(OutName(bId), new FunctionMessage(WriteStateName, new() { new VariableMessage(valName) }));
    }

    private State WriteOutStateForBranch(int bId, IMessage content)
    {
        return new(OutName(bId), new FunctionMessage(WriteStateName, new() { content }));
    }

    private State InitialOutStateForBranch(int bId) => new(OutName(bId), InitialValue);

    private State ShutOutStateForBranch(int bId) => new(OutName(bId), ShutValue);

    #endregion
    #region Consolidated rule and state generation.

    private List<int> WhichBranchesInfinite(BranchDependenceTree depTree)
    {
        List<int> infBranches = new();
        foreach (int trans in InfiniteTransitions)
        {
            infBranches.AddRange(depTree.GetChildrenIncluding(trans));
        }
        return infBranches;
    }

    private static int WhichBranchUsesSocketPrior(int bId, List<int> allCandidates, BranchDependenceTree depTree)
    {
        if (bId == 1)
        {
            return -1;
        }
        int candidate = depTree.GetParentId(bId);
        while (!allCandidates.Contains(candidate))
        {
            if (candidate == BranchDependenceTree.InitialBranch)
            {
                return -1;
            }
            candidate = depTree.GetParentId(bId);
        }
        return candidate;
    }

    private record BranchStatePair(int BranchId, State State);

    private BranchStatePair? GetPriorChannelShutdown(int bId, List<int> writeCandidates, BranchDependenceTree depTree)
    {
        int priorWrite = WhichBranchUsesSocketPrior(bId, writeCandidates, depTree);
        if (priorWrite != -1)
        {
            return new(priorWrite, ShutOutStateForBranch(priorWrite));
        }
        else
        {
            return null;
        }
    }

    public void CollectInitStates(HashSet<State> startStates, BranchDependenceTree depTree)
    {
        // Get the up-to-date list of infinite branches.
        List<int> allInfiniteBranchesQuery = WhichBranchesInfinite(depTree);

        // Create the write sockets.
        bool infWriteFound = false;
        foreach (int wBId in WriteHistory.Keys)
        {
            if (allInfiniteBranchesQuery.Contains(wBId))
            {
                if (!infWriteFound)
                {
                    startStates.Add(new State(InfOutName(), InitialValue));
                    infWriteFound = true;
                }
            }
            else
            {
                startStates.Add(InitialOutStateForBranch(wBId));
            }
        }
    }

    private static (List<int>, List<int>) SplitOnInfinity(IEnumerable<int> branchIds, List<int> infBranches)
    {
        List<int> finite = new();
        List<int> infinite = new();
        foreach (int b in branchIds)
        {
            if (infBranches.Contains(b))
            {
                infinite.Add(b);
            }
            else
            {
                finite.Add(b);
            }
        }
        return (finite, infinite);
    }

    private static void LinkSnapshotList(List<Snapshot> ssList)
    {
        for (int i = 0; i < ssList.Count - 1; i++)
        {
            Snapshot current = ssList[i];
            Snapshot next = ssList[i + 1];
            next.SetModifiedOnceLaterThan(current);
        }
    }

    private List<Snapshot> RegisterLinkedWriteSnapshots(
        int bId,
        List<Write> allHistory,
        int length, // That is, number of overall writes in the rule.
        bool addPremises,
        RuleFactory factory)
    {
        List<Snapshot> writeSS = new() { factory.RegisterState(InitialOutStateForBranch(bId)) };
        for (int i = 0; i < length; i++)
        {
            Write w = allHistory[i];
            State stateToWrite = WriteOutStateForBranch(bId, new VariableMessage($"_v{i}"));
            Snapshot ss = factory.RegisterState(stateToWrite);
            if (addPremises)
            {
                factory.RegisterPremises(ss, w.Premises.ToArray());
            }
            writeSS.Add(ss);
        }
        LinkSnapshotList(writeSS);
        return writeSS;
    }

    public IEnumerable<Rule> GenerateRules(BranchDependenceTree depTree, RuleFactory factory)
    {
        factory.Reset();
        // Generate the public channel known rule (if channel is indeed public).
        Event knowChannelEv = Event.Know(new NameMessage(Name));
        if (IsPublic)
        {
            yield return factory.CreateStateConsistentRule(knowChannelEv);
        }

        // Collect information required for rules.
        List<int> allInfinites = WhichBranchesInfinite(depTree);
        (List<int> finiteBranchWrites, List<int> infiniteBranchWrites) = SplitOnInfinity(WriteHistory.Keys, allInfinites);

        // --- Finite rules ---

        // Output finite write statements.
        foreach (int bId in finiteBranchWrites)
        {
            List<Write> writes = WriteHistory[bId];
            
            for (int i = 0; i < writes.Count; i++)
            {
                // Actual writing to the socket.
                List<Snapshot> writeSS = RegisterLinkedWriteSnapshots(bId, writes, i, true, factory);
                writeSS[^1].TransfersTo = WriteOutStateForBranch(bId, writes[i].Written);
                yield return factory.CreateStateTransferringRule();
            }

            // Final transfer to shutdown.
            List<Snapshot> allWriteSS = RegisterLinkedWriteSnapshots(bId, writes, writes.Count, false, factory);
            allWriteSS[^1].TransfersTo = ShutOutStateForBranch(bId);
            yield return factory.CreateStateTransferringRule();

            // Set up the attacker read rule.
            Snapshot latestWriteSS = factory.RegisterState(WriteOutStateForBranch(bId, "_vLatest"));
            factory.RegisterPremises(latestWriteSS, Event.Know(new NameMessage(Name)));
            yield return factory.CreateStateConsistentRule(Event.Know(new VariableMessage("_vLatest")));
        }

        // --- Infinite Rules ---

        foreach (int wBId in infiniteBranchWrites)
        {
            BranchStatePair? priorPair = GetPriorChannelShutdown(wBId, finiteBranchWrites, depTree);

            foreach (Write w in WriteHistory[wBId])
            {
                Snapshot ss;
                if (priorPair == null)
                {
                    ss = factory.RegisterState(new(InfOutName(), InitialValue));
                }
                else
                {
                    ss = factory.RegisterState(priorPair.State);
                }
                factory.RegisterPremises(ss, w.Premises);
                //ss.TransfersTo = new(InfOutName(), new FunctionMessage(WriteStateName, new() { w.Written }));
                yield return factory.CreateStateConsistentRule(Event.Know(w.Written));
            }
        }
    }

    #endregion
}


