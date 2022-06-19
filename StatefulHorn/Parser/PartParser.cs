using StatefulHorn.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace StatefulHorn.Parser;

/// <summary>
/// Namespace for methods dedicated to transforming correctly formatted strings into IMessage
/// carrying structures such as States, Events and Messages. These methods are contained in
/// the same namespace as their parsing code shares algorithms.
/// </summary>
public static class PartParser
{
    #region Public interfaces - methods for parsing states, events and messages.

    /// <summary>
    /// Attempt to parse a text description of a state into an in-memory representation of a
    /// state.
    /// </summary>
    /// <param name="input">Text description of a state. For instance, "SD(init[])".</param>
    /// <returns>
    ///   A tuple of two values, with S representing the state if it could be parsed. Otherwise,
    ///   Error is set to a message explaining why the text description was invalid.
    ///   Either S or Error will be set to null under all conditions.
    /// </returns>
    public static (State? S, string? Error) TryParseState(string input)
    {
        (Result? rc, string? err) = TryParse(input, "state");
        if (rc != null)
        {
            if (rc.Messages.Count > 1)
            {
                return (null, $"A state can only contain one message, {rc.Messages.Count} provided.");
            }
            return (new(rc.Container, rc.Messages[0]), null);
        }
        return (null, err);
    }

    /// <summary>
    /// Parse the given text description of a state into an in-memory representation, or throw an
    /// exception if the description is malformed.
    /// </summary>
    /// <param name="input">Textual description of the state.</param>
    /// <returns>An in-memory representation of the state.</returns>
    /// <exception cref="ArgumentException">Description does not make sense.</exception>
    public static State ParseState(string input)
    {
        (State? s, string? err) = TryParseState(input);
        if (s == null)
        {
            throw new ArgumentException($"Invalid state {input}: {err!}");
        }
        return s;
    }

