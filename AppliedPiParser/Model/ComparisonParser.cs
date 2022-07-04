using System;
using System.Collections.Generic;
using System.Linq;

using AppliedPi.Model.Comparison;

namespace AppliedPi.Model;

/// <summary>
/// This is a convenience class for creating comparisons from tokens.
/// </summary>
public static class ComparisonParser
{

    #region Public interface.

    /// <summary>
    /// Convert a series of tokens into a comparison.
    /// </summary>
    /// <param name="tokens">
    /// A series of strings as returned from consecutive calls to Parser.ReadNextToken(...).
    /// </param>
    /// <returns>
    /// An in-memory representation of the comparison for further translation and processing.
    /// </returns>
    /// <exception cref="InvalidComparisonException">
    /// The types of the names involved in this comparison do not make sense, or the comparison
    /// is otherwise malformed.
    /// </exception>
    /// <exception cref="UnexpectedTokenException">
    /// Invalid use of names or tokens with the comparison expression.
    /// </exception>
    public static IComparison Parse(IEnumerable<string> tokens)
    {
        List<string> updatedTkns = new(tokens);

        // If there is a comma, then the text on either side belongs together.
        for (int i = 1; i < updatedTkns.Count - 1; i++)
        {
            if (updatedTkns[i] == ",")
            {
                string replacement = updatedTkns[i - 1] + "," + updatedTkns[i + 1];
                updatedTkns.RemoveRange(i - 1, 3);
                updatedTkns.Insert(i - 1, replacement);
                i--;
            }
        }

        // Now that we've assembled the parameter lists and tuple internals, let's reassemble
        // named terms. If we find brackets, we need to work out if they belong together with
        // other pieces.
        // FIXME: This is not quite working.
        for (int i = 0; i < updatedTkns.Count - 3; i++)
        {
            if (!CompOps.Contains(updatedTkns[i]) && updatedTkns[i + 1] == "(" 
                && !CompOps.Contains(updatedTkns[i + 2]) && updatedTkns[i + 3] == ")")
            {
                string termReplacement = updatedTkns[i] + "(" + updatedTkns[i + 2] + ")";
                updatedTkns.RemoveRange(i, 4);
                updatedTkns.Insert(i, termReplacement);
            }
        }

        // See if we have any tuples to reassemble.
        for (int i = 1; i < updatedTkns.Count - 1; i++)
        {
            if (updatedTkns[i - 1] == "(" 
                && !CompOps.Contains(updatedTkns[i]) &&
                updatedTkns[i + 1] == ")")
            {
                string tupleReplacement = "(" + updatedTkns[i] + ")";
                updatedTkns.RemoveRange(i - 1, 3);
                updatedTkns.Insert(i - 1, tupleReplacement);
            }
        }

        List<Node> nodes = new(from t in updatedTkns select new Node(t));
        return ParseNodes(nodes);
    }

    private static readonly List<string> CompOps = new() { "&&", "||", "=", "<>" };

    #endregion
    #region Parsing.

    /// <summary>
    /// An internal abstract data structure used for translating tokens into a comparison
    /// expression. The algorithm uses instances of Node to collapse groups of tokens into
    /// IComparison instances until there is only an IComparison instance remaining.
    /// </summary>
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

        public bool MaybeNameComparison => "=" == Token || "<>" == Token;

        public bool IsEquals => "=" == Token;

        public bool IsBooleanComparison => "&&" == Token || "||" == Token;

        public bool IsNot => "not" == Token;

        public bool IsName 
        {
            get
            {
                return Comparison == null 
                    && !(IsOpenBracket 
                         || IsShutBracket 
                         || MaybeNameComparison 
                         || IsBooleanComparison 
                         || IsNot);
            }
        }

        public bool IsOperand
        {
            get
            {
                return Comparison != null 
                    || !(IsOpenBracket 
                         || IsShutBracket 
                         || MaybeNameComparison 
                         || IsBooleanComparison 
                         || IsNot);
            }
        }

