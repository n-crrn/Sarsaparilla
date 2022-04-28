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

    public static Translation From(ResolvedNetwork rn, Network nw)
    {
        HashSet<Term> cellsInUse = new();
        Dictionary<Term, ChannelCell> allCells = new();

        // All channels are either free declarations or nonce declarations.
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
            // FIXME: Ensure free and constant definitions are transferred across to the ruleset.
        }

        ProcessTree procTree = new(rn);
        BranchDependenceTree depTree = BranchDependenceTree.From(procTree);

        ProcessNode(procTree.StartNode, cellsInUse, allCells, new());

        RuleFactory factory = new();
        HashSet<State> initStates = new();
        HashSet<Rule> allRules = new();
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
                    ProcessNode(n.Branches[0], cellsInUse, allCells, premises);
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
                    ProcessNode(n.Branches[0], cellsInUse, allCells, premises);
                }
                break;
            case OutChannelProcess ocp:
                Term writeChannel = new(ocp.Channel);
                ChannelCell wc = allCells[writeChannel];
                cellsInUse.Add(writeChannel);
                // FIXME: The following line is hardcore wrong.
                wc.RegisterWrite(n.BranchId, new NameMessage(ocp.SentTerm.ToString()), premises);
                if (n.HasNext)
                {
                    ProcessNode(n.Branches[0], cellsInUse, allCells, premises);
                }
                break;
            case ParallelCompositionProcess pcp:
                foreach (ProcessTree.Node b in n.Branches)
                {
                    ProcessNode(b, new(cellsInUse), allCells, new(premises));
                }
                break;
            case ReplicateProcess rp:
                foreach (Term t in cellsInUse)
                {
                    allCells[t].RegisterInfiniteAsOf(n.BranchId);
                }
                if (n.HasNext)
                {
                    ProcessNode(n.Branches[0], cellsInUse, allCells, premises);
                }
                break;
            default:
                throw new NotImplementedException($"Process of type {n.Process.GetType()} cannot yet be translated.");
        }
    }

}
