using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace StatefulHorn;

/// <summary>
/// Parses a series of rules to create a set of basis rules.
/// </summary>
/// <remarks>
/// The core method of this class is <c>Parse(string)</c>. All other methods in the class support
/// that method, so it is the best method to understand first.
/// </remarks>
public class RuleParser
{
    /// <summary>Creates new default instance of the RuleParser.</summary>
    public RuleParser()
    {
        Factory = new();
    }

    /// <summary>
    /// RuleFactory instance used by the parser to create new rule instances.
    /// </summary>
    private readonly RuleFactory Factory;

    // FIXME: Include bulk rule parse methods, providing the result as a List or as a
    // Dictionary<Label, Rule>.

    #region Convenience methods for single rule parsing.

    public StateConsistentRule ParseStateConsistentRule(string ruleSrc)
    {
        Rule r = Parse(ruleSrc);
        if (r is StateConsistentRule scr)
        {
            return scr;
        }
        throw new RuleParseException(ruleSrc, "Rule expected to be State Consistent Rule.");
    }

    public StateTransferringRule ParseStateTransferringRule(string ruleSrc)
    {
        Rule r = Parse(ruleSrc);
        if (r is StateTransferringRule str)
        {
            return str;
        }
        throw new RuleParseException(ruleSrc, "Rule expected to be State Transferring Rule.");
    }

    #endregion
    #region Single rule parsing - actual work.

    public static readonly string GuardUnunifiedOp = "~/>";   // Indicates a cannot-be-unified guard relationship.
    public static readonly string GuardUnunifiableOp = "=/="; // Indicates a cannot-be-unifiable guard relationship.
    public static readonly string LeftState = "-[";           // Separates the premises and snapshot declarations.
    public static readonly string RightState = "]->";         // Separates the snapshot declarations and the results.
    public static readonly string LaterThanOp = "=<";         // Indicates a "later than" snapshot relationship.
    public static readonly string LaterThanOpFancy = "≤";     // Indicates a machine-output "later than" snapshot relationship.
    public static readonly string ModifiedLaterThanOp = "<@";     // Indicates a "modified once" snapshot relationship.
    public static readonly string ModifiedLaterThanOpFancy = "⋖"; // Indicates a machine-output "later than" snapshot relationship.

    private static readonly string[] AllSnapshotRelationshipOps =
    {
        LaterThanOp,
        LaterThanOpFancy,
        ModifiedLaterThanOp,
        ModifiedLaterThanOpFancy
    };

    /// <summary>
    /// Intermediate format of an extracted item in a rule guard section. This type is only used
    /// within the RuleParser.
    /// </summary>
    /// <param name="Lhs">Text description of the left-hand-side message expression.</param>
    /// <param name="Rhs">Text description of the right-hand-side message expression.</param>
    /// <param name="Ununified">The raw text description of the operator.</param>
    /// <seealso cref="GuardUnunifiedOp"/>
    /// <seealso cref="GuardUnunifiableOp"/>
    /// <seealso cref="ReadGuard(string, string)"/>
    private record GuardPiece(string Lhs, string Rhs, bool Ununified);

    /// <summary>
    /// Intermediate format of an extracted item in a rule premise section. This type is only used
    /// within the RuleParser.
    /// </summary>
    /// <param name="Event">Text description of an event expression.</param>
    /// <param name="SnapshotLabel">
    /// The text referencing the appropriate snapshot's label. When there are no snapshots in the
    /// rule, this will be an empty string.
    /// </param>
    /// <seealso cref="ReadHM(string, string)"/>
    private record PremisePiece(string Event, string SnapshotLabel);

    /// <summary>
    /// Intermediate format of an extracted item in a state declaration section. This type is 
    /// only used within the RuleParser.
    /// </summary>
    /// <param name="State">
    /// Text description of a state declaration, which will include a state cell name followed
    /// by a message description in brackets.
    /// </param>
    /// <param name="SnapshotLabel">
    /// Textual label to be used by other sections of the rule to refer to this snapshot.
    /// </param>
    /// <seealso cref="ReadSO(string, string)"/>
    /// <seealso cref="ParseSnapshots(string, string)"/>
    private record SnapshotPiece(string State, string SnapshotLabel);

