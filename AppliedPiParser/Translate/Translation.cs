using System;
using System.Collections.Generic;
using System.Linq;

using StatefulHorn;
using StatefulHorn.Messages;
using AppliedPi.Model;
using AppliedPi.Processes;
using AppliedPi.Translate.MutateRules;

namespace AppliedPi.Translate;

public class Translation
{

    private Translation(HashSet<State> initStates, HashSet<Rule> allRules, IReadOnlySet<IMessage> queries)
    {
        InitialStates = initStates;
        Rules = allRules;
        Queries = queries;
    }

    public IReadOnlySet<State> InitialStates { get; init; }

    public IReadOnlySet<Rule> Rules { get; init; }

    public IReadOnlySet<IMessage> Queries { get; init; }

    public IEnumerable<QueryEngine> QueryEngines()
    {
        foreach (IMessage queryMsg in Queries)
        {
            yield return new QueryEngine(InitialStates, queryMsg, null, Rules);
        }
    }

    private static Rule TranslateConstructor(Constructor ctr, RuleFactory factory)
    {
        HashSet<StatefulHorn.Event> premises = new();
        List<IMessage> funcParam = new();
        for (int i = 0; i < ctr.ParameterTypes.Count; i++)
        {
            IMessage varMsg = new VariableMessage($"@v{i}");
            premises.Add(StatefulHorn.Event.Know(varMsg));
            funcParam.Add(varMsg);
        }
        factory.RegisterPremises(premises.ToArray());
        StatefulHorn.Event result = StatefulHorn.Event.Know(new FunctionMessage(ctr.Name, funcParam));
        return factory.CreateStateConsistentRule(result);
    }

    private static Rule TranslateDestructor(Destructor dtr, ResolvedNetwork rn, RuleFactory factory)
    {
        IMessage lhs = rn.TermToLooseMessage(dtr.LeftHandSide);
        IMessage rhs = new VariableMessage(dtr.RightHandSide);
        factory.RegisterPremises(StatefulHorn.Event.Know(lhs));
        return factory.CreateStateConsistentRule(StatefulHorn.Event.Know(rhs));
    }

    public static Translation From(ResolvedNetwork rn, Network nw)
    {
        HashSet<Term> cellsInUse = new();

        RuleFactory factory = new();
        HashSet<Rule> allRules = new();

        // Transfer the rules for constructors.
        foreach ((string _, Constructor ctr) in nw.Constructors)
        {
            allRules.Add(TranslateConstructor(ctr, factory));
        }

        // Transfer the rules for destructors.
        foreach (Destructor dtr in nw.Destructors)
        {
            allRules.Add(TranslateDestructor(dtr, rn, factory));
        }

        // All channels are either free declarations or nonce declarations, so go through and
        // collect the free declarations. While doing this, start creating the rules for the
        // free declarations and the constants.
        foreach ((Term term, (TermSource src, PiType type)) in rn.TermDetails)
        {
            if (src == TermSource.Free)
            {
                if (!nw.FreeDeclarations[term.Name].IsPrivate)
                {
                    allRules.Add(factory.CreateStateConsistentRule(rn.TermToKnow(term)));
                }
            }
            else if (src == TermSource.Constant)
            {
                allRules.Add(factory.CreateStateConsistentRule(rn.TermToKnow(term)));
            }
        }

        (HashSet<Socket> allSockets, List<IMutateRule> allMutateRules) = GenerateMutateRules(rn, nw);
        HashSet<State> initStates = new(from s in allSockets select s.InitialState());

        foreach (IMutateRule r in allMutateRules)
        {
            factory.SetNextLabel(r.Label);
            allRules.Add(r.GenerateRule(factory));
        }

        return new(initStates, allRules, rn.Queries);
    }

