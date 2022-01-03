using System;
using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// A class that accepts a source file containing a list of Stateful Horn clauses, and processes
/// them to return a series of clause statements. This compiler continually calls-back events
/// to allow quick user-feedback of its progress.
/// </summary>
public class ClauseCompiler : IClauseCompiler
{

    public ClauseCompiler()
    {
        FoundClauses = new();
        BasisRules = new();
    }

    #region Event handlers

    public event Action<IClauseCompiler>? OnReset;

    public event Action<IClauseCompiler, RuleAddedArgs>? OnRuleAddition;

    public event Action<IClauseCompiler, RuleUpdateArgs>? OnRuleUpdate;

    public event Action<IClauseCompiler, string>? OnGeneralWarning;

    public event Action<IClauseCompiler, Universe>? OnComplete;

    #endregion

    private readonly List<(int Line, string ClauseSource)> FoundClauses;

    private readonly List<Rule> BasisRules;

    public Universe Compile(string inputSrc)
    {
        FoundClauses.Clear();
        RuleParser parser = new();
        OnReset?.Invoke(this);

        string[] lines = inputSrc.Split("\n");
        for (int lineOffset = 0; lineOffset < lines.Length; lineOffset++)
        {
            string thisLineClean = lines[lineOffset].Trim();
            if (thisLineClean.Length != 0 && !thisLineClean.StartsWith("//"))
            {
                // In case this rule is broken over several lines, collect the pieces.
                int lineEndOffset = lineOffset;
                while (thisLineClean.EndsWith("\\") && (lineEndOffset + 1) < lines.Length)
                {
                    lineEndOffset++;
                    thisLineClean = thisLineClean + " " + lines[lineEndOffset].Trim();
                }

                int lineNumber = lineOffset + 1;
                FoundClauses.Add((lineNumber, thisLineClean));
                OnRuleAddition?.Invoke(this, new(lineNumber, thisLineClean));
            }
        }

        BasisRules.Clear();
        foreach ((int line, string src) in FoundClauses)
        {
            try
            {
                Rule result = parser.Parse(src);
                BasisRules.Add(result);
                OnRuleUpdate?.Invoke(this, new(line, result, null));
            }
            catch (Exception parseEx)
            {
                OnRuleUpdate?.Invoke(this, new(line, null, parseEx.Message));
            }
        }

        Universe newRuleUniverse = new(BasisRules);
        if (newRuleUniverse.BasisRulesHaveRedundancy)
        {
            OnGeneralWarning?.Invoke(this, "One or more basis rules imply other rules. Please check elaboration log.");
        }
        OnComplete?.Invoke(this, newRuleUniverse);
        return newRuleUniverse;
    }

}