    /// <summary>
    /// Intermediate format of an extracted item in a snapshot relationship section. This type
    /// is only used within the RuleParser.
    /// </summary>
    /// <param name="Label1">Left-hand-side textual snapshot label.</param>
    /// <param name="Op">
    /// Operator demonstrating relationship between <c>Label1</c> and <c>Label2</c>.
    /// </param>
    /// <param name="Label2">Right-hand-side textual snapshot label.</param>
    /// <seealso cref="LaterThanOp"/>
    /// <seealso cref="ModifiedLaterThanOp"/>
    /// <seealso cref="UnchangedOp"/>
    /// <seealso cref="ReadSO(string, string)"/>
    /// <seealso cref="ParseSnapshotRelationships(string, string)"/>
    private record SnapshotRelationship(string Label1, string Op, string Label2);

    /// <summary>
    /// Intermediate format of the extract result section. Unlike the other intermediate types 
    /// within RuleParser, a <c>ResultParts</c> instance represents all parts for the rule.
    /// This type is only used within the RuleParser.
    /// </summary>
    /// <param name="StateConsistent">
    /// If true, then it indicates that the result has been detected to likely be an event 
    /// expression. Otherwise, the result is believed to be a series of state transitions.
    /// </param>
    /// <param name="Parts">
    /// The individual event expressions or state transitions found in the results section of
    /// the rule.
    /// </param>
    private record ResultParts(bool StateConsistent, List<string> Parts);

    /// <summary>
    /// Read a rule description and translate it to its in-memory representation. The description
    /// may be that of either a state consistent rule or a state transferring rule.
    /// </summary>
    /// <param name="ruleSrc">
    /// A string describing the rule in the form of "label = [ G ] H : M -[ S : 0 ]-&gt; e".
    /// </param>
    /// <returns></returns>
    /// <exception cref="RuleParseException">
    /// Thrown if the rule is not formatted correctly, such that the meaning of the rule cannot be
    /// deciphered. Examples of malformatting include excluding "-[" and "]-&gt;" from the rule
    /// source.
    /// </exception>
    /// <exception cref="RuleConstructionException">
    /// Thrown if there is a logic error within the rule. For instance, an Accept event is listed
    /// in the premises of a rule, or the result event of a state consistent rule is listed in its
    /// premises.
    /// </exception>
    public Rule Parse(string ruleSrc)
    {
        Factory.Reset();

        // --- Skip any whitespace at the start of the rule. Ensure rule is not empty. ---
        int i = SkipWhiteSpace(ruleSrc, 0);
        if (i == ruleSrc.Length)
        {
            throw new RuleParseException(ruleSrc, "No actual rule specified.");
        }

        // --- Read label (if there is one). ---
        string? label;
        (label, i) = ReadLabel(ruleSrc, i);
        Factory.SetNextLabel(label);

        // --- Extract the guard section if it is included. ---
        i = SkipWhiteSpace(ruleSrc, i);
        (int, int)? guardPoses = GetGuardExtents(ruleSrc, i);
        string guardDesc;
        if (guardPoses != null)
        {
            guardDesc = ruleSrc[guardPoses.Value.Item1..guardPoses.Value.Item2];
            i = guardPoses.Value.Item2 + 1; // Jumps the position past the ']' at the end of the guard.
        }
        else
        {
            guardDesc = "";
        }

        // --- Extract the premises, state specifiers and results sections ---
        string stateMarkerErrMsg = "Every rule must have '-[' and ']->'.";
        int leftStatePos = ruleSrc.IndexOf(LeftState, i);
        if (leftStatePos < 0)
        {
            throw new RuleParseException(ruleSrc, stateMarkerErrMsg);
        }
        int rightStatePos = ruleSrc.IndexOf(RightState, leftStatePos); // Ensure "]->" is after "-[".
        if (rightStatePos < 0)
        {
            throw new RuleParseException(ruleSrc, stateMarkerErrMsg);
        }
        string hmDesc = ruleSrc[i..leftStatePos];
        string soDesc = ruleSrc[(leftStatePos + LeftState.Length)..(rightStatePos)];
        string resultDesc = ruleSrc[(rightStatePos + RightState.Length)..];

        // --- Process the extracted premises, state specifiers and results sections. ---
        List<GuardPiece> guard = ReadGuard(ruleSrc, guardDesc);
        List<PremisePiece> hm = ReadHM(ruleSrc, hmDesc);
        (List<SnapshotPiece>, List<SnapshotRelationship>) so = ReadSO(ruleSrc, soDesc);
        ResultParts result = ReadResultParts(ruleSrc, resultDesc);

        // --- Conduct final processes of extracted sub-strings to form new rule. ---
        return CombinePieces(ruleSrc, guard, hm, so.Item1, so.Item2, result);
    }

