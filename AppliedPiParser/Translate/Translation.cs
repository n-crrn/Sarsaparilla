using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StatefulHorn;
using StatefulHorn.Messages;
using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPi.Translate;

public class Translation
{

    private Translation(HashSet<State> initStates, HashSet<Rule> allRules)
    {
        InitialStates = initStates;
        Rules = allRules;
    }

    public IReadOnlySet<State> InitialStates { get; init; }

    public IReadOnlySet<Rule> Rules { get; init; }

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
            IMessage varMsg = new VariableMessage($"_v{i}");
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
        Dictionary<Term, ChannelCell> allCells = new();
        Dictionary<Term, Term> subs = new();

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
            if (type == PiType.Channel)
            {
                ChannelCell cc;
                if (src == TermSource.Free)
                {
                    bool isPublic = !(nw.FreeDeclarations[term.Name].IsPrivate);
                    cc = new(term.Name, isPublic);
                    cellsInUse.Add(term);
                }
                else
                {
                    cc = new(term.Name, false);
                    // Not used until specified with the nu operator.
                }
                allCells[term] = cc;
            }
            else if (src == TermSource.Free)
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

        ProcessTree procTree = new(rn);
        BranchDependenceTree depTree = BranchDependenceTree.From(procTree);

        ProcessNode(procTree.StartNode, cellsInUse, allCells, subs, new());

        HashSet<State> initStates = new();
        foreach (ChannelCell c in allCells.Values)
        {
            c.CollectInitStates(initStates, depTree);
            allRules.UnionWith(c.GenerateRules(depTree, factory));
        }
        return new(initStates, allRules);
    }

    private static void ProcessNode(
        ProcessTree.Node n,
        HashSet<Term> cellsInUse,
        Dictionary<Term, ChannelCell> allCells,
        Dictionary<Term, Term> subs,
        HashSet<StatefulHorn.Event> premises)
    {
        switch (n.Process)
        {
            case NewProcess np:
                if (np.PiType == "channel")
                {
                    cellsInUse.Add(new Term(np.Variable));
                }
                if (n.HasNext)
                {
                    ProcessNode(n.Branches[0], cellsInUse, allCells, subs, premises);
                }
                break;
            case InChannelProcess icp:
                Term readChannel = new(icp.Channel);
                ChannelCell ic = allCells[readChannel];
                cellsInUse.Add(readChannel);
                ic.RegisterRead(n.BranchId);
                premises.UnionWith(from v in icp.VariablesDefined() select StatefulHorn.Event.Know(new NameMessage(v)));
                if (n.HasNext)
                {
                    ProcessNode(n.Branches[0], cellsInUse, allCells, subs, premises);
                }
                break;
            case OutChannelProcess ocp:
                Term writeChannel = new(ocp.Channel);
                ChannelCell wc = allCells[writeChannel];
                cellsInUse.Add(writeChannel);
                wc.RegisterWrite(n.BranchId, TermToMessage(ocp.SentTerm.Substitute(subs)), premises);
                if (n.HasNext)
                {
                    ProcessNode(n.Branches[0], cellsInUse, allCells, subs, premises);
                }
                break;
            case ParallelCompositionProcess pcp:
                foreach (ProcessTree.Node b in n.Branches)
                {
                    ProcessNode(b, new(cellsInUse), allCells, subs, new(premises));
                }
                break;
            case ReplicateProcess rp:
                foreach (Term t in cellsInUse)
                {
                    allCells[t].RegisterInfiniteAsOf(n.BranchId);
                }
                if (n.HasNext)
                {
                    ProcessNode(n.Branches[0], cellsInUse, allCells, subs, premises);
                }
                break;
            default:
                throw new NotImplementedException($"Process of type {n.Process.GetType()} cannot yet be translated.");
        }
    }

}