    public static (HashSet<Socket>, List<IMutateRule>) GenerateMutateRules(ResolvedNetwork rn, Network nw)
    {
        HashSet<Term> cellsInUse = new();
        foreach ((Term term, (TermSource src, PiType type)) in rn.TermDetails)
        {
            if (type == PiType.Channel && src == TermSource.Free)
            {
                cellsInUse.Add(term);
            }
        }

        ProcessTree procTree = new(rn);
        BranchSummary rootSummary = PreProcessBranch(procTree.StartNode, new(), cellsInUse);
        List<IMutateRule> allMutateRules = new(ProcessBranch(procTree.StartNode, rootSummary, new(), new(), new(), new(), rn, nw));
        return (rootSummary.AllSockets(), allMutateRules);
    }

    private record BranchSummary(
        Dictionary<Term, ReadSocket> Readers,
        Dictionary<Term, List<ReadSocket>> ReadersCumulative, 
        Dictionary<Term, WriteSocket> Writers,
        List<BranchSummary> Children)
    {
        public HashSet<Socket> AllSockets()
        {
            HashSet<Socket> sockets = new();
            foreach ((Term _, List<ReadSocket> readers) in ReadersCumulative)
            {
                sockets.UnionWith(readers);
            }
            CollectWriters(sockets);
            return sockets;
        }

        private void CollectWriters(HashSet<Socket> sockets)
        {
            sockets.UnionWith(Writers.Values);
            foreach (BranchSummary bs in Children)
            {
                bs.CollectWriters(sockets);
            }
        }
    }

    private static BranchSummary PreProcessBranch(
        ProcessTree.Node n, 
        HashSet<Term> infiniteChannels,
        HashSet<Term> finiteChannels)
    {
        Dictionary<Term, ReadSocket> readers;
        Dictionary<Term, WriteSocket> writers;
        (readers, writers) = GetSocketsRequiredForBranch(n, infiniteChannels);

        // Skip to end of branch - see if there is a replication.
        int branchId = n.BranchId;
        while (branchId == n.BranchId && !n.IsTerminating && n.Branches.Count > 0)
        {
            if (n.Process is NewProcess np && np.PiType == "channel")
            {
                finiteChannels.Add(new(np.Variable));
            }
            n = n.Branches[0];
        }

        List<BranchSummary> children = new();
        if (n.Process is ReplicateProcess)
        {
            HashSet<Term> updatedInfChannels = new(infiniteChannels.Concat(finiteChannels));
            children.Add(PreProcessBranch(n.Branches[0], updatedInfChannels, new()));
        }
        else if (n.Branches.Count > 0)
        {
            foreach (ProcessTree.Node b in n.Branches)
            {
                children.Add(PreProcessBranch(b, infiniteChannels, new(finiteChannels)));
            }
        }

        Dictionary<Term, List<ReadSocket>> readCumulative = new();
        foreach (BranchSummary bs in children)
        {
            foreach ((Term cloneRt, List<ReadSocket> cloneList) in bs.ReadersCumulative)
            {
                if (readCumulative.TryGetValue(cloneRt, out List<ReadSocket>? allList))
                {
                    allList.AddRange(cloneList);
                }
                else
                {
                    readCumulative[cloneRt] = new(cloneList);
                }
            }
        }
        foreach ((Term txRt, ReadSocket txRs) in readers)
        {
            if (readCumulative.TryGetValue(txRt, out List<ReadSocket>? socketList))
            {
                socketList.Add(txRs);
            }
            else
            {
                readCumulative[txRt] = new() { txRs };
            }
        }
        return new(readers, readCumulative, writers, children);
    }