    /// <summary>
    /// Attempt to parse a comma-separated list of state descriptions. If all descriptions are 
    /// parsed correct, a set of the states will be returned. Otherwise, an error message will
    /// be provided. This method will check that no state cell (e.g. "cell1(...)") is not 
    /// specified multiple times.
    /// </summary>
    /// <param name="input">The string containing the list of state descriptions.</param>
    /// <returns>
    /// A tuple, with the first element being the set of states found and the second a description
    /// of any errors encountered during the parse. Either the set will be null or the description
    /// will be null.
    /// </returns>
    public static (HashSet<State>?, string?) TryParseStates(string input)
    {
        List<string> parts = new();
        int indent = 0;
        int lastStart = 0;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '(' || c == '<' || c == '[')
            {
                indent++;
            }
            else if (c == ')' || c == '>' || c == ']')
            {
                indent--;
            }
            else if (c == ',' && indent == 0)
            {
                parts.Add(input[lastStart..i]);
                lastStart = i + 1;
            }
            if (indent < 0)
            {
                return (null, "Incorrectly formatted state(s) specified.");
            }
        }
        parts.Add(input[lastStart..]);

        if (parts.Count == 0)
        {
            return (null, "No states specified.");
        }

        HashSet<State> states = new();
        HashSet<string> names = new(); // Used for checking that all state cell references are unique.
        foreach (string p in parts)
        {
            (State? s, string? err) = TryParseState(p);
            if (err != null)
            {
                return (null, err);
            }
            Debug.Assert(s != null);
            if (!states.Add(s))
            {
                return (null, "Same state specified multiple times.");
            }
            if (!names.Add(s.Name))
            {
                return (null, $"State cell named '{s.Name}' referenced multiple times.");
            }
        }
        return (states, null);
    }

    /// <summary>
    /// Attempt to parse a text description of an event into an in-memory representation of an
    /// event. Note that such descriptions can use acronyms for events: for instance, a know
    /// event for message msg can be represented as "k(msg)".
    /// </summary>
    /// <param name="input">Text description of an event.</param>
    /// <returns>
    ///   A tuple of two values, with Ev representing the event if it could be parsed. Otherwise,
    ///   Error is set to a message explaining why the text description was invalid.
    ///   Either Ev or Error will be set to null under all conditions.
    /// </returns>
    public static (Event? Ev, string? Error) TryParseEvent(string input)
    {
        (Result? rc, string? err) = TryParse(input, "event");

        if (rc == null)
        {
            return (null, err);
        }

        switch (rc.Container)
        {
            case "know":
            case "k":
                if (rc.Messages.Count != 1)
                {
                    string errMsg = rc.Messages.Count == 0 ? "There must be a message to know." : "Only one message can be known at a time.";
                    return (null, errMsg);
                }
                return (Event.Know(rc.Messages[0]), null);
            case "new":
            case "n":
                if (rc.Messages.Count != 1)
                {
                    return (null, "There must be a nonce to create.");
                }
                if (rc.Messages[0] is NonceMessage rcName)
                {
                    return (Event.New(rcName), null);
                }
                return (null, "Wrong types for new event: must be nonce type.");
            case "make":
            case "m":
                if (rc.Messages.Count != 1)
                {
                    string errMsg = rc.Messages.Count == 0 ? "There must be a message to make." : "Only one message can be made at a time.";
                    return (null, errMsg);
                }
                return (Event.Make(rc.Messages[0]), null);
            default:
                return (null, $"Unrecognised event '{rc.Container}'.");
        }
    }

    /// <summary>
    /// Attempt to parse a text description of a message into an in-memory representation of a
    /// message.
    /// </summary>
    /// <param name="input">Text description of a message.</param>
    /// <returns>
    ///   A tuple of two values, with M representing the message if it could be parsed.
    ///   Otherwise, Error is set to a message explaining why the text description was invalid.
    ///   Either M or Error will be set to null under all conditions.
    /// </returns>
    public static (IMessage? M, string? Error) TryParseMessage(string input)
    {
        (IMessage? msg, int endPos, string? err) = TryParseLevel(input, 0);
        if (err == null && endPos != input.Length && !RemainderIsWhiteSpace(input, endPos))
        {
            err = "Extra text included after message.";
        }
        return (msg, err);
    }

    /// <summary>
    /// Parse a text description of a message into an in-memory representation, or throw an
    /// exception if the message is invalid.
    /// </summary>
    /// <param name="input">Text description of a message.</param>
    /// <returns>The parsed message.</returns>
    /// <exception cref="ArgumentException">Textual description did not make sense.</exception>
    public static IMessage ParseMessage(string input)
    {
        (IMessage? m, string? err) = TryParseMessage(input);
        if (err != null)
        {
            throw new ArgumentException($"Invalid message {input}: {err}");
        }
        return m!;
    }

    #endregion
    #region Convenience types and methods.

    /// <summary>
    /// Used to determine if a part parsed should be considered an event or a message. See
    /// Result.IsEvent() for more details.
    /// </summary>
    private static readonly List<string> EventContainers = new()
    {
        "know", "k", "new", "n", "make", "m"
    };

    /// <summary>
    /// This is a privately declared record type that makes the declaration of the TryParse method
    /// below cleaner.
    /// </summary>
    /// <param name="Container">
    ///   Name of the event or state. For instance, for "SD(init[])", "SD" is the Container.
    /// </param>
    /// <param name="Messages">
    ///   The list of messages read as part of the event or state. For instance, for "SD(init[])"
    ///   the list of messages would equal List&lt;IMessage&gt; { NameMessage("init") }.
    /// </param>
    internal record Result(string Container, List<IMessage> Messages)
    {

        /// <summary>
        /// Indicates if this Result is describing an event instead of a method.
        /// </summary>
        /// <returns>True if the outer function call is an event.</returns>
        public bool IsEvent() => EventContainers.Contains(Container);

    }

    /// <summary>
    /// Attempts to parse the given input string into an encapsulating text and a message.
    /// This encapsulating text takes the form that events and states take, where they
    /// have a name and have a message contained in brackets. For instance, "SD(init[])"
    /// is a state "SD" containing a NameMessage of "init". The input string is expected
    /// to only contain that text and message.
    /// </summary>
    /// <remark>
    /// This method has an internal visibility as it is used by the RuleFilter at a point
    /// where it is not certain that the user is looking for a message, event or state.
    /// </remark>
    /// <param name="input">Input string to parse.</param>
    /// <param name="type">
    ///   Type being read. This is used to create appropriate error messages.
    /// </param>
    /// <returns>
    ///   A tuple of the Content record found and an Error string. One or the other will
    ///   be null: if an error is found, then the Error will not be null.
    /// </returns>
    internal static (Result? Content, string? Error) TryParse(string input, string type)
    {
        int i = 0;
        // Skip whitespace.
        while (i < input.Length && char.IsWhiteSpace(input[i]))
        {
            i++;
        }

        // Ensure that there is a name to parse.
        if (i == input.Length)
        {
            return (null, $"Nothing to parse when attempting to read {type}.");
        }

        // Read the name of the state or event.
        StringBuilder containerBuffer = new();
        while (i < input.Length && '(' != input[i])
        {
            containerBuffer.Append(input[i]);
            i++;
        }

        // There must be a list of messages following. If there aren't, then return an error.
        if (i == input.Length)
        {
            return (null, $"Failed to include messages with {type} {containerBuffer}.");
        }

        // Read the following messages, and construct 
        List<IMessage>? containedMsgs;
        string? errMsg;
        (containedMsgs, i, errMsg) = TryParseList(input, i + 1, ')', type);
        if (errMsg == null && !RemainderIsWhiteSpace(input, i))
        {
            errMsg = "Extra text included after input.";
        }

        return (errMsg == null ? new Result(containerBuffer.ToString().Trim(), containedMsgs!) : null, errMsg);
    }

    /// <summary>
    /// Attempts to parse a term, which may itself lead to another sub-list of terms being parsed.
    /// </summary>
    /// <param name="input">The input source string.</param>
    /// <param name="i">
    /// Start position for the parse. There may be whitespace before the next term.
    /// </param>
    /// <returns>
    /// A tuple containing three values: the Message found, the offset to the end position
    /// of the list and an Error description. If an error is found, Error is set to a description
    /// of the error is Message is set to null; the EndPosn is set to where the error was found.
    /// Otherwise, Message is set to the message term parsed, Error is set to null and 
    /// the EndPosn is set to the offset immediately after the end of the term.
    /// </returns>
    private static (IMessage? Message, int EndPosn, string? Error) TryParseLevel(string input, int i)
    {
        // It is a feature of this method that we create new strings for the
        // message structures. This will allow the release of resources
        // associated with the input source.
        StringBuilder buffer = new();
        List<IMessage>? innerMsgs; // Used for returns from recursive calls.
        string? errMsg;            // Used for returns from recursive calls.

        i = SkipWhiteSpace(input, i);
        if (i == input.Length) // Unexpected end of input - an error condition.
        {
            return (null, i, "Unexpected end of input.");
        }

        // Do we have a nonce value? If so, read and return it.
        if ('[' == input[i])
        {
            i++;
            while (i < input.Length && ']' != input[i])
            {
                buffer.Append(input[i]);
                i++;
            }
            return (new NonceMessage(buffer.ToString().Trim()), i + 1, null);
        }

        // Do we have a tuple value? Go through and read the parts of it.
        if ('<' == input[i])
        {
            (innerMsgs, i, errMsg) = TryParseList(input, i + 1, '>', "tuple");
            return (innerMsgs != null ? new TupleMessage(innerMsgs) : null, i, errMsg);
        }

        // Otherwise, read the text of the message in preparation for determining
        // its type.
        buffer.Append(input[i]);
        i++;
        while (i < input.Length && !('[' == input[i] || '(' == input[i] || ')' == input[i] || ',' == input[i] || '>' == input[i]))
        {
            buffer.Append(input[i]);
            i++;
        }

        // Is it a variable? If so, return it.
        if (i == input.Length || ')' == input[i] || ',' == input[i] || '>' == input[i])
        {
            return (new VariableMessage(buffer.ToString().Trim()), i, null);
        }

        // It is a name? If so, increment past the closing square bracket and return it.
        if ('[' == input[i])
        {
            if (i + 1 >= input.Length || ']' != input[i + 1])
            {
                return (null, i + 1, "Malformed nonce value, open square bracket without closing bracket.");
            }
            i += 2;
            return (new NameMessage(buffer.ToString().Trim()), i, null);
        }

        // Only remaining option means that it is a function (input[i] == '(').
        i++;
        (innerMsgs, i, errMsg) = TryParseList(input, i, ')', "function");
        return (innerMsgs != null ? new FunctionMessage(buffer.ToString().Trim(), innerMsgs) : null, i, errMsg);
    }

    /// <summary>
    /// Attempts to parse a list of terms. It is expected that <c>input[i - 1] == '('</c>, and the
    /// next text will be a comma separated list of terms.
    /// </summary>
    /// <param name="input">The input source string.</param>
    /// <param name="i">Where to commence the list parsing.</param>
    /// <param name="endChar">Whether a ')' or '&gt;' is the expected final character.</param>
    /// <param name="type">
    /// Description of the type being parsed, so that any errors can be documented better.
    /// </param>
    /// <returns>
    /// A tuple containing three values: the list of Messages found, the offset to the end position
    /// of the list and an Error description. If an error is found, Error is set to a description
    /// of the error is Messages is set to null; the EndPosn is set to where the error was found.
    /// Otherwise, Messages is set to the list of messages parsed, Error is set to null and 
    /// the EndPosn is set to the offset immediately after the ")" character.
    /// </returns>
    private static (List<IMessage>? Messages, int EndPosn, string? Error) TryParseList(string input, int i, char endChar, string type)
    {
        List<IMessage> innerMsgs = new();
        while (i < input.Length)
        {
            IMessage? msg;
            string? errMsg;

            (msg, i, errMsg) = TryParseLevel(input, i);
            if (errMsg != null)
            {
                return (null, i, errMsg);
            }
            innerMsgs.Add(msg!);

            i = SkipWhiteSpace(input, i);

            if (i == input.Length)
            {
                // There should be a final ')' - if there isn't, then this is malformatted.
                return (null, i, $"Badly formatted {type} '{input}' - no final bracket.");
            }
            else if (endChar == input[i])
            {
                break;
            }
            else if (',' == input[i])
            {
                i++;
            }
            else
            {
                return (null, i, $"Badly formatted {type} '{input}' - expected closing bracket or comma.");
            }
        }
        if (i == input.Length || endChar != input[i])
        {
            return (null, i, $"Badly formatted {type} '{input}' - function not finished before end of source.");
        }
        return (innerMsgs, i + 1, null);
    }

    /// <summary>
    /// Checks if the rest of the input (after offset startPos) contains only white space.
    /// </summary>
    /// <param name="input">Rule source string.</param>
    /// <param name="startPos">The offset to start the check.</param>
    /// <returns>True if there is whitespace after offset startPos; false otherwise.</returns>
    private static bool RemainderIsWhiteSpace(string input, int startPos)
    {
        int i = SkipWhiteSpace(input, startPos);
        return i >= input.Length;
    }

    /// <summary>
    /// Increment offsetspace until either the end of the input is found or a non-white-space 
    /// character is found.
    /// </summary>
    /// <param name="input">Rule source string.</param>
    /// <param name="startPos">The offset to start looking for a non-white-space character.</param>
    /// <returns>
    /// Offset of the first non-white-space character, or the length of the input string.
    /// </returns>
    private static int SkipWhiteSpace(string input, int startPos)
    {
        int i = startPos;
        while (i < input.Length && char.IsWhiteSpace(input[i]))
        {
            i++;
        }
        return i;
    }

    #endregion
}
