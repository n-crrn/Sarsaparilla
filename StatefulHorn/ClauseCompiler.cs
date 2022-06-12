using System;
using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

/// <summary>
/// A class that accepts a source file containing a list of Stateful Horn clauses, and processes
/// them to return a series of clause statements. This compiler continually calls-back events
/// to allow quick user-feedback of its progress.
/// </summary>
public class ClauseCompiler : IClauseCompiler
{

    #region Event handlers

    public event Action<IClauseCompiler>? OnReset;

    public event Action<IClauseCompiler, RuleAddedArgs>? OnRuleAddition;

    public event Action<IClauseCompiler, string>? OnError;

    public event Action<IClauseCompiler, QueryEngine?, string?>? OnComplete;

    #endregion

    public void Compile(string inputSrc)
    {
        List<(int Line, string Source)> foundClauses = new();
        RuleParser parser = new();
        OnReset?.Invoke(this);

        string[] lines = inputSrc.Split("\n");
        for (int lineOffset = 0; lineOffset < lines.Length; lineOffset++)
        {
            string thisLineClean = UncommentLine(lines[lineOffset]);
            if (thisLineClean.Length != 0)
            {
                thisLineClean = UncommentLine(thisLineClean);

                // In case this rule is broken over several lines, collect the pieces.
                int lineEndOffset = lineOffset;
                while (thisLineClean.EndsWith("\\") && (lineEndOffset + 1) < lines.Length)
                {
                    lineEndOffset++;
                    thisLineClean = thisLineClean + " " + UncommentLine(lines[lineEndOffset]);
                }

                int lineNumber = lineOffset + 1;
                foundClauses.Add((lineNumber, thisLineClean));
            }
        }

        HashSet<State>? initStates = null;
        IMessage? leak = null;
        State? when = null;
        int maxElab = -1;

        List<Rule> basisRules = new();
        foreach ((int line, string src) in foundClauses)
        {
            // Check if it is a init or query line before attempting to parse it as a rule.
            if (IsQueryLine(src))
            {
                if (leak != null || when != null)
                {
                    OnError?.Invoke(this, "Attempted to specify multiple queries, only first one noted.");
                }
                else
                {
                    string? queryErr;
                    (leak, when, queryErr) = ParseQueryStatement(src);
                    if (queryErr != null)
                    {
                        OnError?.Invoke(this, queryErr);
                    }
                }
            }
            else if (IsInitLine(src))
            {
                if (initStates != null)
                {
                    OnError?.Invoke(this, "Attempted to specify multiple init statements, only first one noted.");
                }
                else
                {
                    string? initErr;
                    (initStates, initErr) = ParseInitStatement(src);
                    if (initErr != null)
                    {
                        OnError?.Invoke(this, initErr);
                    }
                }
            }
            else if (IsLimitLine(src))
            {
                (int elabLimit, string? limitErr) = ParseLimit(src);
                if (limitErr != null)
                {
                    OnError?.Invoke(this, limitErr);
                }
                else
                {
                    maxElab = elabLimit;
                }
            }
            else
            {
                try
                {
                    Rule result = parser.Parse(src);
                    basisRules.Add(result);
                    OnRuleAddition?.Invoke(this, new(line, src, result, null));
                }
                catch (Exception parseEx)
                {
                    OnRuleAddition?.Invoke(this, new(line, src, null, parseEx.Message));
                }
            }
        }

        if (initStates == null)
        {
            OnComplete?.Invoke(this, null, "Initial states need to be set for analysis.");
        }
        if (leak == null)
        {
            OnComplete?.Invoke(this, null, "Query needs to be specified for analysis.");
        }
        if (initStates != null && leak != null)
        {
            OnComplete?.Invoke(this, new QueryEngine(initStates, leak, when, basisRules, maxElab), null);
        }
    }

    private static string UncommentLine(string line) => line.Split("//").First().Trim();

    private readonly static string QueryPrefix = "query leak ";
    private readonly static string WhenConnector = " when ";
    private readonly static string InitPrefix = "init ";
    private readonly static string LimitPrefix = "limit ";

    private static bool IsQueryLine(string cleanLine) => cleanLine.StartsWith(QueryPrefix);

    private static (IMessage?, State?, string?) ParseQueryStatement(string cleanLine)
    {
        string line = cleanLine[QueryPrefix.Length..];
        string[] parts = line.Split(WhenConnector);
        (IMessage? msg, string? err) = MessageParser.TryParseMessage(parts[0]);
        if (err != null)
        {
            return (null, null, err);
        }
        State? when = null;
        if (parts.Length == 2)
        {
            (when, err) = MessageParser.TryParseState(parts[1]);
            if (err != null)
            {
                return (null, null, err);
            }
        }
        else if (parts.Length > 2)
        {
            return (null, null, "Multiple 'when' keywords in query statement.");
        }
        return (msg, when, null);
    }
    

    private static bool IsInitLine(string cleanLine) => cleanLine.StartsWith(InitPrefix);

    private static (HashSet<State>?, string?) ParseInitStatement(string cleanLine)
    {
        string line = cleanLine[InitPrefix.Length..];
        return MessageParser.TryParseStates(line);
    }

    private static bool IsLimitLine(string cleanLine) => cleanLine.StartsWith(LimitPrefix);

    private static (int, string?) ParseLimit(string cleanLine)
    {
        string number = cleanLine[LimitPrefix.Length..];
        if (int.TryParse(number, out int value) && value > 0)
        {
            return (value, null);
        }
        return (-1, "Limit value was not a valid number.");
    }

}
