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

    private Translation(HashSet<State> initStates, HashSet<Rule> allRules, IReadOnlySet<IMessage> queries, int depth)
    {
        InitialStates = initStates;
        Rules = allRules;
        Queries = queries;
        RecommendedDepth = depth;
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

    public IEnumerable<QueryEngine> QE5s()
    {
        foreach (IMessage queryMsg in Queries)
        {
            yield return new QueryEngine(InitialStates, queryMsg, null, Rules);
        }
    }

    public int RecommendedDepth { get; init; }

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

    private static IEnumerable<Rule> TranslateDestructor(Destructor dtr, ResolvedNetwork rn, RuleFactory factory)
    {
        // The premises to the destructor rule.
        IMessage lhs = rn.TermToLooseMessage(dtr.LeftHandSide);
        foreach (Term p in dtr.LeftHandSide.Parameters)
        {
            factory.RegisterPremise(StatefulHorn.Event.Know(rn.TermToLooseMessage(p)));
        }
        yield return factory.CreateStateConsistentRule(StatefulHorn.Event.Know(lhs));

        // The destructor to value rule.
        IMessage rhs = new VariableMessage(dtr.RightHandSide);
        factory.RegisterPremises(StatefulHorn.Event.Know(lhs));
        yield return factory.CreateStateConsistentRule(StatefulHorn.Event.Know(rhs));
    }

    public static Translation From(ResolvedNetwork rn, Network nw)
    {
        HashSet<Term> cellsInUse = new();

        RuleFactory factory = new();
        HashSet<Rule> allRules = new();

        // Transfer the rules for constructors.
        foreach ((string _, Constructor ctr) in nw.Constructors)
        {
            if (!ctr.DeclaredPrivate)
            {
                allRules.Add(TranslateConstructor(ctr, factory));
            }
        }

        // Transfer the rules for destructors.
        foreach (Destructor dtr in nw.Destructors)
        {
            allRules.UnionWith(TranslateDestructor(dtr, rn, factory));
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

        int recommendedDepth = 0;
        foreach (IMutateRule r in allMutateRules)
        {
            factory.SetNextLabel(r.Label);
            allRules.Add(r.GenerateRule(factory));
            recommendedDepth += r.RecommendedDepth;
        }

        return new(initStates, allRules, rn.Queries, recommendedDepth);
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
        int branchCounter = 0;
        BranchSummary rootSummary = PreProcessBranch(procTree.StartNode, new(), cellsInUse, ref branchCounter);
        List<IMutateRule> allMutateRules = new(ProcessBranch(
            new TranslateFrame(procTree.StartNode, rootSummary, new(), new(), new(), new(), new(), false, new(), null, 0, rn, nw))
        );
        return (rootSummary.AllSockets(), allMutateRules);
    }

    /// <summary>
    /// The output data structure of the first phase of the translation algorithm, which maps out
    /// which channels are in use and how they are used within each branch of the model.
    /// </summary>
    /// <param name="Readers">Read sockets used within this particular branch.</param>
    /// <param name="ReadersCumulative">
    /// Read sockets used within this branch and its child branches.
    /// </param>
    /// <param name="Writers">Write used used within this particular branch.</param>
    /// <param name="Children">Summaries of branches that follow this one.</param>
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
        HashSet<Term> finiteChannels,
        ref int socketSetCounter)
    {
        Dictionary<Term, ReadSocket> readers;
        Dictionary<Term, WriteSocket> writers;
        socketSetCounter++;
        (readers, writers) = GetSocketsRequiredForBranch(n, infiniteChannels, socketSetCounter);

        while (n.Branches.Count == 1 && n.Process is not ReplicateProcess && n.Process is not LetProcess)
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
            children.Add(PreProcessBranch(n.Branches[0], updatedInfChannels, new(), ref socketSetCounter));
        }
        else if (n.Branches.Count > 0)
        {
            foreach (ProcessTree.Node b in n.Branches)
            {
                children.Add(PreProcessBranch(b, infiniteChannels, new(finiteChannels), ref socketSetCounter));
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
        HashSet<Term> infiniteChannels,
        int socketSetId)
    {
        Dictionary<Term, ReadSocket> readers = new();
        Dictionary<Term, WriteSocket> writers = new();

        ProcessTree.Node? next = n;
        while (next != null && next.Process is not ReplicateProcess && next.Process is not LetProcess)
        {
            if (next.Process is InChannelProcess icp)
            {
                Term channelTerm = new(icp.Channel);
                if (!readers.TryGetValue(channelTerm, out ReadSocket? newReadSocket))
                {
                    newReadSocket = new(icp.Channel, infiniteChannels.Contains(channelTerm) ? Socket.Infinite : socketSetId);
                    readers[channelTerm] = newReadSocket;
                }
                newReadSocket.ReceivePatterns.Add(icp.ReceivePattern);
            }
            else if (next.Process is OutChannelProcess ocp)
            {
                Term channelTerm = new(ocp.Channel);
                if (!writers.ContainsKey(channelTerm))
                {
                    writers[channelTerm] = new(ocp.Channel, infiniteChannels.Contains(channelTerm) ? Socket.Infinite : socketSetId);
                }
            }

            next = next.Branches.Count == 1 ? next.Branches[0] : null;
        }

        return (readers, writers);
    }

    /// <summary>
    /// Every branch has a translation frame describing its context. Using this record makes it 
    /// easier to modify context parameters as the process tree is recursively parsed.
    /// </summary>
    /// <param name="Node">The specific process being translated.</param>
    /// <param name="Summary">The pre-processed summary of socket usage in the branch.</param>
    /// <param name="ParallelBranches">
    ///   A list of the pre-processed summaries of sockets used in branches that may be run
    ///   concurrently to this process.</param>
    /// <param name="PreviousSockets">
    ///   Sockets from the previous branch that need to be shut before the sockets in this
    ///   branch can be opened.
    /// </param>
    /// <param name="InteractionCount">
    ///   A dictionary noting the number of times reading or writing has occurred on the
    ///   currently open sockets. This is important for ensuring the correct sequencing of
    ///   rule applications.
    /// </param>
    /// <param name="Conditions">
    ///   The conditions imposed upon all translations of a branch by virtue of previous if
    ///   or let processes.
    /// </param>
    /// <param name="Premises">
    ///   The list of events that have been encountered so far during the translation.
    /// </param>
    /// <param name="Replicated">
    ///   Is this branch this finite, or has it gone through a replication event.
    /// </param>
    /// <param name="LeakedSockets">
    ///   Dictionary used to track the special socket identifiers that have been transmitted.
    ///   This object should be shared between all TranslateFrames of a model.
    /// </param>
    /// <param name="PreviousControlSplit">
    ///   The TranslateFrame that lead directly to this TranslateFrame. It is used when there
    ///   is a need to fall back to the ProVerif translation.
    /// </param>
    /// <param name="WhichChild">
    ///   From the perspective of the PreviousControlSplit, this is the index of this 
    ///   TranslateFrame amongst its Node.Branches. Required for the conduct of the
    ///   ProVerif-style translation.
    /// </param>
    /// <param name="Rn">
    ///   The fully resolved network being translated. This parameter allows terms to be 
    ///   interrogated. This object should be shared between all TranslateFrames of a model.
    /// </param>
    /// <param name="Nw">
    ///   The original parsed Network. This is provided to allow comparisons to be properly
    ///   translated. This object should be shared between all TranslateFrames of a model.
    /// </param>
    private record TranslateFrame
    (
        ProcessTree.Node Node,
        BranchSummary Summary,
        List<BranchSummary> ParallelBranches,
        List<Socket> PreviousSockets,
        Dictionary<Socket, int> InteractionCount,
        IfBranchConditions Conditions,
        HashSet<StatefulHorn.Event> Premises,
        bool Replicated,
        Dictionary<string, int> LeakedSockets,
        TranslateFrame? PreviousControlSplit,
        int WhichChild,
        ResolvedNetwork Rn,
        Network Nw
    );

    private static IEnumerable<IMutateRule> ProcessBranch(TranslateFrame tf)
    {
        HashSet<IMutateRule> rules = new();

        // Open required reader and infinite write sockets.
        List<Socket> toOpen = new(tf.Summary.Readers.Values);
        //toOpen.AddRange(from s in tf.Summary.Writers.Values where s.IsInfinite select s);
        toOpen.AddRange(tf.Summary.Writers.Values);
        if (toOpen.Count > 0)
        {
            rules.Add(new OpenSocketsRule(toOpen, tf.PreviousSockets)
            {
                Conditions = tf.Conditions
            });
        }

        // Ensure know rules for used writer sockets are set. Do not worry about conditions - the
        // knowledge of the socket contents, once the socket is known, are available.
        foreach (WriteSocket ws in tf.Summary.Writers.Values)
        {
            rules.Add(new KnowChannelContentRule(ws));
        }

        // Pre-filter infinite readers from the parallel branches. The infinite cross-link rule
        // requires that the premises and result are provided.
        Dictionary<Term, ReadSocket> infReaders;
        Dictionary<Term, List<ReadSocket>> finReaders;
        (infReaders, finReaders) = SplitInfiniteReaders(tf.ParallelBranches);

        // Set up the socket read/write counts. Necessary for proper sequence of rule generation.
        // Dictionary<Socket, int> interactionCount = new();
        if (tf.InteractionCount.Count == 0 && (tf.Summary.Readers.Count > 0 || tf.Summary.Writers.Count > 0))
        {
            foreach (ReadSocket rsS in tf.Summary.Readers.Values)
            {
                if (!rsS.IsInfinite)
                {
                    tf.InteractionCount[rsS] = 0;
                }
            }
            foreach (WriteSocket wrS in tf.Summary.Writers.Values)
            {
                if (!wrS.IsInfinite)
                {
                    tf.InteractionCount[wrS] = 0;
                }
            }
        }

        // Go through processes one at a time.
        int branchId = tf.Node.BranchId;
        ProcessTree.Node n = tf.Node;
        while (branchId == n.BranchId)
        {
            if (n.Process is InChannelProcess icp)
            {
                Term inChannelTerm = new(icp.Channel);
                ReadSocket reader = tf.Summary.Readers[inChannelTerm];
                if (reader.IsInfinite)
                {
                    rules.Add(new ReadResetRule(reader)
                    {
                        Conditions = tf.Conditions
                    });
                    foreach (IMutateRule imr in ReadRule.GenerateRulesForReceivePattern(reader, icp.ReceivePattern))
                    {
                        imr.Conditions = tf.Conditions;
                        rules.Add(imr);
                    }
                }
                else
                {
                    int rc = tf.InteractionCount[reader];
                    if (rc > 0)
                    {
                        rules.Add(new ReadResetRule(reader, rc)
                        {
                            Conditions = tf.Conditions
                        });
                    }
                    foreach (IMutateRule mr in ReadRule.GenerateRulesForReceivePattern(reader, icp.ReceivePattern))
                    {
                        mr.Conditions = tf.Conditions;
                        rules.Add(mr);
                    }
                    tf.InteractionCount[reader] = rc + 1;
                }
                rules.UnionWith(AttackChannelRule.GenerateRulesForReceivePattern(reader, icp.ReceivePattern, tf.Conditions));

                foreach ((string varEntry, _) in icp.ReceivePattern)
                {
                    tf.Premises.Add(ReadRule.VariableCellAsPremise(varEntry));
                }
            }
            else if (n.Process is OutChannelProcess ocp)
            {
                Term outChannelTerm = new(ocp.Channel);
                WriteSocket writer = tf.Summary.Writers[outChannelTerm];
                IMessage resultMessage = tf.Rn.TermToMessage(ocp.SentTerm);

                if (tf.Replicated && tf.Rn.CheckTermType(ocp.SentTerm, PiType.Channel))
                {
                    IMessage sendMessage = new NameMessage(GetNextSentSocketMarker(ocp.SentTerm.ToString(), tf.LeakedSockets));
                    HashSet<StatefulHorn.Event> sendPremises = new(tf.Premises) { StatefulHorn.Event.Know(sendMessage) };
                    string sendChannel = ocp.SentTerm.ToString();
                    if (n.Branches.Count > 0)
                    {
                        ProVerifTranslate(sendChannel, n.Branches[0], sendPremises, rules, tf.Rn, tf.Nw);
                    }
                    ParallelProcessesProVerifTranslate(tf, sendChannel, StatefulHorn.Event.Know(sendMessage), rules);
                    rules.Add(new BasicRule(new() { StatefulHorn.Event.Know(sendMessage) }, resultMessage, $"ChannelToken:{sendMessage}:{resultMessage}"));

                    resultMessage = sendMessage;
                }

                // Infinite cross-links have to be done here as this is where the premises and
                // result are.
                if (writer.IsInfinite)
                {
                    bool foundDestination = false;
                    if (infReaders.TryGetValue(outChannelTerm, out ReadSocket? rxSocket))
                    {
                        // For every matching receive pattern, add an infinite cross link.
                        List<IMutateRule> icls = new(InfiniteCrossLink.GenerateRulesForReceivePatterns(
                            writer,
                            rxSocket,
                            tf.InteractionCount,
                            tf.Premises,
                            resultMessage));
                        foreach (IMutateRule iclR in icls)
                        {
                            iclR.Conditions = tf.Conditions;
                            rules.Add(iclR);
                        }
                        rules.Add(new InfiniteWriteRule(writer, tf.InteractionCount, tf.Premises, resultMessage)
                        {
                            Conditions = tf.Conditions
                        });
                        
                        foundDestination = true;
                    }
                    if (finReaders.TryGetValue(outChannelTerm, out List<ReadSocket>? rxSocketList) && rxSocketList!.Count > 0)
                    {
                        rules.Add(new FiniteWriteRule(writer, tf.InteractionCount, tf.Premises, resultMessage)
                        {
                            Conditions = tf.Conditions
                        });
                        foundDestination = true;
                    }
                    if (!foundDestination)
                    {
                        throw new ArgumentException($"Write to channel {writer.ChannelName} is missing a corresponding concurrent read.");
                    }
                }
                else
                {
                    int wc = tf.InteractionCount[writer];
                    rules.Add(new FiniteWriteRule(writer, tf.InteractionCount, tf.Premises, resultMessage)
                    {
                        Conditions = tf.Conditions
                    });
                    tf.InteractionCount[writer] = wc + 1;
                }
            }

            if (n.Branches.Count == 0 || n.IsTerminating)
            {
                break;
            }
            n = n.Branches[0];
        }

        // Add in the required finite cross-links.
        foreach ((Term wt, WriteSocket ws) in tf.Summary.Writers)
        {
            if (finReaders.TryGetValue(wt, out List<ReadSocket>? rxSockets))
            {
                foreach (ReadSocket rx in rxSockets)
                {
                    rules.Add(new FiniteCrossLinkRule(ws, rx)
                    {
                        Conditions = tf.Conditions
                    });
                }
            }
            if (!ws.IsInfinite && infReaders.TryGetValue(wt, out ReadSocket? infRxSocket))
            {
                rules.Add(new FiniteCrossLinkRule(ws, infRxSocket!)
                {
                    Conditions = tf.Conditions
                });
            }
        }

        // Regardless of the ending process type, each ending process will still require an
        // individually generated list of what parallel branches are available, and which 
        // sockets need to be shut down.
        List<List<BranchSummary>> childParallelLists = new();
        List<Socket> thisBranchSockets = new();

        bool oneBranchOnly = n.Branches.Count == 1;
        if (oneBranchOnly)
        {
            childParallelLists.Add(tf.ParallelBranches);
        }
        else if (n.Branches.Count > 1)
        {
            for (int i = 0; i < n.Branches.Count; i++)
            {
                List<BranchSummary> subParallel = new(tf.ParallelBranches);
                for (int j = 0; j < n.Branches.Count; j++)
                {
                    if (i != j)
                    {
                        subParallel.Add(tf.Summary.Children[j]);
                    }
                }
                childParallelLists.Add(subParallel);
            }
        }

        // Collect and shut down the finite sockets if shutdown is required.
        if (!oneBranchOnly || n.Process is ReplicateProcess || n.Process is LetProcess)
        {
            thisBranchSockets.AddRange(from rs in tf.Summary.Readers.Values where !rs.IsInfinite select rs);
            thisBranchSockets.AddRange(from ws in tf.Summary.Writers.Values where !ws.IsInfinite select ws);
            if (tf.InteractionCount.Count > 0)
            {
                rules.Add(new ShutSocketsRule(tf.InteractionCount) { Conditions = tf.Conditions });
            }
        }
        // Sometimes, control passes through a branch that does not do anything. The ordering of
        // sockets still needs to be maintained.
        if (thisBranchSockets.Count == 0)
        {
            thisBranchSockets.AddRange(tf.PreviousSockets);
        }

        // The following constants are used for both IfProcesses and LetProcesses.
        const int GuardedBranchOffset = 0;
        const int ElseBranchOffset = 1;
        // Prepare the "parent" TranslateFrame, which is important for the full ProVerif style translations.
        tf = tf with { Node = n };
        if (n.Process is IfProcess ifp)
        {
            BranchRestrictionSet brs = BranchRestrictionSet.From(ifp.Comparison, tf.Rn, tf.Nw);

            // Handle if conditions by generating a complete set of rules for each condition
            // of the branch.
            foreach (IfBranchConditions ifCond in brs.IfConditions)
            {
                TranslateFrame ifFrame = tf with
                {
                    Node = n.Branches[GuardedBranchOffset],
                    Summary = oneBranchOnly ? tf.Summary : tf.Summary.Children[GuardedBranchOffset],
                    ParallelBranches = childParallelLists[GuardedBranchOffset],
                    PreviousSockets = thisBranchSockets,
                    InteractionCount = oneBranchOnly ? tf.InteractionCount : new(),
                    Conditions = tf.Conditions.And(ifCond),
                    Premises = new(tf.Premises),
                    PreviousControlSplit = tf,
                    WhichChild = GuardedBranchOffset
                };
                rules.UnionWith(ProcessBranch(ifFrame));
            }

            // Handle else conditions, if they exist. Again, complete set of rules for 
            // every condition.
            if (!oneBranchOnly)
            {
                foreach (IfBranchConditions elseCond in brs.ElseConditions)
                {
                    TranslateFrame elseFrame = tf with
                    {
                        Node = n.Branches[ElseBranchOffset],
                        Summary = tf.Summary.Children[ElseBranchOffset],
                        ParallelBranches = childParallelLists[ElseBranchOffset],
                        InteractionCount = new(),
                        PreviousSockets = thisBranchSockets,
                        Conditions = tf.Conditions.And(elseCond),
                        Premises = new(tf.Premises),
                        PreviousControlSplit = tf,
                        WhichChild = ElseBranchOffset
                    };
                    rules.UnionWith(ProcessBranch(elseFrame));
                }
            }
        }
        else if (n.Process is LetProcess lp)
        {
            // Generates rules that set a value for the conditions.
            LetValueSetFactory lvsFactory = new(
                lp, 
                tf.Rn, 
                tf.Nw, 
                thisBranchSockets, 
                tf.Summary.Children[GuardedBranchOffset].AllSockets(), 
                tf.Premises
            );
            rules.UnionWith(lvsFactory.GenerateSetRules()); 

            // Generate the guarded branch rules.
            TranslateFrame guardedLetFrame = tf with
            {
                Node = n.Branches[GuardedBranchOffset],
                Summary = tf.Summary.Children[GuardedBranchOffset],
                ParallelBranches = childParallelLists[GuardedBranchOffset],
                PreviousSockets = thisBranchSockets,
                InteractionCount = new(),
                Conditions = tf.Conditions,
                Premises = new(tf.Premises) { StatefulHorn.Event.Know(lvsFactory.StoragePremiseMessage) },
                PreviousControlSplit = tf,
                WhichChild = GuardedBranchOffset
            };
            rules.UnionWith(ProcessBranch(guardedLetFrame));

            if (!oneBranchOnly)
            {
                // Generate the else branch rules.
                TranslateFrame letElseFrame = tf with
                {
                    Node = n.Branches[ElseBranchOffset],
                    Summary = tf.Summary.Children[ElseBranchOffset],
                    ParallelBranches = childParallelLists[ElseBranchOffset],
                    PreviousSockets = thisBranchSockets,
                    InteractionCount = new(),
                    Conditions = tf.Conditions.Not(lvsFactory.Variable, lvsFactory.StoragePremiseMessage),
                    Premises = new(tf.Premises) { StatefulHorn.Event.Know(lvsFactory.EmptyStoragePremiseMessage) },
                    PreviousControlSplit = tf,
                    WhichChild = ElseBranchOffset
                };
                rules.UnionWith(ProcessBranch(letElseFrame));
            }
        }
        else if (n.Process is ReplicateProcess || n.Process is ParallelCompositionProcess)
        {
            // Translate the child process, and pass the found children up the "callstack".
            bool nextRepl = n.Process is ReplicateProcess;
            for (int i = 0; i < n.Branches.Count; i++)
            {
                TranslateFrame childFrame = tf with
                {
                    Node = n.Branches[i],
                    Summary = tf.Summary.Children[i],
                    ParallelBranches = childParallelLists[i],
                    PreviousSockets = thisBranchSockets,
                    InteractionCount = new(),
                    Premises = new(tf.Premises),
                    Replicated = tf.Replicated || nextRepl,
                    PreviousControlSplit = tf,
                    WhichChild = i
                };
                rules.UnionWith(ProcessBranch(childFrame));
            }
        }
        return rules;
    }

    private static (Dictionary<Term, ReadSocket>, Dictionary<Term, List<ReadSocket>>) SplitInfiniteReaders(
        List<BranchSummary> summaries)
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

    private static string GetNextSentSocketMarker(string channel, Dictionary<string, int> sentSockets)
    {
        sentSockets.TryGetValue(channel, out int sentCount);
        string marker = $"@{channel}@{sentCount}";
        sentSockets[channel] = 1;
        return marker;
    }

    private static void ParallelProcessesProVerifTranslate(
        TranslateFrame fromFrame, 
        string sentChannel, 
        StatefulHorn.Event sendPremise,
        HashSet<IMutateRule> allRules)
    {
        if (fromFrame.PreviousControlSplit != null)
        {
            TranslateFrame tf = fromFrame.PreviousControlSplit;
            HashSet<StatefulHorn.Event> premises = new(tf.Premises) { sendPremise };
            for (int i = 0; i < tf.Node.Branches.Count; i++)
            {
                if (i != tf.WhichChild)
                {
                    ProVerifTranslate(sentChannel, tf.Node.Branches[i], premises, allRules, tf.Rn, tf.Nw);
                }
            }
        }
    }

    private static void ProVerifTranslate(
        string sentChannel,
        ProcessTree.Node n,
        HashSet<StatefulHorn.Event> premises,
        HashSet<IMutateRule> allRules,
        ResolvedNetwork rn,
        Network nw)
    {
        switch (n.Process)
        {
            case InChannelProcess icp:
                foreach ((string varEntry, _) in icp.ReceivePattern)
                {
                    premises.Add(ReadRule.VariableCellAsPremise(varEntry));
                }
                if (n.Branches.Count > 0)
                {
                    ProVerifTranslate(sentChannel, n.Branches[0], premises, allRules, rn, nw);
                }
                break;
            case OutChannelProcess ocp:
                if (ocp.Channel == sentChannel)
                {
                    allRules.Add(new BasicRule(premises, rn.TermToMessage(ocp.SentTerm)));
                }
                if (n.Branches.Count > 0)
                {
                    ProVerifTranslate(sentChannel, n.Branches[0], premises, allRules, rn, nw);
                }
                break;
            case IfProcess ifp:
                BranchRestrictionSet brs = BranchRestrictionSet.From(ifp.Comparison, rn, nw);
                foreach (IfBranchConditions ic in brs.IfConditions)
                {
                    // FIXME: This is inefficient, cloning the rules would be quicker.
                    HashSet<IMutateRule> ifBranchRules = new();
                    ProVerifTranslate(sentChannel, n.Branches[0], premises, ifBranchRules, rn, nw);
                    AddConditions(allRules, ic, ifBranchRules);
                }
                if (n.Branches.Count > 1)
                {
                    foreach (IfBranchConditions elseCond in brs.ElseConditions)
                    {
                        // FIXME: This is inefficient, cloning the rules would be quicker.
                        HashSet<IMutateRule> elseRules = new();
                        ProVerifTranslate(sentChannel, n.Branches[1], premises, elseRules, rn, nw);
                        AddConditions(allRules, elseCond, elseRules);
                    }
                }
                break;
            case LetProcess lp:
                LetValueSetFactory lvsFactory = new(lp, rn, nw, Enumerable.Empty<Socket>(), Enumerable.Empty<Socket>(), premises);
                allRules.UnionWith(lvsFactory.GenerateSetRules());
                HashSet<StatefulHorn.Event> guardedPremises = new(premises)
                {
                    StatefulHorn.Event.Know(lvsFactory.StoragePremiseMessage)
                };
                HashSet<IMutateRule> letGuardRules = new();
                ProVerifTranslate(sentChannel, n.Branches[0], guardedPremises, letGuardRules, rn, nw);
                allRules.UnionWith(letGuardRules);

                if (n.Branches.Count > 1)
                {
                    IfBranchConditions elseConds = new(new(), new(lvsFactory.Variable, lvsFactory.StoragePremiseMessage));
                    HashSet<StatefulHorn.Event> elsePremises = new(premises)
                    {
                        StatefulHorn.Event.Know(lvsFactory.EmptyStoragePremiseMessage)
                    };
                    HashSet<IMutateRule> letElseRules = new();
                    ProVerifTranslate(sentChannel, n.Branches[1], elsePremises, letElseRules, rn, nw);
                    AddConditions(allRules, elseConds, letElseRules);
                }
                break;
            case ParallelCompositionProcess:
                foreach (ProcessTree.Node b in n.Branches)
                {
                    ProVerifTranslate(sentChannel, b, new(premises), allRules, rn, nw);
                }
                break;
            default: // ReplicateProcess and NewProcess.
                if (n.Branches.Count > 0)
                {
                    ProVerifTranslate(sentChannel, n.Branches[0], premises, allRules, rn, nw);
                }
                break;
        }
    }

    private static void AddConditions(HashSet<IMutateRule> newRules, IfBranchConditions cond, HashSet<IMutateRule> smallSet)
    {
        foreach (IMutateRule mr in smallSet)
        {
            mr.Conditions = mr.Conditions.And(cond);
            newRules.Add(mr);
        }
    }

}
