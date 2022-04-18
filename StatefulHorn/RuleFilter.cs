using System.Collections.Generic;
using System.Linq;

namespace StatefulHorn;

internal delegate bool RuleFilterFunction(Rule r);

public class RuleFilter
{

    public RuleFilter() : this("") { }

    public RuleFilter(string filterSpec)
    {
        IndividualFilters = new();

        List<string> terms = SplitIntoTerms(filterSpec.Trim());
        foreach (string term in terms)
        {
            (MessageParser.Result? result, string? _) = MessageParser.TryParse(term, "search text");
            if (result != null) // Means we something like "Container(Inner)"
            {
                if (result.IsEvent())
                {
                    (Event? ev, string? _) = MessageParser.TryParseEvent(term);
                    IndividualFilters.Add((Rule r) => r.ContainsEvent(ev!));
                }
                else
                {
                    (State? st, string? _) = MessageParser.TryParseState(term);
                    if (st != null)
                    {
                        IndividualFilters.Add((Rule r) => r.ContainsState(st!));
                    }
                    (IMessage? msg, string? _) = MessageParser.TryParseMessage(term);
                    if (msg != null)
                    {
                        IndividualFilters.Add((Rule r) => r.ContainsMessage(msg!));
                    }
                }
            }
            else
            {
                (IMessage? simpleMsg, string? _) = MessageParser.TryParseMessage(term);
                if (simpleMsg != null)
                {
                    IndividualFilters.Add((Rule r) => r.ContainsMessage(simpleMsg!));
                }
            }
        }
    }

    private static List<string> SplitIntoTerms(string input)
    {
        List<string> terms = new();
        int bracketIndent = 0;
        int lastStart = 0;
        int i = 0;
        for (; i < input.Length; i++)
        {
            if (input[i] == '(')
            {
                bracketIndent++;
            }
            else if (input[i] == ')')
            {
                bracketIndent--;
            }
            else if (input[i] == ',' && bracketIndent == 0)
            {
                terms.Add(input[lastStart..i]);
                lastStart = i + 1;
            }
        }
        if (lastStart < i)
        {
            terms.Add(input[lastStart..i]);
        }
        terms.RemoveAll((string t) => t == string.Empty);
        return terms;
    }


    /// <summary>
    /// Returns a RuleFilter that will definitely correspond with the input if it is valid. The 
    /// RuleFilter class constructor will always return a working filter, but sometimes this
    /// means that the user may provide input (e.g. "(") but that a pass-through filter is 
    /// returned. This method detects cases like this and prevents any filter from being 
    /// returned.
    /// </summary>
    /// <param name="spec">Input string defining the new filter.</param>
    /// <returns>
    ///   Null if the filter returned is a pass-through despite filter text being provided.
    /// </returns>
    /// <seealso cref="RuleFilter.RuleFilter(string)"/>
    public static RuleFilter? CreateValid(string spec)
    {
        RuleFilter rf = new(spec);
        return spec.Trim() != string.Empty && rf.IsPassthrough ? null: rf;
    }

    private readonly List<RuleFilterFunction> IndividualFilters;

    public bool IsPassthrough => IndividualFilters.Count == 0;

    public List<Rule> Filter(IEnumerable<StateConsistentRule> cRules, IEnumerable<StateTransferringRule> tRules) 
    {
        if (IsPassthrough)
        {
            return Sort(new(cRules), new(tRules));
        }
        List<StateConsistentRule> scRules = new(from c in cRules where DoFilter(c) select c);
        List<StateTransferringRule> stRules = new(from t in tRules where DoFilter(t) select t);
        return Sort(scRules, stRules);
    }

    private bool DoFilter(Rule r)
    {
        foreach (RuleFilterFunction f in IndividualFilters)
        {
            if (f(r))
            {
                return true;
            }
        }
        return false;
    }

    private static List<Rule> Sort(List<StateConsistentRule> cRules, List<StateTransferringRule> tRules)
    {
        List<Rule> sortedList = new();
        cRules.Sort(RuleComparison);
        sortedList.AddRange(cRules);
        sortedList.AddRange(tRules);
        return sortedList;
    }

    private static int RuleComparison(StateConsistentRule r1, StateConsistentRule r2)
    {
        if (r1.Result.EventType == r2.Result.EventType)
        {
            return r1.Result.ToString().CompareTo(r2.Result.ToString());
        }
        else if (r1.Result.EventType == Event.Type.Leak)
        {
            return -1;
        }
        else if (r1.Result.EventType == Event.Type.Accept)
        {
            if (r2.Result.EventType == Event.Type.Leak)
            {
                return 1;
            }
            else
            {
                return -1;
            }
        }
        else
        {
            return 1;
        }
    }

}