    #region Textual parsing to tokens.

    /// <summary>
    /// Reads the label of a rule. If a label is set, it will take the form of
    /// "label = ...&lt;rest of rule&gt;...". Note that '=' is part of other operators, so logic
    /// is required to ensure that any '=' in a rule is not part of one of those operators.
    /// </summary>
    /// <param name="input">Rule source.</param>
    /// <param name="firstCharPos">Offset of the first non-white-space character.</param>
    /// <returns>
    /// A tuple of two values: the label string and the offset to the start of the remainder
    /// of the rule. If there is no label, the label string will be set to <c>null</c> and 
    /// the offset will equal the <c>firstCharPos</c> parameter.
    /// </returns>
    /// <exception cref="RuleParseException">
    /// Thrown if the rule is composed of a label, and nothing else. For instance, if the rule
    /// was only "label=".
    /// </exception>
    private static (string?, int) ReadLabel(string input, int firstCharPos)
    {
        if (input[firstCharPos] == '[')
        {
            // Start of the guard.
            return (null, firstCharPos);
        }

        // If there is no "=", then there is no label.
        int equalsPos = input.IndexOf('=');
        if (-1 == equalsPos)
        {
            return (null, firstCharPos);
        }
        else
        {
            // Double check that it is not a different operator. In doing so, throw an exception if
            // the rule ends with '=', as that would suggest this input is all label and no rule.
            if (equalsPos == input.Length)
            {
                throw new RuleParseException(input, "Rule should not end with '=' operator.");
            }
            else
            {
                if (input[equalsPos + 1] == '<' || input[equalsPos + 1] == '/')
                {
                    return (null, firstCharPos);
                }
            }
        }

        // Otherwise we can use the "=".
        return (input[firstCharPos..equalsPos].Trim(), equalsPos + 1);
    }

    private static (int, int)? GetGuardExtents(string input, int firstCharPos)
    {
        if (input[firstCharPos] != '[')
        {
            return null;
        }
        int i;
        int indent = 1;
        for (i = firstCharPos + 1; i < input.Length && indent > 0; i++)
        {
            char c = input[i];
            if (c == '[')
            {
                indent++;
            }
            else if (c == ']')
            {
                indent--;
            }
        }
        if (i == input.Length)
        {
            throw new RuleParseException(input, "Guard statement not ended.");
        }
        return (firstCharPos + 1, i - 1);
    }

    private static List<GuardPiece> ReadGuard(string whole, string guardDesc)
    {
        if (guardDesc == string.Empty)
        {
            return new();
        }

        List<GuardPiece> pieces = new();
        string[] eqns = guardDesc.Split(',');
        foreach (string eqn in eqns)
        {
            string[] eqnParts = eqn.Split(GuardUnunifiedOp);
            bool ununified = eqnParts.Length == 2;
            if (!ununified)
            {
                eqnParts = eqn.Split(GuardUnunifiableOp);
            }
            if (eqnParts.Length != 2)
            {
                throw new RuleParseException(whole, "Malformed guard statement.");
            }
            else if (eqnParts.Length == 2)
            {
                pieces.Add(new GuardPiece(eqnParts[0].Trim(), eqnParts[1].Trim(), ununified));
            }
        }
        return pieces;
    }

    private static List<PremisePiece> ReadHM(string whole, string hmDesc)
    {
        string[] topParts = hmDesc.Split(':', 2);
        List<PremisePiece> pieces = ParseEvents(whole, topParts[0]);
        if (topParts.Length == 2)
        {
            Dictionary<string, string> correspondences = ParseSnapshotCorrespondence(whole, topParts[1]);
            for (int i = 0; i < pieces.Count; i++)
            {
                string ssIMRef = pieces[i].SnapshotLabel;
                if (!correspondences.TryGetValue(pieces[i].SnapshotLabel, out string? ssLabel))
                {
                    throw new RuleParseException(whole, $"Snapshot reference {ssIMRef} has no correspondence in rule.");
                }
                Debug.Assert(ssLabel != null);
                pieces[i] = pieces[i] with { SnapshotLabel = ssLabel };
            }
        }
        return pieces;
    }

