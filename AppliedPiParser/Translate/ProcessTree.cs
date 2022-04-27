using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AppliedPi.Model;
using AppliedPi.Processes;
using StatefulHorn;

namespace AppliedPi.Translate;

public class ProcessTree
{
    public ProcessTree(ResolvedNetwork rn)
    {
        IReadOnlyList<IProcess> seq = rn.ProcessSequence;
        if (seq.Count == 0)
        {
            throw new InvalidOperationException("Resolved network does not have any processes to translate.");
        }

        StartNode = new(seq[0]);
        Node current = StartNode;
        for (int i = 1; i < seq.Count; i++)
        {
            Node next = new(seq[i]);
            current.AddNext(next);
            current = next;
        }
        LabelBranches(StartNode, GetNextBranchId());
    }

    internal Node StartNode { get; init; }

    internal int NextBranchId = 0;

    private int GetNextBranchId()
    {
        int bId = NextBranchId;
        NextBranchId++;
        return bId;
    }

    private void LabelBranches(Node starting, int startId)
    {
        starting.BranchId = startId;

        // Propagate the Id sequentially.
        Node next = starting;
        while (!next.IsTerminating && next.Branches.Count > 0)
        {
            next = next.Branches[0];
            next.BranchId = startId;
        }

        // New labels for outward branches - this is the terminating process.
        if (next.Branches.Count > 0)
        {
            foreach (Node branch in next.Branches)
            {
                LabelBranches(branch, GetNextBranchId());
            }
        }
    }

    internal class Node
    {
        public Node(IProcess p)
        {
            Process = p;
            switch (p)
            {
                case ProcessGroup pg:
                    // Roll out the group...
                    Process = pg.Processes[0];
                    Node current = this;
                    for (int i = 1; i < pg.Processes.Count; i++)
                    {
                        IProcess innerP = pg.Processes[i];
                        Node next = new(innerP);
                        current.AddNext(next);
                        current = next;
                    }
                    break;
                case IfProcess ip:
                    Branches.Add(new(ip.GuardedProcess));
                    if (ip.ElseProcess != null)
                    {
                        Branches.Add(new(ip.ElseProcess!));
                    }
                    IsTerminating = true;
                    break;
                case LetProcess lp:
                    Branches.Add(new(lp.GuardedProcess));
                    if (lp.ElseProcess != null)
                    {
                        Branches.Add(new(lp.ElseProcess!));
                    }
                    IsTerminating = true;
                    break;
                case ParallelCompositionProcess pcp:
                    Branches.AddRange(from pcpP in pcp.Processes select new Node(pcpP));
                    IsTerminating = true;
                    break;
                case ReplicateProcess rp:
                    IsTerminating = true;
                    break;
            }
        }

        public IProcess Process { get; init; }

        #region Branch handling.

        public List<Node> Branches { get; init; } = new();

        public int BranchId { get; set; } = -1;

        public void AddNext(Node n)
        {
            if (IsTerminating)
            {
                throw new InvalidOperationException($"Cannot sequentially append process to terminating process '{Process}'.");
            }
            if (Branches.Count > 0)
            {
                throw new InvalidOperationException($"Sequentially following process already added to '{Process}'");
            }
            n.BranchId = BranchId;
            Branches.Add(n);
        }

        public bool IsTerminating { get; init; }

        public bool HasNext => Branches.Count > 0;

        #endregion
    }

}
