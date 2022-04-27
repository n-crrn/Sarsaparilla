using System.Collections.Generic;

namespace AppliedPi.Translate;

public class BranchDependenceTree
{

    public static readonly int InitialBranch = 0;

    private BranchDependenceTree()
    {
        Dependencies = new();
    }

    public static BranchDependenceTree From(ProcessTree pt)
    {
        BranchDependenceTree t = new();
        t.Dependencies.Capacity = pt.NextBranchId;
        for (int i = 0; i < pt.NextBranchId; i++)
        {
            t.Dependencies.Add(0);
        }
        t.BuildFromProcessNodes(pt.StartNode);
        return t;
    }

    private void BuildFromProcessNodes(ProcessTree.Node n)
    {
        int startBranch = n.BranchId;
        while (!n.IsTerminating && n.Branches.Count > 0)
        {
            n = n.Branches[0];
        }
        if (n.IsTerminating)
        {
            RegisterParentId(startBranch, n.BranchId);
            foreach (ProcessTree.Node b in n.Branches)
            {
                RegisterParentId(n.BranchId, b.BranchId);
                BuildFromProcessNodes(b);
            }
        }
    }

    private readonly List<int> Dependencies;

    public int GetParentId(int branch) => branch == InitialBranch ? -1 : Dependencies[InitialBranch];    

    public void RegisterParentId(int parent, int child)
    {
        Dependencies[child] = parent;
    }

    public IEnumerable<int> AllParentIds(int child)
    {
        while (child != InitialBranch)
        {
            child = Dependencies[child];
            yield return child;
        }
    }

    public List<int> GetChildrenIncluding(int parent)
    {
        List<int> branches = new() { parent };
        for (int i = parent; i < Dependencies.Count; i++)
        {
            if (branches.Contains(Dependencies[i]))
            {
                branches.Add(i);
            }
        }
        return branches;
    }

    public bool IsParentOf(int parent, int child)
    {
        if (parent == InitialBranch)
        {
            return false;
        }
        int possParent = Dependencies[child];
        while (possParent != parent && possParent != InitialBranch)
        {
            possParent = Dependencies[possParent];
        }
        return possParent == parent;
    }

}