    private static List<PremisePiece> ParseEvents(string whole, string eventsDesc)
    {
        List<PremisePiece> pieces = new();
        StringBuilder refBuffer = new();
        int i = 0;
        while (i < eventsDesc.Length)
        {
            string term;
            i = SkipWhiteSpace(eventsDesc, i);
            (term, i) = ReadTerm(whole, eventsDesc, i);
            if (term != string.Empty)
            {
                string ssRef = "";
                i = SkipWhiteSpace(eventsDesc, i);
                if (i < eventsDesc.Length && eventsDesc[i] == '(')
                {
                    i++;
                    while (i < eventsDesc.Length && eventsDesc[i] != ')')
                    {
                        refBuffer.Append(eventsDesc[i]);
                        i++;
                    }
                    ssRef = refBuffer.ToString();
                    refBuffer.Clear();
                    i++; // Move past the closing bracket.
                }
                i = SkipWhiteSpace(eventsDesc, i);
                if (i < eventsDesc.Length && eventsDesc[i] == ',')
                {
                    i++;
                }
                pieces.Add(new PremisePiece(term, ssRef.Trim()));
            }
        }
        return pieces;
    }

    private static int SkipWhiteSpace(string input, int start)
    {
        int i = start;
        while (i < input.Length && char.IsWhiteSpace(input[i]))
        {
            i++;
        }
        return i;
    }

    private static Dictionary<string, string> ParseSnapshotCorrespondence(string whole, string mDesc)
    {
        mDesc = mDesc.Trim();
        if (mDesc[0] != '{' || mDesc[^1] != '}')
        {
            throw new RuleParseException(whole, "Malformatted Premise-Snapshot correspondence section.");
        }
        mDesc = mDesc[1..^1];

        string[] correspondences = mDesc.Split(',');
        Dictionary<string, string> found = new();
        foreach (string c in correspondences)
        {
            string[] cParts = c.Split("::");
            if (cParts.Length != 2)
            {
                throw new RuleParseException(whole, "Unable to parse Premise-Snapshot correspondence section.");
            }
            found[cParts[0].Trim().TrimStart('(').TrimEnd(')').Trim()] = cParts[1].Trim();
        }
        return found;
    }

    private static (string, int) ReadTerm(string whole, string input, int start)
    {
        Stack<char> indentChars = new();
        int i = start;
        for (; i < input.Length; i++)
        {
            if (input[i] == ',' && indentChars.Count == 0)
            {
                return (input[start..i], i);
            }
            if (input[i] == '[' || input[i] == '(')
            {
                indentChars.Push(input[i]);
            }
            else if (input[i] == ']' || input[i] == ')')
            {
                if (indentChars.Count == 0)
                {
                    throw new RuleParseException(whole, $"'{input[i]}' never opened.");
                }
                char opening = indentChars.Pop();
                if (!((opening == '[' && input[i] == ']') || (opening == '(' && input[i] == ')')))
                {
                    throw new RuleParseException(whole, $"'{input[i]}' mismatch when reading term.");
                }
                if (indentChars.Count == 0)
                {
                    return (input[start..(i + 1)], i + 1);
                }
            }
        }
        if (start == i)
        {
            return ("", i);
        }
        return (input[start..], i);
    }

    private static (List<SnapshotPiece>, List<SnapshotRelationship>) ReadSO(string whole, string soDesc)
    {
        if (soDesc == string.Empty)
        {
            return (new(), new());
        }

        string[] topParts = soDesc.Split(":");
        List<SnapshotPiece> ssPieces = ParseSnapshots(whole, topParts[0]);
        List<SnapshotRelationship> ssRels;
        if (topParts.Length > 2)
        {
            throw new RuleParseException(whole, "Incorrectly formatted State Description section.");
        }
        else if (topParts.Length == 1)
        {
            if (ssPieces.Count > 1)
            {
                throw new RuleParseException(whole, "Multiple snapshots but no description of their relationship provided.");
            }
            ssRels = new();
        }
        else
        {
            ssRels = ParseSnapshotRelationships(whole, topParts[1]);
        }
        return (ssPieces, ssRels);
    }

