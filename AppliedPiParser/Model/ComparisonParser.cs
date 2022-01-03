using System;
using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Model;

/// <summary>
/// This is a convenience class for creating comparisons from tokens. This logic is a
/// demonstration of a (rare) weakness of modern iterative languages compared with
/// fully functional languages.
/// </summary>
internal static class ComparisonParser
{
    private struct Node
    {
        public string? Token;

        public IComparison? Comparison;

        public Node(string t)
        {
            Token = t;
            Comparison = null;
        }

        public Node(IComparison c)
        {
            Comparison = c;
            Token = null;
        }

        public bool IsOpenBracket => "(" == Token;

        public bool IsShutBracket => ")" == Token;

        public bool IsNameComparison => "=" == Token || "<>" == Token;

        public bool IsEquals => "=" == Token;

        public bool IsBooleanComparison => "&&" == Token || "||" == Token;

        public bool IsName => Comparison == null && !(IsOpenBracket || IsShutBracket || IsNameComparison || IsBooleanComparison);

        public bool IsOperand => Comparison != null || !(IsOpenBracket || IsShutBracket || IsNameComparison || IsBooleanComparison);

        public override string ToString() => Token ?? Comparison!.ToString();

        public static string FormatList(List<Node> nodes)
        {
            List<string> nodeStrings = new(from n in nodes select n.ToString());
            return String.Join(" ", nodeStrings);
        }
    }

    public static IComparison Parse(List<string> tokens)
    {
        List<Node> nodes = new(from t in tokens select new Node(t));
        return ParseNodes(nodes);
    }

    private static IComparison ParseNodes(List<Node> nodes)
    {
        // FIXME: Need a better means of expressing error conditions for a comparison, such
        // that the position of the error can be more accurately described.
        if (nodes[0].IsNameComparison || nodes[0].IsBooleanComparison)
        {
            throw new UnexpectedTokenException("( or <name>", nodes[0].ToString(), "Comparison");
        }
        if (nodes[^1].IsNameComparison || nodes[^1].IsBooleanComparison)
        {
            throw new UnexpectedTokenException(") or <name>", nodes[-1].ToString(), "Comparison");
        }

        // Collapse the brackets.
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsOpenBracket)
            {
                List<Node> subList = new();
                int j = i + 1;
                int nesting = 1;
                for (; j < nodes.Count; j++)
                {
                    Node thisNode = nodes[j];
                    if (thisNode.IsOpenBracket)
                    {
                        nesting++;
                    }
                    else if (thisNode.IsShutBracket)
                    {
                        nesting--;
                        if (nesting == 0)
                        {
                            break;
                        }
                    }
                    subList.Add(thisNode);
                }
                if (j == nodes.Count)
                {
                    // Overran the end of the tokens - bracket is not shut.
                    throw new UnexpectedTokenException(")", "then", "Comparison");
                }
                nodes.RemoveRange(i, subList.Count);
                IComparison subComp = ParseNodes(subList);
                nodes.Insert(i, new(subComp));
            }
        }

        // Collapse the name comparisons.
        for (int i = 1; i < nodes.Count - 1; i++)
        {
            Node n = nodes[i];
            if (n.IsNameComparison)
            {
                // Check that the input nodes are correctly typed.
                Node lhs = nodes[i - 1];
                Node rhs = nodes[i + 1];
                if (!lhs.IsName || !rhs.IsName)
                {
                    throw new InvalidComparisonException(n.ToString(), lhs.ToString(), rhs.ToString());
                }
                Node cmp = new(new NameComparison(n.IsEquals, lhs.ToString(), rhs.ToString()));

                i--; // Step backwards, we are about to remove these nodes and insert the comparison.
                nodes.RemoveRange(i, 3);
                nodes.Insert(i, cmp);
            }
        }

        // Collapse the boolean comparisons.
        while (nodes.Count > 1)
        {
            if (nodes.Count == 2)
            {
                throw new InvalidComparisonException("Down to two nodes while comparing: " + Node.FormatList(nodes));
            }
            Node lhs = nodes[0];
            Node op = nodes[1];
            Node rhs = nodes[2];
            if (lhs.Comparison == null || rhs.Comparison == null)
            {
                throw new InvalidComparisonException(op.ToString(), lhs.ToString(), rhs.ToString());
            }
            BooleanComparison.Type opType = BooleanComparison.TypeFromString(op.ToString());
            Node replacement = new(new BooleanComparison(opType, lhs.Comparison!, rhs.Comparison!));
            nodes[0] = replacement;
            nodes.RemoveRange(1, 2);
        }
        if (nodes[0].Comparison == null)
        {
            // It is just a token.
            throw new InvalidComparisonException($"Token {nodes[0].Token} is not a comparison.");
        }
        return nodes[0].Comparison!;
    }
}

class InvalidComparisonException : Exception
{
    public InvalidComparisonException(string opDesc, string lhsDesc, string rhsDesc) :
        base($"Attempted to apply {opDesc} to operands {lhsDesc} and {rhsDesc}.")
    { }

    public InvalidComparisonException(string msg) : base(msg) { }
}