        public bool IsOperator
        {
            get
            {
                return Comparison != null 
                    && (IsOpenBracket 
                        || IsShutBracket 
                        || MaybeNameComparison 
                        || IsBooleanComparison 
                        || IsNot);
            }
        }

        public IComparison AsComparison => Comparison ?? new IsComparison(Token!);

        public override string ToString() => Token ?? Comparison!.ToString();

        public static string FormatList(List<Node> nodes)
        {
            List<string> nodeStrings = new(from n in nodes select n.ToString());
            return "'" + string.Join("', '", nodeStrings) + "'";
        }
    }

    private static IComparison ParseNodes(List<Node> nodes)
    {
        // FIXME: Need a better means of expressing error conditions for a comparison, such
        // that the position of the error can be more accurately described.
        if (nodes[0].MaybeNameComparison || nodes[0].IsBooleanComparison)
        {
            throw new UnexpectedTokenException("( or <name>", nodes[0].ToString(), "Comparison");
        }
        if (nodes[^1].MaybeNameComparison || nodes[^1].IsBooleanComparison)
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
                nodes.RemoveRange(i, subList.Count + 2); // +2 for the opening and shutting brackets.
                IComparison subComp = ParseNodes(subList);
                nodes.Insert(i, new(subComp));
            }
        }

        // Collapse not operations - note that any brackets will be subsumed.
        for (int i = 0; i < nodes.Count - 1; i++)
        {
            if (nodes[i].Token == "not")
            {
                Node notParam = nodes[i + 1];
                if (notParam.IsOperator)
                {
                    throw new UnexpectedTokenException(notParam.Token!, "then", "Comparison");
                }
                NotComparison notCmp = notParam.Comparison != null ? new(notParam.Comparison!) : new(notParam.Token!);
                nodes.RemoveRange(i, 2);
                nodes.Insert(i, new(notCmp));
            }
        }
        if (nodes[^1].Token == "not")
        {
            throw new UnexpectedTokenException("not", "then", "Comparison");
        }

        // Collapse the name comparisons.
        for (int i = 1; i < nodes.Count - 1; i++)
        {
            Node n = nodes[i];
            if (n.MaybeNameComparison)
            {
                // Check that the input nodes are correctly typed.
                Node lhs = nodes[i - 1];
                Node rhs = nodes[i + 1];
                if (!lhs.IsName || !rhs.IsName)
                {
                    throw new InvalidComparisonException(n.ToString(), lhs.ToString(), rhs.ToString());
                }
                Node cmp = new(new EqualityComparison(n.IsEquals, lhs.ToString(), rhs.ToString()));

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
            BooleanComparison.Type opType = BooleanComparison.TypeFromString(op.ToString());
            Node replacement = new(new BooleanComparison(opType, lhs.AsComparison, rhs.AsComparison));
            nodes[0] = replacement;
            nodes.RemoveRange(1, 2);
        }
        if (nodes[0].Comparison == null)
        {
            // It is just a token - which may be a boolean name.
            return new IsComparison(nodes[0].Token!);
        }
        return nodes[0].Comparison!;
    }

    #endregion

}

/// <summary>
/// An exception that is thrown when a user provided comparison is not sensible.
/// </summary>
class InvalidComparisonException : Exception
{

    /// <summary>
    /// Create an InvalidComparisonException with a standard message when an operation cannot
    /// be applied to the provided operands.
    /// </summary>
    /// <param name="opDesc">Operation (e.g. '&&', '||').</param>
    /// <param name="lhsDesc">Left hand operand.</param>
    /// <param name="rhsDesc">Right hand operand.</param>
    public InvalidComparisonException(string opDesc, string lhsDesc, string rhsDesc) :
        base($"Attempted to apply {opDesc} to operands {lhsDesc} and {rhsDesc}.")
    { }

    /// <summary>
    /// Create an InvalidComparisonException with the given message.
    /// </summary>
    /// <param name="msg">Exception message.</param>
    public InvalidComparisonException(string msg) : base(msg) { }
}
