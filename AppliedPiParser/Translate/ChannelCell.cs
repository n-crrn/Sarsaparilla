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

    public static readonly string WriteStateName = "_Write";

    private static readonly int NeverOpened = -1;

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

    private string InfInName() => $"{Name}@In";

    private string InfOutName() => $"{Name}@Out";

    private string InName(int bId) => $"{Name}@{bId}@In";

    private string OutName(int bId) => $"{Name}@{bId}@Out";

    private State InitialInStateForBranch(int bId) => new(InName(bId), InitialValue);

    private State WaitingInStateForBranch(int bId) => new(InName(bId), WaitingValue);

    private State ReadInStateForBranch(int bId, string valName = "_v")
    {
        return new(InName(bId), new FunctionMessage(ReadStateName, new() { new VariableMessage(valName) }));
    }

    private State WriteOutStateForBranch(int bId, string valName = "_v")
    {
        return new(OutName(bId), new FunctionMessage(WriteStateName, new() { new VariableMessage(valName) }));
    }

    private State WriteOutStateForBranch(int bId, IMessage content)
    {
        return new(OutName(bId), new FunctionMessage(WriteStateName, new() { content }));
    }

    private State ShutInStateForBranch(int bId) => new(InName(bId), ShutValue);

    private State InitialOutStateForBranch(int bId) => new(OutName(bId), InitialValue);

    private State WaitingOutStateForBranch(int bId) => new(OutName(bId), WaitingValue);

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

        // Create the read sockets.
        bool infReadFound = false;
        foreach (int rBId in ReadHistory.Keys)
        {
            if (allInfiniteBranchesQuery.Contains(rBId))
            {
                if (!infReadFound)
                {
                    startStates.Add(new State(InfInName(), InitialValue));
                    infReadFound = true;
                }
            }
            else
            {
                startStates.Add(InitialInStateForBranch(rBId));
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

    private Dictionary<int, string> AllInSocketNames(List<int> finiteBranches, List<int> infiniteBranches)
    {
        Dictionary<int, string> inNames = new();
        foreach (int b in finiteBranches)
        {
            inNames[b] = InName(b);
        }
        if (infiniteBranches.Count > 0)
        {
            inNames[-1] = InfInName();
        }
        return inNames;
    }

    private List<Snapshot> RegisterLinkedWriteSnapshots(
        int bId,
        List<Write> allHistory,
        int length, // That is, number of overall writes in the rule.
        bool includeFinalWait,
        RuleFactory factory)
    {
        List<Snapshot> writeSS = new() { factory.RegisterState(InitialOutStateForBranch(bId)) };
        for (int i = 0; i < length; i++)
        {
            Write w = allHistory[i];
            Snapshot ss = factory.RegisterState(WriteOutStateForBranch(bId, w.Written));
            factory.RegisterPremises(ss, w.Premises.ToArray());
            writeSS.Add(ss);
            if (includeFinalWait || i != length - 1)
            {
                writeSS.Add(factory.RegisterState(WaitingOutStateForBranch(bId)));
            }
        }
        LinkSnapshotList(writeSS);
        return writeSS;
    }

    private List<Snapshot> RegisterLinkedReadSnapshots(int bId, int readCount, RuleFactory factory)
    {
        List<Snapshot> readSS = new() { factory.RegisterState(InitialInStateForBranch(bId)) };
        for (int i = 0; i < readCount; i++)
        {
            readSS.Add(factory.RegisterState(WaitingInStateForBranch(bId)));
            readSS.Add(factory.RegisterState(ReadInStateForBranch(bId, $"_v{i}")));
        }
        LinkSnapshotList(readSS);
        return readSS;
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
        (List<int> finiteBranchReads, List<int> infiniteBranchReads) = SplitOnInfinity(ReadHistory.Keys, allInfinites);

        Dictionary<int, string> allInSockets = AllInSocketNames(finiteBranchReads, infiniteBranchReads);

        // --- Finite rules ---

        // Output finite write statements.
        foreach (int bId in finiteBranchWrites)
        {
            List<Write> writes = WriteHistory[bId];
            
            for (int i = 0; i < writes.Count; i++)
            {
                // Actual writing to the socket.
                List<Snapshot> writeSS = RegisterLinkedWriteSnapshots(bId, writes, i, false, factory);
                writeSS[^1].TransfersTo = WriteOutStateForBranch(bId, writes[i].Written);
                yield return factory.CreateStateTransferringRule();

                // The transfer from the output socket - reuse writeSS.
                foreach ((int otherBId, string otherSocketName) in allInSockets)
                {
                    if (otherBId != bId)
                    {
                        writeSS = RegisterLinkedWriteSnapshots(bId, writes, i + 1, false, factory);
                        Snapshot otherWaitSS = factory.RegisterState(new(otherSocketName, WaitingValue));
                        otherWaitSS.TransfersTo = new(otherSocketName, new FunctionMessage(ReadStateName, new() { writes[i].Written }));
                        if (i + 1 == writes.Count)
                        {
                            writeSS[^1].TransfersTo = ShutOutStateForBranch(bId);
                        }
                        else
                        {
                            writeSS[^1].TransfersTo = WaitingOutStateForBranch(bId);
                        }
                        yield return factory.CreateStateTransferringRule();
                    }
                }
            }

            // Set up the shutdown rule. (no longer needed)
            /*List<Snapshot> shutdownSS = RegisterLinkedWriteSnapshots(bId, writes, writes.Count, false, factory);
            shutdownSS[^1].TransfersTo = ShutOutStateForBranch(bId);
            yield return factory.CreateStateTransferringRule();*/

            // Set up the attacker read rule.
            Snapshot latestWriteSS = factory.RegisterState(WriteOutStateForBranch(bId, "_vLatest"));
            factory.RegisterPremises(latestWriteSS, Event.Know(new NameMessage(Name)));
            yield return factory.CreateStateConsistentRule(Event.Know(new VariableMessage("_vLatest")));
        }

        // Output finite read statements, including their shutdown statements.
        foreach (int bId in finiteBranchReads)
        {
            // Set up the initial read states.
            if (bId == BranchDependenceTree.InitialBranch)
            {
                Snapshot ss = factory.RegisterState(InitialInStateForBranch(bId));
                ss.TransfersTo = WaitingInStateForBranch(bId);
                yield return factory.CreateStateTransferringRule();
            }
            else
            {
                int priorReadUseBId = WhichBranchUsesSocketPrior(bId, finiteBranchReads, depTree);
                int priorWriteUseBId = WhichBranchUsesSocketPrior(bId, finiteBranchWrites, depTree);
                int priorUseBId = Math.Max(priorReadUseBId, priorWriteUseBId); // Note the latest read/write.
                bool readSocket = priorReadUseBId == priorUseBId;
                if (priorUseBId == -1)
                {
                    // Assume opened from start.
                    Snapshot ss = factory.RegisterState(InitialInStateForBranch(bId));
                    ss.TransfersTo = WaitingInStateForBranch(bId);
                    yield return factory.CreateStateTransferringRule();
                }
                else
                {
                    // Open after previous usage is finished.
                    State prevState = readSocket ? ShutInStateForBranch(priorUseBId) : ShutOutStateForBranch(priorUseBId);
                    Snapshot ss = factory.RegisterState(prevState);
                    ss.TransfersTo = WaitingInStateForBranch(bId);
                    yield return factory.CreateStateTransferringRule();
                }
            }

            // Set up the accept-read rules.
            int readPremises = ReadHistory[bId];
            for (int i = 0; i < readPremises - 1; i++)
            {
                // FIXME: Need to consider read patterns.
                List<Snapshot> readSS = RegisterLinkedReadSnapshots(bId, i, factory);
                readSS.Add(factory.RegisterState(WaitingInStateForBranch(bId)));
                readSS.Add(factory.RegisterState(ReadInStateForBranch(bId, $"_vLatest")));
                readSS[^1].TransfersTo = WaitingInStateForBranch(bId);
                yield return factory.CreateStateTransferringRule();
            }

            // Set up the shutdown rule.
            // FIXME: Need to consider read patterns.
            List<Snapshot> shutdownSS = RegisterLinkedReadSnapshots(bId, readPremises, factory);
            shutdownSS[^1].TransfersTo = ShutInStateForBranch(bId);
            yield return factory.CreateStateTransferringRule();

            // Set up the attacker insertion rule - is this actually needed?
            /*Snapshot latestReadSS = factory.RegisterState(WaitingInStateForBranch(bId));
            factory.RegisterPremises(latestReadSS, Event.Know(new NameMessage(Name)), Event.Know(new VariableMessage("_x")));
            latestReadSS.TransfersTo = ReadInStateForBranch(bId, "_x");
            yield return factory.CreateStateTransferringRule();*/
        }

        // --- Infinite Rules ---

        List<(int, State?)> infiniteWriteConditions = new();
        List<(int, State?)> infiniteReadConditions = new();

        // For infinite output sockets: if there are pre-conditions for the existence of an
        // infinite output socket, then set up rules to do finite transfers to finite
        // input sockets. Record the pre-condition for the final set of rules.
        foreach (int wBId in infiniteBranchWrites)
        {
            int priorBranch = WhichBranchUsesSocketPrior(wBId, finiteBranchReads, depTree);
            State? infState = priorBranch == NeverOpened ? null : ShutInStateForBranch(priorBranch);

            foreach (Write w in WriteHistory[wBId])
            {
                foreach ((int otherBId, string otherSocketName) in allInSockets)
                {
                    if (otherBId == NeverOpened)
                    {
                        // There is a special write rule for dealing with an infinite in socket.
                        continue;
                    }
                    if (infState != null)
                    {
                        factory.RegisterState(infState);
                    }
                    Snapshot readSS = factory.RegisterState(new(otherSocketName, WaitingValue));
                    // Premises are registered with the read, as the write may not exist.
                    factory.RegisterPremises(readSS, w.Premises);
                    readSS.TransfersTo = new(otherSocketName, new FunctionMessage(ReadStateName, new() { w.Written }));
                    yield return factory.CreateStateTransferringRule();
                }
            }

            infiniteWriteConditions.Add((wBId, infState));
        }

        // For infinite input sockets: just always reset the socket to Waiting if it is set
        // to read.
        if (infiniteBranchReads.Count > 0)
        {
            foreach (int rBId in infiniteBranchReads)
            {
                int priorBranch = WhichBranchUsesSocketPrior(rBId, finiteBranchReads, depTree);
                State? infState = null;
                if (priorBranch == NeverOpened)
                {
                    Snapshot initialInfSS = factory.RegisterState(new(InfInName(), InitialValue));
                    initialInfSS.TransfersTo = new(InfInName(), WaitingValue);
                    yield return factory.CreateStateTransferringRule();
                }
                else
                {
                    factory.RegisterState(ShutInStateForBranch(priorBranch));
                    Snapshot infSS = factory.RegisterState(new(InfInName(), InitialValue));
                    infSS.TransfersTo = new(InfInName(), WaitingValue);
                    yield return factory.CreateStateTransferringRule();
                    infState = ShutInStateForBranch(priorBranch);
                }
                infiniteReadConditions.Add((rBId, infState));
            }

            Snapshot readSS = factory.RegisterState(new(InfInName(), new FunctionMessage(ReadStateName, new() { new VariableMessage("_v") })));
            readSS.TransfersTo = new(InfInName(), WaitingValue);
            yield return factory.CreateStateTransferringRule();
        }


        // For infinite output to infinite input transfer: if pre-conditions for both sockets
        // exist, then the transferred data is simply known if the socket is known.
        foreach ((int wBId, State? infWriteCond) in infiniteWriteConditions)
        {
            // Need to track read start conditions.
            foreach ((int rBId, State? infReadCond) in infiniteReadConditions)
            {
                List<Write> branchWrites = WriteHistory[wBId];
                foreach (Write w in branchWrites)
                {
                    // After all the previous crazy logic, we can finally relax and use some
                    // good old-fashioned ProVerif-style Rules!
                    Event kEv = Event.Know(w.Written);
                    if (infWriteCond == null && infReadCond == null)
                    {
                        factory.RegisterPremises(w.Premises.ToArray());
                        yield return factory.CreateStateConsistentRule(kEv);
                    }
                    else if (infWriteCond == null && infReadCond != null)
                    {
                        Snapshot inSS = factory.RegisterState(infReadCond);
                        factory.RegisterPremises(inSS, w.Premises);
                        yield return factory.CreateStateConsistentRule(kEv);
                    }
                    else if (infReadCond == null && infWriteCond != null)
                    {
                        Snapshot outSS = factory.RegisterState(infWriteCond);
                        factory.RegisterPremises(outSS, w.Premises);
                        yield return factory.CreateStateConsistentRule(kEv);
                    }
                    else
                    {
                        Snapshot outSS = factory.RegisterState(infWriteCond!);
                        factory.RegisterPremises(outSS, w.Premises);
                        factory.RegisterState(infReadCond!);
                        yield return factory.CreateStateConsistentRule(kEv);
                    }
                }
            }
        }
    }

    #endregion
}