    private static List<SnapshotPiece> ParseSnapshots(string whole, string sDesc)
    {
        List<string> pairParts = new();

        // Split out each (s, a) pair, and store in pairParts.
        int i = 0;
        StringBuilder buffer = new();
        Stack<char> indentChars = new();
        for (; i < sDesc.Length; i++)
        {
            char c = sDesc[i];
            if (c == '(' || c == '[')
            {
                if (indentChars.Count != 0)
                {
                    buffer.Append(c);
                }
                indentChars.Push(c);
            }
            else if (c == ')' || c == ']')
            {
                if (indentChars.Count == 0)
                {
                    throw new RuleParseException(whole, "Encountered close bracket/square bracket when reading snapshot.");
                }
                char starter = indentChars.Pop();
                if (!((starter == '(' && c == ')') || (starter == '[' && c == ']')))
                {
                    throw new RuleParseException(whole, "Mismatched bracket/square bracket when reading term.");
                }
                if (indentChars.Count != 0)
                {
                    buffer.Append(c);
                }
                else
                {
                    pairParts.Add(buffer.ToString().Trim());
                    buffer.Clear();
                    while ((i + 1) < sDesc.Length && (sDesc[i + 1] == ',' || char.IsWhiteSpace(sDesc[i + 1])))
                    {
                        i++; // Skip the comma.
                    }
                }
            }
            else
            {
                buffer.Append(c);
            }
        }

        // Split up the (s, a) pair.
        List<SnapshotPiece> pieces = new();
        foreach (string parts in pairParts)
        {
            int rComma = parts.LastIndexOf(',');
            if (rComma == -1)
            {
                if (pairParts.Count != 1)
                {
                    throw new RuleParseException(whole, "States need to be associated with snapshots.");
                }
                pieces.Add(new(parts, ""));
            }
            else
            {
                string ssDesc = parts[(rComma + 1)..].Trim();
                if (ssDesc.Contains(')'))
                {
                    if (pairParts.Count != 1)
                    {
                        throw new RuleParseException(whole, "States need to be associated with snapshots.");
                    }
                    pieces.Add(new(parts, ""));
                }
                else
                {
                    pieces.Add(new(parts[0..rComma], ssDesc));
                }
            }
        }

        return pieces;
    }

    private static List<SnapshotRelationship> ParseSnapshotRelationships(string whole, string oDesc)
    {
        oDesc = oDesc.Trim().TrimStart('{').TrimEnd('}');
        string[] relationships = oDesc.Split(",");
        //string[] opsToTry = new string[] { LaterThanOp, LaterThanOpFancy, ModifiedLaterThanOp, ModifiedLaterThanOpFancy, UnchangedOp, UnchangedOpFancy };
        List<SnapshotRelationship> found = new();
        foreach (string r in relationships)
        {
            bool suitableOpFound = false;
            foreach (string op in AllSnapshotRelationshipOps)
            {
                string[] parts = r.Split(op);
                if (parts.Length == 2)
                {
                    found.Add(new(parts[0].Trim(), op, parts[1].Trim()));
                    suitableOpFound = true;
                    break;
                }
                else if (parts.Length > 2)
                {
                    throw new RuleParseException(whole, $"Malformatted snapshot relationship: {r}");
                }
            }
            if (!suitableOpFound)
            {
                throw new RuleParseException(whole, $"Snapshot relationship with no operator: {r}");
            }
        }
        return found;
    }

    private static ResultParts ReadResultParts(string whole, string resultDesc)
    {
        if (resultDesc == string.Empty)
        {
            return new(false, new()); // Will be caught as an error as there is no result.
        }

        resultDesc = resultDesc.Trim();

        if (resultDesc[0] == '<')
        {
            List<string> parts = new();
            StringBuilder buffer = new();
            int indent = 0;
            for (int i = 0; i < resultDesc.Length; i++)
            {
                char c = resultDesc[i];
                if (c == '<')
                {
                    if (indent > 0)
                    {
                        buffer.Append(c);
                    }
                    indent++;
                }
                else if (c == '>')
                {
                    if (indent == 1)
                    {
                        parts.Add(buffer.ToString());
                        buffer.Clear();
                    }
                    else
                    {
                        buffer.Append(c);
                    }
                    indent--;
                }
                else if ((indent == 0) && !(char.IsWhiteSpace(c) || c == ','))
                {
                    throw new RuleParseException(whole, $"Malformed State Transformation section: {resultDesc}");
                }
                else if (indent > 0)
                {
                    buffer.Append(c);
                }
            }
            return new(false, parts);
        }
        return new(true, new() { resultDesc });
    }

