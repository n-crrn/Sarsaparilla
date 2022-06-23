using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatefulHorn.Query;

public class QueryNodeMatrix
{

    public QueryNodeMatrix(HashSet<IMessage> stateVariables, State? when)
    {
        StateVariables = stateVariables;
        When = when;
    }

    private readonly HashSet<IMessage> StateVariables;

    private readonly State? When;

    private readonly Dictionary<IMessage, List<QueryNode>> FoundNodes = new();

    public int TermCount => FoundNodes.Count;

    public QueryNode RequestNode(IMessage result, int rank, Guard g, QueryNode? requester = null)
    {
        // Try to grab the existing, correct node.
        if (FoundNodes.TryGetValue(result, out List<QueryNode>? nodeLine))
        {
            foreach (QueryNode qn in nodeLine)
            {
                if (qn.Rank == rank && qn.Guard.Equals(g))
                {
                    if (requester != null)
                    {
                        qn.LeadingFrom.Add(requester);
                    }
                    return qn;
                }
            }
        }
        else
        {
            nodeLine = new();
            FoundNodes[result] = nodeLine;
        }

        // None exists, create and submit one that is pre-assessment.
        QueryNode newQn = new(result, rank, g, When);
        nodeLine.Add(newQn);
        if (requester != null)
        {
            newQn.LeadingFrom.Add(requester);
        }
        return newQn;
    }

    public List<QueryNode> EnsureNodesUpdated(QueryNode startingNode)
    {
        Queue<QueryNode> toCheck = new();
        toCheck.Enqueue(startingNode);

        List<QueryNode> newNodes = new();
        while (toCheck.TryDequeue(out QueryNode? next))
        {
            if (next.RefreshState(this, newNodes))
            {
                foreach (QueryNode n in next.LeadingFrom)
                {
                    toCheck.Enqueue(n);
                }
            }
            next.ClearChanged();
        }
        return newNodes;
    }

}
