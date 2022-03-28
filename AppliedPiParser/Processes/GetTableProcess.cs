using System.Collections.Generic;
using System.Linq;

namespace AppliedPi.Processes;

public class GetTableProcess : IProcess
{
    public GetTableProcess(string table, List<(bool, string)> maList)
    {
        TableName = table;
        MatchAssignList = maList;
    }

    public override string ToString()
    {
        List<string> formattedMatches = new();
        foreach ((bool isMatch, string name) in MatchAssignList)
        {
            formattedMatches.Add(isMatch ? "=" + name : name);
        }
        return $"get {TableName}(" + string.Join(", ", formattedMatches) + ")";
    }

    public string TableName { get; init; }

    public List<(bool, string)> MatchAssignList;

    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is GetTableProcess gtp &&
            TableName.Equals(gtp.TableName) &&
            MatchAssignList.SequenceEqual(gtp.MatchAssignList);
    }

    public override int GetHashCode() => TableName.GetHashCode();

    public static bool operator ==(GetTableProcess p1, GetTableProcess p2) => p1.Equals(p2);

    public static bool operator !=(GetTableProcess p1, GetTableProcess p2) => !p1.Equals(p2);

    #endregion
    #region IProcess implementation.

    public IProcess? Next { get; set; }

    public IEnumerable<string> Terms() => from ma in MatchAssignList select ma.Item2;

    public IProcess ResolveTerms(SortedList<string, string> subs)
    {
        List<(bool, string)> newMAList = new(from ma in MatchAssignList
                                             select (ma.Item1, subs.GetValueOrDefault(ma.Item2, ma.Item2)));
        return new GetTableProcess(TableName, newMAList);
    }

    #endregion
}