    private static (Dictionary<Term, ReadSocket>, Dictionary<Term, WriteSocket>) GetSocketsRequiredForBranch(
        ProcessTree.Node n,
        HashSet<Term> infiniteChannels)
    {
        Dictionary<Term, ReadSocket> readers = new();
        Dictionary<Term, WriteSocket> writers = new();

        int branchId = n.BranchId;
        while (branchId == n.BranchId)
        {
            if (n.Process is InChannelProcess icp)
            {
                Term channelTerm = new(icp.Channel);
                if (!readers.TryGetValue(channelTerm, out ReadSocket? newReadSocket))
                {
                    if (infiniteChannels.Contains(channelTerm))
                    {
                        newReadSocket = new(icp.Channel);
                    }
                    else
                    {
                        newReadSocket = new(icp.Channel, branchId);
                    }
                    readers[channelTerm] = newReadSocket;
                }
                newReadSocket.ReceivePatterns.Add(icp.ReceivePattern);
            }
            else if (n.Process is OutChannelProcess ocp)
            {
                Term channelTerm = new(ocp.Channel);
                if (!writers.ContainsKey(channelTerm))
                {
                    if (infiniteChannels.Contains(channelTerm))
                    {
                        writers[channelTerm] = new(ocp.Channel);
                    }
                    else
                    {
                        writers[channelTerm] = new(ocp.Channel, branchId);
                    }
                }
            }

            if (n.IsTerminating || n.Branches.Count == 0)
            {
                break;
            }
            n = n.Branches[0];
        }

        return (readers, writers);
    }

