using System.Collections.Generic;

namespace AppliedPi.Statements;

/// <summary>
/// Convenience methods that are used by multiple Statement classes. The simmilarity between 
/// Statements is not so great as to warrant the use of inheritence, so we keep such methods
/// in this namespace.
/// </summary>
internal static class StatementUtils
{

    public static string ParameterTypeListToString(SortedList<string, string> paramTypes)
    {
        List<string> paramPairs = new(paramTypes.Count);
        foreach (KeyValuePair<string, string> paramType in paramTypes)
        {
            paramPairs.Add($"{paramType.Key}: {paramType.Value}");
        }
        return string.Join(", ", paramPairs);
    }

}
