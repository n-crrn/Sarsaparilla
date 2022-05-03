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

    private Translation(HashSet<State> initStates, HashSet<Rule> allRules, HashSet<IMessage> queries)
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

    /// <summary>
    /// Provides a direct translation of a term to a non-variable message representation.
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    private static IMessage TermToMessage(Term t)
    {
        if (t.Parameters.Count > 0)
        {
            List<IMessage> msgParams = new(from p in t.Parameters select TermToMessage(p));
            return t.IsTuple ? new TupleMessage(msgParams) : new FunctionMessage(t.Name, msgParams);
        }
        return new NameMessage(t.Name);
    }

    private static StatefulHorn.Event TermToKnow(Term t)
    {
        return StatefulHorn.Event.Know(TermToMessage(t));
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

    private static IMessage TermWithVarsToMessage(Term t, Network nw)
    {
        List<IMessage> parameters = new();
        if (t.Parameters.Count > 0)
        {
            parameters.AddRange(from p in t.Parameters select TermWithVarsToMessage(p, nw));
            if (t.IsTuple)
            {
                return new TupleMessage(parameters);
            }
            return new FunctionMessage(t.Name, parameters);
        }
        else
        {
            // Ensure that the term is not a declared free or a constant.
            if (nw.FreeDeclarations.ContainsKey(t.Name) || null != nw.GetConstant(t.Name))
            {
                return new NameMessage(t.Name);
            }
            return new VariableMessage(t.Name);
        }
    }

    private static Rule TranslateDestructor(Destructor dtr, Network nw, RuleFactory factory)
    {
        IMessage lhs = TermWithVarsToMessage(dtr.LeftHandSide, nw);
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
            allRules.Add(TranslateDestructor(dtr, nw, factory));
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
                    allRules.Add(factory.CreateStateConsistentRule(TermToKnow(term)));
                }
            }
            else if (src == TermSource.Constant)
            {
                allRules.Add(factory.CreateStateConsistentRule(TermToKnow(term)));
            }
        }

        (HashSet<Socket> allSockets, List<IMutateRule> allMutateRules) = GenerateMutateRules(rn);
        HashSet<State> initStates = new(from s in allSockets select s.InitialState());
        //allRules.UnionWith(from r in allMutateRules select r.GenerateRule(factory));

        foreach (IMutateRule r in allMutateRules)
        {
            factory.SetNextLabel(r.Label);
            allRules.Add(r.GenerateRule(factory));
        }

        HashSet<IMessage> queries = new(from q in nw.Queries select TermToMessage(q.LeakQuery));

        return new(initStates, allRules, queries);
    }

    public static (HashSet<Socket>, List<IMutateRule>) GenerateMutateRules(ResolvedNetwork rn)
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
        List<IMutateRule> allMutateRules = new(ProcessBranch(procTree.StartNode, rootSummary, new(), new(), new(), new()));
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
        Dictionary<Term, Term> subs,
        HashSet<StatefulHorn.Event> premises)
    {
        // Open required reader sockets.
        foreach (ReadSocket rs in summary.Readers.Values)
        {
            yield return new OpenReadSocketRule(rs, previousSockets);
        }

        // Know rules for used writer sockets.
        foreach (WriteSocket ws in summary.Writers.Values)
        {
            yield return new KnowChannelContentRule(ws);
        }

        // Pre-filter infinite readers from the parallel branches. The infinite cross-link rule
        // requires that the premises and result are provided.
        Dictionary<Term, ReadSocket> infReaders;
        Dictionary<Term, List<ReadSocket>> finReaders;
        (infReaders, finReaders) = SplitInfiniteReaders(parallelBranches);

        // Perform required branch operations.
        Dictionary<Term, int> readCount = new();
        Dictionary<Term, int> writeCount = new();
        int branchId = n.BranchId;
        while (branchId == n.BranchId)
        {
            switch (n.Process)
            {
                case InChannelProcess icp:
                    Term inChannelTerm = new(icp.Channel);
                    ReadSocket reader = summary.Readers[inChannelTerm];
                    readCount.TryGetValue(inChannelTerm, out int rc);
                    if (reader.IsInfinite)
                    {
                        yield return new ReadResetRule(reader);
                        foreach (IMutateRule imr in InfiniteReadRule.GenerateRulesForReceivePattern(reader, icp.ReceivePattern))
                        {
                            yield return imr;
                        }
                    }
                    else
                    {
                        if (rc > 0)
                        {
                            yield return new ReadResetRule(reader, rc);
                        }
                        foreach (IMutateRule mr in FiniteReadRule.GenerateRulesForReceivePattern(reader, rc, icp.ReceivePattern))
                        {
                            yield return mr;
                        }
                    }
                    
                    foreach ((string varEntry, _) in icp.ReceivePattern)
                    {
                        premises.Add(FiniteReadRule.VariableCellAsPremise(varEntry));
                    }
                    readCount[inChannelTerm] = rc + 1;
                    break;
                case OutChannelProcess ocp:
                    Term outChannelTerm = new(ocp.Channel);
                    WriteSocket writer = summary.Writers[outChannelTerm];
                    writeCount.TryGetValue(outChannelTerm, out int wc);
                    IMessage resultMessage = TermToMessage(ocp.SentTerm);
                    // Infinite cross-links have to be done here as this is where the premises and
                    // result are.
                    if (writer.IsInfinite)
                    {
                        if (infReaders.TryGetValue(outChannelTerm, out ReadSocket? rxSocket))
                        {
                            //yield return new InfiniteCrossLink(writer, rxSocket, premises, StatefulHorn.Event.Know(resultMessage));
                            // For every matching receive pattern, add an infinite cross link.
                            IEnumerable<IMutateRule> iclRules = InfiniteCrossLink.GenerateRulesForReadReceivePatterns(writer, rxSocket, premises, resultMessage);
                            foreach (IMutateRule rxPatternRule in iclRules)
                            {
                                yield return rxPatternRule;
                            }
                        }
                        if (finReaders.TryGetValue(outChannelTerm, out List<ReadSocket>? rxSocketList) && rxSocketList!.Count > 0)
                        {
                            yield return new InfiniteWriteRule(writer, premises, resultMessage);
                        }
                    }
                    else
                    {
                        yield return new FiniteWriteRule(writer, wc, premises, resultMessage);
                        writeCount[outChannelTerm] = wc + 1;
                    }
                    break;
            }
            if (n.Branches.Count == 0 || n.IsTerminating)
            {
                break;
            }
            n = n.Branches[0];
        }

        // Shut down the sockets.
        List<Socket> thisBranchSockets = new();
        foreach ((Term rt, ReadSocket rs) in summary.Readers)
        {
            if (!rs.IsInfinite)
            {
                thisBranchSockets.Add(rs);
                yield return new ShutRule(rs, readCount[rt]);
            }
        }
        foreach ((Term wt, WriteSocket ws) in summary.Writers)
        {
            if (!ws.IsInfinite)
            {
                thisBranchSockets.Add(ws);
                yield return new ShutRule(ws, writeCount[wt]);
            }
        }

        // Add in the required finite cross-links.
        foreach ((Term wt, WriteSocket ws) in summary.Writers)
        {
            if (finReaders.TryGetValue(wt, out List<ReadSocket>? rxSockets))
            {
                foreach (ReadSocket rx in rxSockets)
                {
                    yield return new FiniteCrossLinkRule(ws, rx);
                }
            }
        }

        // Process the children.
        for (int i = 0; i < n.Branches.Count; i++)
        {
            // Build parallel branches.
            List<BranchSummary> subParallel = new(parallelBranches);
            for (int j = 0; j < n.Branches.Count; j++)
            {
                if (i != j)
                {
                    subParallel.Add(summary.Children[j]);
                }
            }

            // Execute query.
            IEnumerable<IMutateRule> childRules = 
                ProcessBranch(n.Branches[i], summary.Children[i], subParallel, thisBranchSockets, new(subs), new(premises));
            foreach (IMutateRule r in childRules)
            {
                yield return r;
            }
        }
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