    private static IEnumerable<IMutateRule> ProcessBranch(
        ProcessTree.Node n, 
        BranchSummary summary, 
        List<BranchSummary> parallelBranches,
        List<Socket> previousSockets, 
        IfBranchConditions conditions,
        HashSet<StatefulHorn.Event> premises,
        ResolvedNetwork rn,
        Network nw)
    {
        HashSet<IMutateRule> rules = new();

        // Open required reader sockets.
        foreach (ReadSocket rs in summary.Readers.Values)
        {
            rules.Add(new OpenReadSocketRule(rs, previousSockets)
            {
                Conditions = conditions
            });
        }

        // Know rules for used writer sockets.
        foreach (WriteSocket ws in summary.Writers.Values)
        {
            rules.Add(new KnowChannelContentRule(ws)
            {
                Conditions = conditions
            });
        }

        // Pre-filter infinite readers from the parallel branches. The infinite cross-link rule
        // requires that the premises and result are provided.
        Dictionary<Term, ReadSocket> infReaders;
        Dictionary<Term, List<ReadSocket>> finReaders;
        (infReaders, finReaders) = SplitInfiniteReaders(parallelBranches);

        // Set up the socket read/write counts. Necessary for proper sequence of rule generation.
        Dictionary<Socket, int> interactionCount = new();
        foreach (ReadSocket rsS in summary.Readers.Values)
        {
            if (!rsS.IsInfinite)
            {
                interactionCount[rsS] = 0;
            }
        }
        foreach (WriteSocket wrS in summary.Writers.Values)
        {
            if (!wrS.IsInfinite)
            {
                interactionCount[wrS] = 0;
            }
        }

        // Go through processes one at a time.
        int branchId = n.BranchId;
        while (branchId == n.BranchId)
        {
            switch (n.Process)
            {
                case InChannelProcess icp:
                    Term inChannelTerm = new(icp.Channel);
                    ReadSocket reader = summary.Readers[inChannelTerm];
                    if (reader.IsInfinite)
                    {
                        rules.Add(new ReadResetRule(reader)
                        {
                            Conditions = conditions
                        });
                        foreach (IMutateRule imr in InfiniteReadRule.GenerateRulesForReceivePattern(reader, icp.ReceivePattern))
                        {
                            imr.Conditions = conditions;
                            rules.Add(imr);
                        }
                    }
                    else
                    {
                        int rc = interactionCount[reader];
                        if (rc > 0)
                        {
                            rules.Add(new ReadResetRule(reader, rc)
                            { 
                                Conditions = conditions
                            });
                        }
                        foreach (IMutateRule mr in FiniteReadRule.GenerateRulesForReceivePattern(reader, rc, icp.ReceivePattern))
                        {
                            mr.Conditions = conditions;
                            rules.Add(mr);
                        }
                        interactionCount[reader] = rc + 1;
                    }
                    rules.UnionWith(AttackChannelRule.GenerateRulesForReceivePattern(icp.ReceivePattern, conditions));
                    
                    foreach ((string varEntry, _) in icp.ReceivePattern)
                    {
                        premises.Add(FiniteReadRule.VariableCellAsPremise(varEntry));
                    }
                    break;
                case OutChannelProcess ocp:
                    Term outChannelTerm = new(ocp.Channel);
                    WriteSocket writer = summary.Writers[outChannelTerm];
                    IMessage resultMessage = rn.TermToMessage(ocp.SentTerm);
                    // Infinite cross-links have to be done here as this is where the premises and
                    // result are.
                    if (writer.IsInfinite)
                    {
                        if (infReaders.TryGetValue(outChannelTerm, out ReadSocket? rxSocket))
                        {
                            // For every matching receive pattern, add an infinite cross link.
                            IEnumerable<IMutateRule> iclRules = InfiniteCrossLink.GenerateRulesForReadReceivePatterns(writer, rxSocket, premises, resultMessage);
                            foreach (IMutateRule rxPatternRule in iclRules)
                            {
                                rxPatternRule.Conditions = conditions;
                                rules.Add(rxPatternRule);
                            }
                        }
                        if (finReaders.TryGetValue(outChannelTerm, out List<ReadSocket>? rxSocketList) && rxSocketList!.Count > 0)
                        {
                            rules.Add(new InfiniteWriteRule(writer, premises, resultMessage)
                            {
                                Conditions = conditions
                            });
                        }
                    }
                    else
                    {
                        int wc = interactionCount[writer];
                        rules.Add(new FiniteWriteRule(writer, interactionCount, premises, resultMessage)
                        {
                            Conditions = conditions
                        });
                        interactionCount[writer] = wc + 1;
                    }
                    break;
            }
            if (n.Branches.Count == 0 || n.IsTerminating)
            {
                break;
            }
            n = n.Branches[0];
        }

        // Collect and shut down the finite sockets.
        List<Socket> thisBranchSockets = new(from rs in summary.Readers.Values where !rs.IsInfinite select rs);
        thisBranchSockets.AddRange(from ws in summary.Writers.Values where !ws.IsInfinite select ws);
        if (interactionCount.Count > 0)
        {
            rules.Add(new ShutSocketsRule(interactionCount) { Conditions = conditions });
        }

        // Add in the required finite cross-links.
        foreach ((Term wt, WriteSocket ws) in summary.Writers)
        {
            if (finReaders.TryGetValue(wt, out List<ReadSocket>? rxSockets))
            {
                foreach (ReadSocket rx in rxSockets)
                {
                    rules.Add(new FiniteCrossLinkRule(ws, rx)
                    {
                        Conditions = conditions
                    });
                }
            }
        }

        // Regardless of the ending process type, each ending process will still require an
        // individually generated list of what parallel branches are available.
        List<List<BranchSummary>> childParallelLists = new();
        for (int i = 0; i < n.Branches.Count; i++)
        {
            List<BranchSummary> subParallel = new(parallelBranches);
            for (int j = 0; j < n.Branches.Count; j++)
            {
                if (i != j)
                {
                    subParallel.Add(summary.Children[j]);
                }
            }
            childParallelLists.Add(subParallel);
        }

        // The following constants are used for both IfProcesses and LetProcesses.
        const int GuardedBranchOffset = 0;
        const int ElseBranchOffset = 1;
        if (n.Process is IfProcess ifp)
        {
            BranchRestrictionSet brs = BranchRestrictionSet.From(ifp.Comparison, rn, nw);

            // Handle if conditions by generating a complete set of rules for each condition
            // of the branch.
            foreach (IfBranchConditions ifCond in brs.IfConditions)
            {
                IfBranchConditions updatedIfConditions = conditions.And(ifCond);
                IEnumerable<IMutateRule> ifChildRules = ProcessBranch(
                    n.Branches[GuardedBranchOffset],
                    summary.Children[GuardedBranchOffset],
                    childParallelLists[GuardedBranchOffset],
                    thisBranchSockets,
                    updatedIfConditions,
                    new(premises),
                    rn,
                    nw);
                rules.UnionWith(ifChildRules);
            }

            // Handle else conditions, if they exist. Again, complete set of rules for 
            // every condition.
            if (n.Branches.Count > 1)
            {
                foreach (IfBranchConditions elseCond in brs.ElseConditions)
                {
                    IfBranchConditions updatedElseConditions = conditions.And(elseCond);
                    IEnumerable<IMutateRule> elseChildRules = ProcessBranch(
                        n.Branches[ElseBranchOffset],
                        summary.Children[ElseBranchOffset],
                        childParallelLists[ElseBranchOffset],
                        thisBranchSockets,
                        updatedElseConditions,
                        new(premises),
                        rn,
                        nw);
                    rules.UnionWith(elseChildRules);
                }
            }
        }
        else if (n.Process is LetProcess lp)
        {
            LetValueSetFactory lvsFactory = new(lp, rn, nw, thisBranchSockets, premises);

            // Generate the rules that actually set a value given a set of conditions.
            rules.UnionWith(lvsFactory.GenerateSetRules());

            premises.Add(StatefulHorn.Event.Know(lvsFactory.EmptyStoragePremiseMessage));

            // Alter the premise for the guarded branch.
            IEnumerable<IMutateRule> guardedRules = ProcessBranch(
                n.Branches[GuardedBranchOffset],
                summary.Children[GuardedBranchOffset],
                childParallelLists[GuardedBranchOffset],
                thisBranchSockets,
                conditions,
                new(premises),
                rn,
                nw);
            rules.UnionWith(guardedRules);

            if (n.Branches.Count > 1)
            {
                // Alter the guard for the else branch.
                IfBranchConditions updatedConds = conditions.Not(lvsFactory.Variable, lvsFactory.StoragePremiseMessage);
                IEnumerable<IMutateRule> elseRules = ProcessBranch(
                    n.Branches[GuardedBranchOffset],
                    summary.Children[GuardedBranchOffset],
                    childParallelLists[GuardedBranchOffset],
                    thisBranchSockets,
                    updatedConds,
                    new(premises),
                    rn,
                    nw);
                rules.UnionWith(elseRules);
            }
        }
        else if (n.Process is ReplicateProcess || n.Process is ParallelCompositionProcess)
        {
            // Translate the child process, and pass the found children up the "callstack".
            for (int i = 0; i < n.Branches.Count; i++)
            {
                IEnumerable<IMutateRule> childRules = ProcessBranch(
                    n.Branches[i], 
                    summary.Children[i],
                    childParallelLists[i], 
                    thisBranchSockets, 
                    conditions, 
                    new(premises), 
                    rn, 
                    nw);
                rules.UnionWith(childRules);
            }
        }
        return rules;
    }

    private static (Dictionary<Term, ReadSocket>, Dictionary<Term, List<ReadSocket>>) SplitInfiniteReaders(List<BranchSummary> summaries)
    {
        Dictionary<Term, ReadSocket> infReaders = new();
        Dictionary<Term, List<ReadSocket>> finReaders = new();
        foreach (BranchSummary s in summaries)
        {
            foreach ((Term t, List<ReadSocket> readers) in s.ReadersCumulative)
            {
                foreach (ReadSocket r in readers)
                {
                    if (r.IsInfinite)
                    {
                        infReaders[t] = r;
                    }
                    else
                    {
                        if (finReaders.TryGetValue(t, out List<ReadSocket>? otherReaders))
                        {
                            otherReaders!.Add(r);
                        }
                        else
                        {
                            finReaders[t] = new() { r };
                        }
                    }
                }
            }
        }
        return (infReaders, finReaders);
    }

}