    #endregion
    #region Tokens to in-memory representation.

    private static Guard CreateGuard(string whole, List<GuardPiece> pieces)
    {
        HashSet<(IMessage, IMessage)> ununified = new();
        HashSet<(IMessage, IMessage)> ununifiable = new();

        foreach (GuardPiece p in pieces)
        {
            (IMessage? msg1, string? errMsg1) = MessageParser.TryParseMessage(p.Lhs);
            if (errMsg1 != null)
            {
                throw new RuleParseException(whole, $"Cannot parse message '{p.Lhs}': {errMsg1}");
            }
            (IMessage? msg2, string? errMsg2) = MessageParser.TryParseMessage(p.Rhs);
            if (errMsg2 != null)
            {
                throw new RuleParseException(whole, $"Cannot parse message '{p.Rhs}': {errMsg2}");
            }
            if (p.Ununified)
            {
                ununified.Add((msg1!, msg2!));
            }
            else
            {
                ununifiable.Add((msg1!, msg2!));
            }
        }

        return Guard.CreateFromSets(ununified, ununifiable);
    }

    private Rule CombinePieces(
        string whole,
        List<GuardPiece> guardPieces,
        List<PremisePiece> premisePieces, 
        List<SnapshotPiece> ssPieces, 
        List<SnapshotRelationship> ssRelationships,
        ResultParts result)
    {
        Factory.GuardStatements = CreateGuard(whole, guardPieces);

        // Create all of the required snapshots. This needs to be done early so that Premises
        // and Results can be associated with them later.
        Dictionary<string, Snapshot> ssByLabel = new();
        foreach (SnapshotPiece sp in ssPieces)
        {
            // Check that there is no double declaration of states.
            if (ssByLabel.ContainsKey(sp.SnapshotLabel))
            {
                throw new RuleParseException(whole, $"Multiple declarations of snapshot label '{sp.SnapshotLabel}'.");
            }

            // Actually parse the state and associate it to its label if successful.
            (State? s, string? stateErr) = MessageParser.TryParseState(sp.State);
            if (stateErr != null)
            {
                throw new RuleParseException(whole, $"Unable to parse state '{sp.State}': {stateErr}");
            }
            ssByLabel[sp.SnapshotLabel] = Factory.RegisterState(s!);
        }

        Dictionary<string, List<string>> ssPreviousByLabel = new();
        // Establish relationships between snapshots.
        foreach (SnapshotRelationship sr in ssRelationships)
        {
            // Check that the snapshots referenced exist.
            if (!ssByLabel.TryGetValue(sr.Label1, out Snapshot? ss1))
            {
                string knownSS = string.Join(", ", from k in ssByLabel.Keys select $"'{k}'");
                string ssErrMsg = $"Attempted to reference snapshot '{sr.Label1}', which does not exist. Known keys are: {knownSS}.";
                throw new RuleParseException(whole, ssErrMsg);
            }
            if (!ssByLabel.TryGetValue(sr.Label2, out Snapshot? ss2))
            {
                throw new RuleParseException(whole, $"Attempted to reference snapshot '{sr.Label2}', which does not exist.");
            }
            if (!ssPreviousByLabel.TryGetValue(sr.Label1, out List<string>? prevToLabel1))
            {
                prevToLabel1 = new();
                ssPreviousByLabel[sr.Label1] = prevToLabel1;
            }
            if (!ssPreviousByLabel.TryGetValue(sr.Label2, out List<string>? prevToLabel2))
            {
                prevToLabel2 = new();
                ssPreviousByLabel[sr.Label2] = prevToLabel2;
            }
            // Check that the new relationship is consistent.
            if (prevToLabel1.Contains(sr.Label2))
            {
                string consistMsg = $"Inconsistent ordering of snapshots: attempted to set {sr.Label1} {sr.Op} {sr.Label2} " +
                    $"when {sr.Label2} has previously been set before {sr.Label1}.";
                throw new RuleParseException(whole, consistMsg);
            }
            prevToLabel2.Add(sr.Label1);
            // Actually set the relationship.
            if (sr.Op == LaterThanOp || sr.Op == LaterThanOpFancy)
            {
                ss2.SetLaterThan(ss1);
            }
            else if (sr.Op == ModifiedLaterThanOp || sr.Op == ModifiedLaterThanOpFancy)
            {
                ss2.SetModifiedOnceLaterThan(ss1);
            }
            else
            {
                throw new RuleParseException(whole, $"Attempted to set '{sr.Op}' relationship between snapshots.");
            }
        }

        // Associate premises, if there is something to associate with.
        List<(Event, string)> parsedEvents = new();
        foreach (PremisePiece pp in premisePieces)
        {
            (Event? ev, string? evErrMsg) = MessageParser.TryParseEvent(pp.Event);

            if (evErrMsg != null)
            {
                throw new RuleParseException(whole, $"Unable to parse premise event '{pp.Event}': {evErrMsg}");
            }
            parsedEvents.Add((ev!, pp.SnapshotLabel));
        }

        if (ssByLabel.Count == 0)
        {
            foreach ((Event pEv, string ssLabel) in parsedEvents)
            {
                if (ssLabel != "")
                {
                    string noSSErrMsg = $"Premise '{pEv}' associated with snapshot label '{ssLabel}' when there are no snapshots.";
                    throw new RuleParseException(whole, noSSErrMsg);
                }
                Factory.RegisterPremise(pEv);
            }
        }
        else
        {
            foreach ((Event pEv, string ssLabel) in parsedEvents)
            {
                if (ssLabel == string.Empty)
                {
                    throw new RuleParseException(whole, $"Premise '{pEv}' not associated with any snapshot.");
                }
                if (!ssByLabel.ContainsKey(ssLabel))
                {
                    string missingSSLabel = $"Premise '{pEv}' snapshot label '{ssLabel}' does not exist.";
                    throw new RuleParseException(whole, missingSSLabel);
                }
                Factory.RegisterPremises(ssByLabel[ssLabel], pEv);
            }
        }

        // Finally resolve the results, and create rule type based on the result.
        if (result.Parts.Count == 0)
        {
            throw new RuleParseException(whole, "Rule must provide some result on the right-hand-side.");
        }
        if (result.StateConsistent)
        {
            if (result.Parts.Count > 1)
            {
                throw new RuleParseException(whole, "State consistent rule can only have one resulting event.");
            }
            (Event? resultEv, string? resultErr) = MessageParser.TryParseEvent(result.Parts[0]);
            if (resultErr != null)
            {
                throw new RuleParseException(whole, $"Could not parse result '{result.Parts[0]}': {resultErr}");
            }
            return Factory.CreateStateConsistentRule(resultEv!);
        }
        foreach ((string ssLabel, State transferTo) in ParseStateTransferringResult(whole, result.Parts))
        {
            if (ssByLabel.TryGetValue(ssLabel, out Snapshot? ssToExtend))
            {
                if (ssToExtend!.TransfersTo != null)
                {
                    throw new RuleParseException(whole, $"Attempt to extend snapshot '{ssLabel}' more than once for transfer rule.");
                }
                ssToExtend!.TransfersTo = transferTo;
            }
            else
            {
                throw new RuleParseException(whole, $"Snapshot label '{ssLabel}' on right-hand-side does not exist.");
            }
        }

        return Factory.CreateStateTransferringRule();
    }

