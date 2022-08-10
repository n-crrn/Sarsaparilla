using System.Collections.Generic;

namespace StatefulHorn.Query;

public class QueryNodeMatrix
{

    public QueryNodeMatrix(State? when)
    {
        When = when;
    }

    private readonly State? When;

    private readonly Dictionary<IMessage, List<QueryNode>> FoundNodes = new();

    public int TermCount => FoundNodes.Count;

    /// <summary>
    /// Return a QueryNode with the given parameters. If such a node has been created before
    /// within this matrix, return that previously created node.
    /// </summary>
    /// <param name="result">Message that the node represents.</param>
    /// <param name="rank">The rank at which the node's message is to be known.</param>
    /// <param name="g">
    /// The guard conditions for the node message. The IAssignableMessage fields of the guard
    /// should only correspond with result itself or IAssignableMessage sub-messages in result.
    /// </param>
    /// <param name="requester">
    /// If another node is requesting this node, the requestor parameter is used to set the
    /// LeadingFrom property of the returned node.
    /// </param>
    /// <returns>A new or existing node matching the given parameters.</returns>
    public QueryNode RequestNode(IMessage result, int rank, Guard g, QueryNode? requester = null)
    {
        // Try to grab the existing, correct node.
        if (FoundNodes.TryGetValue(result, out List<QueryNode>? nodeLine))
        {
            for (int i = 0; i < nodeLine.Count; i++)
            {
                QueryNode qn = nodeLine[i];
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

    /// <summary>
    /// This method updates all nodes that are affected by a change in a node originating from
    /// this matrix.
    /// </summary>
    /// <param name="startingNode">Originally changed node.</param>
    /// <param name="newNodes">
    /// A list to be populated with additional nodes to be assessed. These new nodes are typically
    /// the result of resolving variables within a PremiseOptionSet. The purpose of this parameter
    /// is to minimise the creation of new List objects during a query.
    /// </param>
    /// <returns></returns>
    public List<QueryNode> EnsureNodesUpdated(QueryNode startingNode, List<QueryNode> newNodes)
    {
        Queue<QueryNode> toCheck = new();
        toCheck.Enqueue(startingNode);

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