    private static List<(string, State)> ParseStateTransferringResult(string whole, List<string> parts)
    {
        List<(string, State)> parsed = new();
        foreach (string p in parts)
        {
            string[] transferComponents = p.Split(':');
            if (transferComponents.Length != 2)
            {
                throw new RuleParseException(whole, $"Could not parse '{p}' into a snapshot label-state pair: no separator.");
            }
            (State? s, string? stateParseErrMsg) = MessageParser.TryParseState(transferComponents[1]);
            if (stateParseErrMsg != null)
            {
                throw new RuleParseException(whole, $"Could not parse '{p}' into a snapshot label-state pair: {stateParseErrMsg}");
            }
            parsed.Add((transferComponents[0], s!));
        }
        return parsed;
    }

    #endregion

    #endregion
}

/// <summary>
/// Exception thrown when the textual description of a rule defies what is required or expected 
/// by the parser.
/// </summary>
public class RuleParseException : Exception
{
    /// <summary>
    /// Creates a new RuleParseException. An error message is composed based on the parameters
    /// provided.
    /// </summary>
    /// <param name="givenRule">The full rule with the error.</param>
    /// <param name="err">A description of the error encountered.</param>
    public RuleParseException(string givenRule, string err) :
        base($"Error found while parsing '{givenRule}': {err}")
    { }
}
